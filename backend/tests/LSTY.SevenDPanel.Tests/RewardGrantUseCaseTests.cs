using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class RewardGrantUseCaseTests
    {
        [Fact]
        public async Task Grant_dispatches_game_effects_before_currency_and_reuses_eligibility()
        {
            using var database = new RewardTestDatabase();
            var rewardStore = new SqliteRewardStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewardStore, catalog).Execute(Package());
            var delivery = new RecordingRewardDeliveryPort(command =>
            {
                Assert.Empty(economy.QueryTransactions(new TransactionKeysetQuery(
                    20, null, null, null, null, null)).Transactions);
                return RewardDeliveryResult.Succeeded(command.Entries
                    .Where(entry => entry.Kind != RewardEntryKind.Currency)
                    .Select(entry => RewardDeliveryEntryResult.Succeeded(
                        entry.OperationEntryId,
                        "player-action-" + entry.OperationEntryId)));
            });
            var useCase = new GrantRewardUseCase(rewardStore, delivery, economy, catalog);

            var first = await useCase.ExecuteAsync(Command("grant-1", "eligibility-1"), CancellationToken.None);
            var duplicateEligibility = await useCase.ExecuteAsync(
                Command("grant-2", "eligibility-1"),
                CancellationToken.None);

            Assert.Equal(GrantOperationState.Completed, first.Operation.State);
            Assert.True(duplicateEligibility.Reused);
            Assert.Equal(first.Operation.OperationId, duplicateEligibility.Operation.OperationId);
            Assert.Equal(1, delivery.Calls);
            var account = economy.QueryAccounts(new AccountKeysetQuery(
                20, false, "EOS-player", null, null, null)).Accounts.Single();
            Assert.Equal(25, account.PostedBalance);
            Assert.All(
                rewardStore.GetGrant(first.Operation.OperationId).Entries,
                entry => Assert.Equal(GrantOperationState.Completed, entry.State));
        }

        [Fact]
        public async Task Unknown_delivery_is_only_reconciled_manually_and_is_never_redispatched()
        {
            using var database = new RewardTestDatabase();
            var rewardStore = new SqliteRewardStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewardStore, catalog).Execute(Package());
            var delivery = new RecordingRewardDeliveryPort(command =>
                RewardDeliveryResult.ResultUnknown(new[]
                {
                    RewardDeliveryEntryResult.ResultUnknown(
                        command.Entries.First(entry => entry.Kind == RewardEntryKind.Item).OperationEntryId,
                        "grant-item-unknown",
                        "ResultUnknown")
                }));
            var grant = new GrantRewardUseCase(rewardStore, delivery, economy, catalog);

            var result = await grant.ExecuteAsync(Command("unknown", "unknown"), CancellationToken.None);
            var pending = new PendingRewardReconciliationUseCase(rewardStore).Execute(20);
            var confirmed = new ConfirmRewardGrantUseCase(rewardStore, economy).Execute(
                new ConfirmRewardGrantCommand(
                    result.Operation.OperationId,
                    "owner-1",
                    "manual-confirm-1",
                    DateTimeOffset.UtcNow));

            Assert.Equal(GrantOperationState.PendingReconciliation, result.Operation.State);
            Assert.Single(pending);
            Assert.Equal(GrantOperationState.Completed, confirmed.State);
            Assert.Equal("owner-1", confirmed.ReconciledBy);
            Assert.Equal("manual-confirm-1", confirmed.CorrelationId);
            Assert.Equal(1, delivery.Calls);
            Assert.Equal(25, economy.QueryAccounts(new AccountKeysetQuery(
                20, false, "EOS-player", null, null, null)).Accounts.Single().PostedBalance);
        }

        [Fact]
        public async Task Refund_reverses_currency_and_compensation_creates_a_linked_grant()
        {
            using var database = new RewardTestDatabase();
            var rewardStore = new SqliteRewardStore(database.ConnectionFactory);
            var economy = new SqliteEconomyLedgerStore(database.ConnectionFactory);
            var catalog = RewardTestCatalog.Available();
            new SaveRewardPackageUseCase(rewardStore, catalog).Execute(Package());
            var delivery = new RecordingRewardDeliveryPort(command =>
                RewardDeliveryResult.Succeeded(command.Entries
                    .Where(entry => entry.Kind != RewardEntryKind.Currency)
                    .Select(entry => RewardDeliveryEntryResult.Succeeded(
                        entry.OperationEntryId,
                        "action-" + entry.OperationEntryId))));
            var grant = new GrantRewardUseCase(rewardStore, delivery, economy, catalog);

            var refundable = await grant.ExecuteAsync(Command("refund-source", "refund-source"), CancellationToken.None);
            var refunded = new RefundRewardGrantUseCase(rewardStore, economy).Execute(
                new RefundRewardGrantCommand(
                    refundable.Operation.OperationId,
                    "refund-key",
                    "Owner",
                    "owner-1",
                    "refund-correlation",
                    DateTimeOffset.UtcNow));
            var compensationSource = await grant.ExecuteAsync(
                Command("compensation-source", "compensation-source"),
                CancellationToken.None);
            var compensated = await new CompensateRewardGrantUseCase(rewardStore, grant).ExecuteAsync(
                new CompensateRewardGrantCommand(
                    compensationSource.Operation.OperationId,
                    "compensation-key",
                    "Owner",
                    "owner-1",
                    "compensation-correlation"),
                CancellationToken.None);

            Assert.Equal(GrantOperationState.Refunded, refunded.State);
            Assert.Contains(
                economy.QueryTransactions(new TransactionKeysetQuery(
                    50, "EOS-player", null, null, null, null)).Transactions,
                transaction => transaction.Type == "RewardRefund");
            Assert.NotEqual(compensationSource.Operation.OperationId, compensated.Operation.OperationId);
            Assert.Equal(
                compensationSource.Operation.OperationId,
                compensated.Operation.CompensatesOperationId);
            Assert.Equal(
                GrantOperationState.Compensated,
                rewardStore.GetGrant(compensationSource.Operation.OperationId).State);
        }

        private static RewardPackageDraft Package() => new RewardPackageDraft(
            "starter-package",
            "Starter Package",
            "A typed reward package",
            true,
            10,
            new[]
            {
                RewardPackageEntryDraft.Item(
                    "starter-item",
                    "medicalBandage",
                    GameResourceKind.Item,
                    2,
                    null,
                    null,
                    "catalog-v1"),
                RewardPackageEntryDraft.Currency("starter-currency", 25),
                RewardPackageEntryDraft.RegisteredActionEntry(
                    "starter-reset",
                    RewardRegisteredActions.ResetSkills)
            });

        private static GrantRewardCommand Command(string idempotencyKey, string eligibilityKey) =>
            new GrantRewardCommand(
                "starter-package",
                "EOS-player",
                42,
                "world-1",
                idempotencyKey,
                eligibilityKey,
                "Achievement",
                "achievement-1",
                "System",
                "reward-tests",
                idempotencyKey + "-correlation");

        private sealed class RecordingRewardDeliveryPort : IRewardDeliveryPort
        {
            private readonly Func<RewardDeliveryCommand, RewardDeliveryResult> deliver;

            public RecordingRewardDeliveryPort(Func<RewardDeliveryCommand, RewardDeliveryResult> deliver) =>
                this.deliver = deliver;

            public int Calls { get; private set; }

            public Task<RewardDeliveryResult> DeliverAsync(
                RewardDeliveryCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                return Task.FromResult(deliver(command));
            }
        }
    }
}
