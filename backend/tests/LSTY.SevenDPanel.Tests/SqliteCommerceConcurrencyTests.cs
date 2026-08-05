using System;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Commerce;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteCommerceConcurrencyTests
    {
        [Fact]
        public async Task Concurrent_buyers_of_the_last_item_create_only_one_reserved_order()
        {
            using var database = new RewardTestDatabase();
            var store = Prepare(database);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            economy.GetOrCreatePlayerAccount("EOS-a", "open-a", 100, Now);
            economy.GetOrCreatePlayerAccount("EOS-b", "open-b", 100, Now);
            store.SaveProduct(new ShopProductDraft(
                "last-item", "Last Item", string.Empty, true, 10, 1, null,
                "starter-package", 0), Now);

            var results = await Task.WhenAll(
                Task.Run(() => store.ReservePurchase(Purchase("a", "EOS-a"))),
                Task.Run(() => store.ReservePurchase(Purchase("b", "EOS-b"))));

            Assert.Single(results.Where(result =>
                result.Status == PurchaseReservationStatus.Reserved && result.Created));
            Assert.Single(results.Where(result =>
                result.Status == PurchaseReservationStatus.OutOfStock));
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM shop_purchases WHERE state = 'Reserved';"));
            Assert.Equal(0, connection.ExecuteScalar<long>(
                "SELECT stock_remaining FROM shop_products WHERE product_id = 'last-item';"));
        }

        [Fact]
        public async Task Concurrent_same_code_same_player_returns_one_authoritative_attempt()
        {
            using var database = new RewardTestDatabase();
            var store = Prepare(database);
            const string normalized = "ABCDEFGHJKLMNPQR";
            store.SaveRedeemCode(new RedeemCodeSecretDraft(
                "code-1",
                RedeemCodeCodec.Digest(normalized),
                "NPQR",
                RedeemCodeCodec.NormalizationVersion,
                "starter-package",
                true,
                null,
                null,
                10,
                1), Now);

            var results = await Task.WhenAll(
                Task.Run(() => store.ReserveRedemption(Redemption("attempt-a", normalized))),
                Task.Run(() => store.ReserveRedemption(Redemption("attempt-b", normalized))));

            Assert.Single(results.Where(result => result.Created));
            Assert.Single(results.Where(result => !result.Created));
            Assert.Equal(results[0].Attempt!.AttemptId, results[1].Attempt!.AttemptId);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM redeem_attempts WHERE code_id = 'code-1' AND crossplatform_id = 'EOS-player';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT redemption_count FROM redeem_codes WHERE code_id = 'code-1';"));
        }

        [Fact]
        public void Daily_claim_replay_and_stale_row_versions_preserve_the_authoritative_state()
        {
            using var database = new RewardTestDatabase();
            var store = Prepare(database);
            var claim = new DailyRewardClaimDraft(
                "claim-1",
                "daily-rule",
                "starter-package",
                "EOS-player",
                "2026-07-27",
                Now,
                Now.AddDays(1),
                "daily-key",
                42,
                "world-1",
                "daily-correlation",
                Now);

            var first = store.GetOrCreateDailyRewardClaim(claim);
            var replay = store.GetOrCreateDailyRewardClaim(claim);

            Assert.True(first.Created);
            Assert.False(replay.Created);
            Assert.Equal(first.Claim.ClaimId, replay.Claim.ClaimId);
            Assert.Equal(first.Claim.RowVersion, replay.Claim.RowVersion);
            Assert.False(store.TryStartDailyRewardClaim(
                claim.ClaimId,
                first.Claim.RowVersion + 1,
                Now.AddSeconds(1)));
            Assert.True(store.TryStartDailyRewardClaim(
                claim.ClaimId,
                first.Claim.RowVersion,
                Now.AddSeconds(1)));

            var dispatching = store.GetDailyRewardClaim(claim.ClaimId);
            Assert.False(store.TryResolveDailyRewardClaim(
                claim.ClaimId,
                first.Claim.RowVersion,
                DailyRewardClaimState.Completed,
                "grant-stale",
                null,
                Now.AddSeconds(2)));
            Assert.True(store.TryResolveDailyRewardClaim(
                claim.ClaimId,
                dispatching.RowVersion,
                DailyRewardClaimState.Failed,
                null,
                "grant-failed",
                Now.AddSeconds(2)));

            var resolved = store.GetDailyRewardClaim(claim.ClaimId);
            Assert.Equal(DailyRewardClaimState.Failed, resolved.State);
            Assert.Equal("grant-failed", resolved.ErrorCode);
            Assert.Equal(dispatching.RowVersion + 1, resolved.RowVersion);
        }

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

        private static SqliteCommerceStore Prepare(RewardTestDatabase database)
        {
            var rewardStore = new SqliteRewardStore(database.ConnectionFactory);
            new SaveRewardPackageUseCase(rewardStore, RewardTestCatalog.Available()).Execute(
                new RewardPackageDraft(
                    "starter-package",
                    "Starter Package",
                    string.Empty,
                    true,
                    0,
                    new[]
                    {
                        RewardPackageEntryDraft.Item(
                            "starter-item", "medicalBandage", GameResourceKind.Item,
                            1, null, null, "catalog-v1")
                    }));
            return new SqliteCommerceStore(database.ConnectionFactory);
        }

        private static PurchaseReservationRequest Purchase(string suffix, string player) =>
            new PurchaseReservationRequest(
                "purchase-" + suffix,
                "reservation-" + suffix,
                "last-item",
                player,
                1,
                "purchase-key-" + suffix,
                "purchase-correlation-" + suffix,
                Now,
                Now.AddMinutes(5));

        private static RedeemReservationRequest Redemption(string attemptId, string normalized) =>
            new RedeemReservationRequest(
                attemptId,
                RedeemCodeCodec.Digest(normalized),
                RedeemCodeCodec.NormalizationVersion,
                "EOS-player",
                "redeem-correlation",
                Now);
    }
}
