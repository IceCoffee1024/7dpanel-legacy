using System;

namespace LSTY.SevenDPanel.Application.Economy
{
    public interface IEconomyLedgerStore
    {
        AccountSnapshot GetOrCreatePlayerAccount(
            string crossplatformId,
            string idempotencyKey,
            long openingAmount,
            DateTimeOffset occurredAtUtc);

        LedgerWriteResult Commit(LedgerTransactionDraft transaction);

        FundsReservationResult TryReserve(FundsReservationDraft reservation);

        LedgerWriteResult Capture(
            string reservationId,
            string transactionId,
            string idempotencyKey,
            DateTimeOffset occurredAtUtc);

        bool Release(string reservationId, DateTimeOffset occurredAtUtc);

        AccountPage QueryAccounts(AccountKeysetQuery query);

        TransactionPage QueryTransactions(TransactionKeysetQuery query);
    }

    public interface IEconomyAccountAdministrationStore
    {
        AccountSnapshot SetFrozen(
            string accountId,
            bool isFrozen,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc);
    }
}
