using System;
using LSTY.SevenDPanel.Domain.Economy;

namespace LSTY.SevenDPanel.Application.Economy
{
    public sealed class OpenPlayerAccountUseCase
    {
        private readonly IEconomyLedgerStore store;

        public OpenPlayerAccountUseCase(IEconomyLedgerStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public AccountSnapshot Execute(OpenPlayerAccountCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return store.GetOrCreatePlayerAccount(
                command.CrossplatformId,
                command.IdempotencyKey,
                command.OpeningAmount,
                command.OccurredAtUtc);
        }
    }

    public sealed class SetAccountFrozenUseCase
    {
        private readonly IEconomyAccountAdministrationStore store;

        public SetAccountFrozenUseCase(IEconomyAccountAdministrationStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public AccountSnapshot Execute(SetAccountFrozenCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return store.SetFrozen(
                command.AccountId,
                command.IsFrozen,
                command.ExpectedRowVersion,
                command.OccurredAtUtc);
        }
    }

    public sealed class AdjustPlayerBalanceUseCase
    {
        private readonly IEconomyLedgerStore store;

        public AdjustPlayerBalanceUseCase(IEconomyLedgerStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public LedgerWriteResult Execute(AdjustPlayerBalanceCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var account = store.GetOrCreatePlayerAccount(
                command.CrossplatformId,
                command.IdempotencyKey + ":account",
                0,
                command.OccurredAtUtc);
            var systemAccountId = command.PlayerSide == LedgerSide.Credit
                ? SystemAccountIds.Issuance
                : SystemAccountIds.Recovery;
            var entries = command.PlayerSide == LedgerSide.Credit
                ? new[]
                {
                    new LedgerEntryDraft(systemAccountId, LedgerSide.Debit, command.Amount),
                    new LedgerEntryDraft(account.AccountId, LedgerSide.Credit, command.Amount)
                }
                : new[]
                {
                    new LedgerEntryDraft(account.AccountId, LedgerSide.Debit, command.Amount),
                    new LedgerEntryDraft(systemAccountId, LedgerSide.Credit, command.Amount)
                };
            return store.Commit(new LedgerTransactionDraft(
                command.TransactionId,
                command.PlayerSide == LedgerSide.Credit
                    ? "OwnerAdjustmentCredit"
                    : "OwnerAdjustmentDebit",
                command.IdempotencyKey,
                command.OccurredAtUtc,
                EconomyActorKinds.Owner,
                command.ActorId,
                command.CrossplatformId,
                "AccountAdjustment",
                account.AccountId,
                command.CorrelationId,
                command.Reason,
                entries));
        }
    }

    public sealed class TransferBalanceUseCase
    {
        private readonly IEconomyLedgerStore store;

        public TransferBalanceUseCase(IEconomyLedgerStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public LedgerWriteResult Execute(TransferBalanceCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var source = store.GetOrCreatePlayerAccount(
                command.SourceCrossplatformId,
                command.IdempotencyKey + ":source-account",
                0,
                command.OccurredAtUtc);
            var target = store.GetOrCreatePlayerAccount(
                command.TargetCrossplatformId,
                command.IdempotencyKey + ":target-account",
                0,
                command.OccurredAtUtc);
            return store.Commit(new LedgerTransactionDraft(
                command.TransactionId,
                "PlayerTransfer",
                command.IdempotencyKey,
                command.OccurredAtUtc,
                EconomyActorKinds.Player,
                command.SourceCrossplatformId,
                command.SourceCrossplatformId,
                "PlayerTransfer",
                command.TransactionId,
                command.CorrelationId,
                null,
                new[]
                {
                    new LedgerEntryDraft(source.AccountId, LedgerSide.Debit, command.Amount),
                    new LedgerEntryDraft(target.AccountId, LedgerSide.Credit, command.Amount)
                }));
        }
    }

    public sealed class QueryEconomyAccountsUseCase
    {
        private readonly IEconomyLedgerStore store;

        public QueryEconomyAccountsUseCase(IEconomyLedgerStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public AccountPage Execute(AccountKeysetQuery query) =>
            store.QueryAccounts(query ?? throw new ArgumentNullException(nameof(query)));
    }

    public sealed class QueryEconomyTransactionsUseCase
    {
        private readonly IEconomyLedgerStore store;

        public QueryEconomyTransactionsUseCase(IEconomyLedgerStore store) =>
            this.store = store ?? throw new ArgumentNullException(nameof(store));

        public TransactionPage Execute(TransactionKeysetQuery query) =>
            store.QueryTransactions(query ?? throw new ArgumentNullException(nameof(query)));
    }
}
