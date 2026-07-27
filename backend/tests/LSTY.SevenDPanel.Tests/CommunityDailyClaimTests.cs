using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Commerce;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Application.Rewards;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityDailyClaimTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 4, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Concurrent_daily_claims_create_one_authoritative_grant()
        {
            using var database = new RewardTestDatabase();
            var rewards = new SqliteRewardStore(database.ConnectionFactory);
            var commerce = new SqliteCommerceStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewards, catalog).Execute(Package());
            new SaveDailyRewardPolicyUseCase(commerce, rewards).Execute(
                new DailyRewardPolicyDraft("daily-main", "daily-package", true, null));
            var delivery = new RecordingDelivery(RewardDeliveryResult.Succeeded(
                Array.Empty<RewardDeliveryEntryResult>()));
            var grant = new GrantRewardUseCase(rewards, delivery, economy, catalog);
            var claim = new ClaimDailyRewardUseCase(
                commerce,
                grant,
                commerce,
                () => Now,
                () => "daily-claim-1");
            var command = new DailyRewardClaimCommand(
                "daily-main", "EOS-A", 7, "world-1", "daily-correlation");

            var results = await Task.WhenAll(
                claim.ExecuteAsync(command, TestContext.Current.CancellationToken),
                claim.ExecuteAsync(command, TestContext.Current.CancellationToken));

            Assert.Equal(1, delivery.Calls);
            Assert.Single(results, result => result.Status == DailyRewardClaimStatus.Claimed);
            Assert.All(results, result => Assert.Equal(
                results[0].Claim.ClaimId,
                result.Claim.ClaimId));
            Assert.Single(results.Select(result => result.Claim.GrantOperationId).Distinct());
        }

        [Fact]
        public async Task Unknown_daily_grant_is_never_redispatched_after_restart()
        {
            using var database = new RewardTestDatabase();
            var rewards = new SqliteRewardStore(database.ConnectionFactory);
            var commerce = new SqliteCommerceStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewards, catalog).Execute(Package());
            new SaveDailyRewardPolicyUseCase(commerce, rewards).Execute(
                new DailyRewardPolicyDraft("daily-main", "daily-package", true, null));
            var firstDelivery = new RecordingDelivery(RewardDeliveryResult.ResultUnknown(
                Array.Empty<RewardDeliveryEntryResult>()));
            var command = new DailyRewardClaimCommand(
                "daily-main", "EOS-A", 7, "world-1", "daily-unknown");
            var first = new ClaimDailyRewardUseCase(
                commerce,
                new GrantRewardUseCase(rewards, firstDelivery, economy, catalog),
                commerce,
                () => Now,
                () => "daily-claim-unknown");

            var pending = await first.ExecuteAsync(
                command,
                TestContext.Current.CancellationToken);
            var recoveryDelivery = new RecordingDelivery(RewardDeliveryResult.Succeeded(
                Array.Empty<RewardDeliveryEntryResult>()));
            var afterRestart = new ClaimDailyRewardUseCase(
                new SqliteCommerceStore(database.ConnectionFactory),
                new GrantRewardUseCase(rewards, recoveryDelivery, economy, catalog),
                new SqliteCommerceStore(database.ConnectionFactory),
                () => Now,
                () => "unused-claim-id");
            var replay = await afterRestart.ExecuteAsync(
                command,
                TestContext.Current.CancellationToken);

            Assert.Equal(DailyRewardClaimStatus.PendingReconciliation, pending.Status);
            Assert.Equal(DailyRewardClaimStatus.PendingReconciliation, replay.Status);
            Assert.Equal(1, firstDelivery.Calls);
            Assert.Equal(0, recoveryDelivery.Calls);
            Assert.Equal(pending.Claim.GrantOperationId, replay.Claim.GrantOperationId);
        }

        [Fact]
        public async Task Persisted_daily_policy_is_required_and_a_disabled_policy_cannot_dispatch()
        {
            using var database = new RewardTestDatabase();
            var rewards = new SqliteRewardStore(database.ConnectionFactory);
            var commerce = new SqliteCommerceStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewards, catalog).Execute(Package());
            var policies = new SaveDailyRewardPolicyUseCase(commerce, rewards);
            var enabled = policies.Execute(
                new DailyRewardPolicyDraft("daily-main", "daily-package", true, null));
            var disabled = policies.Execute(
                new DailyRewardPolicyDraft(
                    "daily-main", "daily-package", false, enabled.RowVersion));
            var delivery = new RecordingDelivery(RewardDeliveryResult.Succeeded(
                Array.Empty<RewardDeliveryEntryResult>()));
            var claim = new ClaimDailyRewardUseCase(
                commerce,
                new GrantRewardUseCase(rewards, delivery, economy, catalog),
                commerce,
                () => Now,
                () => "daily-claim-disabled");

            await Assert.ThrowsAsync<DailyRewardPolicyUnavailableException>(() => claim.ExecuteAsync(
                new DailyRewardClaimCommand(
                    "daily-main", "EOS-A", 7, "world-1", "daily-disabled"),
                TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<DailyRewardPolicyUnavailableException>(() => claim.ExecuteAsync(
                new DailyRewardClaimCommand(
                    "daily-missing", "EOS-A", 7, "world-1", "daily-missing"),
                TestContext.Current.CancellationToken));

            Assert.False(disabled.Enabled);
            Assert.Equal(1, disabled.RowVersion);
            Assert.Equal(0, delivery.Calls);
        }

        private static RewardPackageDraft Package() => new RewardPackageDraft(
            "daily-package",
            "Daily",
            string.Empty,
            true,
            0,
            new[] { RewardPackageEntryDraft.Currency("daily-currency", 5) });

        private sealed class RecordingDelivery : IRewardDeliveryPort
        {
            private readonly RewardDeliveryResult result;
            private int calls;

            public RecordingDelivery(RewardDeliveryResult result) => this.result = result;

            public int Calls => Volatile.Read(ref calls);

            public Task<RewardDeliveryResult> DeliverAsync(
                RewardDeliveryCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref calls);
                return Task.FromResult(result);
            }
        }
    }
}
