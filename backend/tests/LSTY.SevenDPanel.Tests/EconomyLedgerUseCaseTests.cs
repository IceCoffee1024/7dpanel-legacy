using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "Application")]
    public sealed class EconomyLedgerUseCaseTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 1, 2, 3, TimeSpan.Zero);

        [Fact]
        public void Credit_and_debit_adjustments_use_fixed_system_accounts_and_balanced_entries()
        {
            var store = new RecordingStore(Account("EOS-A", 100));
            var useCase = new AdjustPlayerBalanceUseCase(store);

            useCase.Execute(new AdjustPlayerBalanceCommand(
                "credit-1", "credit-key", "EOS-A", LedgerSide.Credit, 25,
                "owner", Now, "corr-credit", "compensation"));
            useCase.Execute(new AdjustPlayerBalanceCommand(
                "debit-1", "debit-key", "EOS-A", LedgerSide.Debit, 10,
                "owner", Now.AddMilliseconds(1), "corr-debit", "recovery"));

            Assert.Equal(2, store.Committed.Count);
            Assert.Equal(
                new[] { SystemAccountIds.Issuance, "player:EOS-A" },
                store.Committed[0].Entries.Select(entry => entry.AccountId));
            Assert.Equal(
                new[] { LedgerSide.Debit, LedgerSide.Credit },
                store.Committed[0].Entries.Select(entry => entry.Side));
            Assert.Equal(
                new[] { "player:EOS-A", SystemAccountIds.Recovery },
                store.Committed[1].Entries.Select(entry => entry.AccountId));
            Assert.Equal(
                new[] { LedgerSide.Debit, LedgerSide.Credit },
                store.Committed[1].Entries.Select(entry => entry.Side));
            Assert.All(store.Committed, draft => Assert.True(LedgerRules.IsBalanced(
                draft.Entries.Select(entry => new LedgerEntryAmount(entry.Side, entry.Amount)))));
            Assert.All(store.Committed, draft => Assert.Equal(EconomyActorKinds.Owner, draft.ActorKind));
        }

        [Fact]
        public void Transfer_builds_one_atomic_two_sided_player_transaction()
        {
            var store = new RecordingStore(Account("EOS-A", 100), Account("EOS-B", 0));
            var useCase = new TransferBalanceUseCase(store);

            useCase.Execute(new TransferBalanceCommand(
                "transfer-1", "transfer-key", "EOS-A", "EOS-B", 40,
                Now, "corr-transfer"));

            var draft = Assert.Single(store.Committed);
            Assert.Equal("PlayerTransfer", draft.Type);
            Assert.Equal(EconomyActorKinds.Player, draft.ActorKind);
            Assert.Equal("EOS-A", draft.ActorId);
            Assert.Equal("EOS-A", draft.RelatedCrossplatformId);
            Assert.Equal(
                new[] { "player:EOS-A", "player:EOS-B" },
                draft.Entries.Select(entry => entry.AccountId));
            Assert.Equal(
                new[] { LedgerSide.Debit, LedgerSide.Credit },
                draft.Entries.Select(entry => entry.Side));
            Assert.Throws<ArgumentException>(() => useCase.Execute(new TransferBalanceCommand(
                "same", "same-key", "EOS-A", "EOS-A", 1, Now, null)));
        }

        [Fact]
        public void Opening_freezing_and_queries_use_the_fixed_ports()
        {
            var account = Account("EOS-A", 100);
            var store = new RecordingStore(account);

            var opened = new OpenPlayerAccountUseCase(store).Execute(
                new OpenPlayerAccountCommand("EOS-A", "open-key", 25, Now));
            var frozen = new SetAccountFrozenUseCase(store).Execute(
                new SetAccountFrozenCommand(account.AccountId, true, account.RowVersion, Now));
            var accounts = new QueryEconomyAccountsUseCase(store).Execute(
                new AccountKeysetQuery(25, includeSystem: false));
            var transactions = new QueryEconomyTransactionsUseCase(store).Execute(
                new TransactionKeysetQuery(25, relatedCrossplatformId: "EOS-A"));

            Assert.Same(account, opened);
            Assert.True(frozen.IsFrozen);
            Assert.Equal("open-key", store.LastOpeningKey);
            Assert.NotNull(store.LastAccountQuery);
            Assert.NotNull(store.LastTransactionQuery);
            Assert.Empty(accounts.Accounts);
            Assert.Empty(transactions.Transactions);
        }

        [Fact]
        public void Commands_reject_negative_amounts_empty_reasons_and_non_utc_timestamps()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OpenPlayerAccountCommand(
                "EOS-A", "open", -1, Now));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AdjustPlayerBalanceCommand(
                "tx", "key", "EOS-A", LedgerSide.Credit, -1,
                "owner", Now, null, "reason"));
            Assert.Throws<ArgumentException>(() => new AdjustPlayerBalanceCommand(
                "tx", "key", "EOS-A", LedgerSide.Credit, 1,
                "owner", Now, null, " "));
            Assert.Throws<ArgumentException>(() => new TransferBalanceCommand(
                "tx", "key", "EOS-A", "EOS-B", 1,
                Now.ToOffset(TimeSpan.FromHours(8)), null));
        }

        private static AccountSnapshot Account(string crossplatformId, long balance) =>
            new AccountSnapshot(
                "player:" + crossplatformId,
                EconomyAccountKind.Player,
                crossplatformId,
                enabled: true,
                isFrozen: false,
                postedBalance: balance,
                reservedDebit: 0,
                createdAtUtc: Now,
                updatedAtUtc: Now,
                rowVersion: 0);

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingStore : IEconomyLedgerStore, IEconomyAccountAdministrationStore
        {
            private readonly Dictionary<string, AccountSnapshot> accounts;

            public RecordingStore(params AccountSnapshot[] accounts) =>
                this.accounts = accounts.ToDictionary(
                    account => account.CrossplatformId!, StringComparer.Ordinal);

            public List<LedgerTransactionDraft> Committed { get; } = new List<LedgerTransactionDraft>();
            public string? LastOpeningKey { get; private set; }
            public AccountKeysetQuery? LastAccountQuery { get; private set; }
            public TransactionKeysetQuery? LastTransactionQuery { get; private set; }

            public AccountSnapshot GetOrCreatePlayerAccount(
                string crossplatformId,
                string idempotencyKey,
                long openingAmount,
                DateTimeOffset occurredAtUtc)
            {
                LastOpeningKey = idempotencyKey;
                return accounts[crossplatformId];
            }

            public LedgerWriteResult Commit(LedgerTransactionDraft transaction)
            {
                Committed.Add(transaction);
                var entries = transaction.Entries.Select((entry, index) =>
                    new LedgerEntrySnapshot(
                        "entry-" + index,
                        entry.AccountId,
                        entry.Side,
                        entry.Amount,
                        balanceAfter: 0)).ToArray();
                return new LedgerWriteResult(
                    new LedgerTransactionSnapshot(
                        transaction.TransactionId,
                        transaction.Type,
                        transaction.IdempotencyKey,
                        transaction.OccurredAtUtc,
                        transaction.ActorKind,
                        transaction.ActorId,
                        transaction.RelatedCrossplatformId,
                        transaction.BusinessKind,
                        transaction.BusinessId,
                        transaction.CorrelationId,
                        transaction.Reason,
                        "Committed",
                        entries),
                    Array.Empty<AccountSnapshot>());
            }

            public FundsReservationResult TryReserve(FundsReservationDraft reservation) =>
                throw new NotSupportedException();

            public LedgerWriteResult Capture(
                string reservationId,
                string transactionId,
                string idempotencyKey,
                DateTimeOffset occurredAtUtc) => throw new NotSupportedException();

            public bool Release(string reservationId, DateTimeOffset occurredAtUtc) =>
                throw new NotSupportedException();

            public AccountPage QueryAccounts(AccountKeysetQuery query)
            {
                LastAccountQuery = query;
                return new AccountPage(Array.Empty<AccountSnapshot>(), null);
            }

            public TransactionPage QueryTransactions(TransactionKeysetQuery query)
            {
                LastTransactionQuery = query;
                return new TransactionPage(Array.Empty<LedgerTransactionSnapshot>(), null);
            }

            public AccountSnapshot SetFrozen(
                string accountId,
                bool isFrozen,
                long expectedRowVersion,
                DateTimeOffset occurredAtUtc)
            {
                var existing = accounts.Values.Single(account => account.AccountId == accountId);
                return new AccountSnapshot(
                    existing.AccountId,
                    existing.Kind,
                    existing.CrossplatformId,
                    existing.Enabled,
                    isFrozen,
                    existing.PostedBalance,
                    existing.ReservedDebit,
                    existing.CreatedAtUtc,
                    occurredAtUtc,
                    existing.RowVersion + 1);
            }
        }
    }
}
