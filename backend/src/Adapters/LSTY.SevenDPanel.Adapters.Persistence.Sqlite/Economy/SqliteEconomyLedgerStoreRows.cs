using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Economy;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Economy
{
    internal static class SqliteEconomyLedgerStoreRows
    {
        internal const string AccountSelect = @"SELECT
            account_id AS AccountId, account_kind AS AccountKind,
            crossplatform_id AS CrossplatformId, enabled AS Enabled,
            is_frozen AS IsFrozen, posted_balance AS PostedBalance,
            reserved_debit AS ReservedDebit, created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc, row_version AS RowVersion
            FROM economy_accounts";

        internal const string TransactionSelect = @"SELECT
            transaction_id AS TransactionId, transaction_type AS TransactionType,
            idempotency_key AS IdempotencyKey, occurred_utc AS OccurredUtc,
            actor_kind AS ActorKind, actor_id AS ActorId,
            related_crossplatform_id AS RelatedCrossplatformId,
            business_kind AS BusinessKind, business_id AS BusinessId,
            correlation_id AS CorrelationId, reason AS Reason, status AS Status
            FROM economy_transactions";

        internal const string EntrySelect = @"SELECT
            entry_id AS EntryId, transaction_id AS TransactionId,
            account_id AS AccountId, ordinal AS Ordinal, side AS Side,
            amount AS Amount, balance_after AS BalanceAfter
            FROM economy_entries";

        internal const string ReservationSelect = @"SELECT
            reservation_id AS ReservationId, account_id AS AccountId,
            amount AS Amount, state AS State, idempotency_key AS IdempotencyKey,
            business_kind AS BusinessKind, business_id AS BusinessId,
            captured_transaction_id AS CapturedTransactionId,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            expires_at_utc AS ExpiresAtUtc, row_version AS RowVersion
            FROM economy_reservations";

        internal static LedgerWriteResult LoadWriteResult(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string transactionId)
        {
            var row = connection.QuerySingle<TransactionRow>(
                TransactionSelect + " WHERE transaction_id = @TransactionId;",
                new { TransactionId = transactionId }, transaction);
            var entries = LoadEntries(connection, transaction, new[] { transactionId });
            var accounts = entries
                .Select(entry => entry.AccountId)
                .Distinct(StringComparer.Ordinal)
                .Select(accountId => ToAccount(GetAccount(connection, transaction, accountId)))
                .ToArray();
            return new LedgerWriteResult(ToTransaction(row, entries), accounts);
        }

        internal static IReadOnlyList<EntryRow> LoadEntries(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            IEnumerable<string> transactionIds)
        {
            var ids = transactionIds.Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0) return Array.Empty<EntryRow>();
            return connection.Query<EntryRow>(
                    EntrySelect +
                    " WHERE transaction_id IN @TransactionIds ORDER BY transaction_id, ordinal;",
                    new { TransactionIds = ids }, transaction)
                .ToArray();
        }

        internal static TransactionRow? FindTransactionByIdempotency(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string idempotencyKey) => connection.QuerySingleOrDefault<TransactionRow>(
                TransactionSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { IdempotencyKey = idempotencyKey }, transaction);

        internal static AccountRow? FindPlayerAccount(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string crossplatformId) => connection.QuerySingleOrDefault<AccountRow>(
                AccountSelect +
                " WHERE account_kind = 'Player' AND crossplatform_id = @CrossplatformId;",
                new { CrossplatformId = crossplatformId }, transaction);

        internal static AccountRow GetAccount(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string accountId) => connection.QuerySingleOrDefault<AccountRow>(
                AccountSelect + " WHERE account_id = @AccountId;",
                new { AccountId = accountId }, transaction) ??
                throw new EconomyAccountNotFoundException();

        internal static AccountSnapshot ToAccount(AccountRow row) => new AccountSnapshot(
            row.AccountId,
            (EconomyAccountKind)Enum.Parse(typeof(EconomyAccountKind), row.AccountKind),
            row.CrossplatformId,
            row.Enabled != 0,
            row.IsFrozen != 0,
            row.PostedBalance,
            row.ReservedDebit,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
            row.RowVersion);

        internal static LedgerTransactionSnapshot ToTransaction(
            TransactionRow row,
            IEnumerable<EntryRow> entries) => new LedgerTransactionSnapshot(
                row.TransactionId,
                row.TransactionType,
                row.IdempotencyKey,
                DateTimeOffset.FromUnixTimeMilliseconds(row.OccurredUtc),
                row.ActorKind,
                row.ActorId,
                row.RelatedCrossplatformId,
                row.BusinessKind,
                row.BusinessId,
                row.CorrelationId,
                row.Reason,
                row.Status,
                entries.OrderBy(entry => entry.Ordinal).Select(entry => new LedgerEntrySnapshot(
                    entry.EntryId,
                    entry.AccountId,
                    (LedgerSide)Enum.Parse(typeof(LedgerSide), entry.Side),
                    entry.Amount,
                    entry.BalanceAfter)));

        internal static FundsReservationResult ToReservationResult(
            ReservationRow reservation,
            AccountRow account) => new FundsReservationResult(
                reservation.ReservationId,
                string.Equals(reservation.State, "Reserved", StringComparison.Ordinal)
                    ? FundsReservationStatus.Reserved
                    : string.Equals(reservation.State, "Captured", StringComparison.Ordinal)
                        ? FundsReservationStatus.Captured
                        : FundsReservationStatus.Released,
                reservation.Amount,
                ToAccount(account),
                reservation.CapturedTransactionId);

        internal sealed class AccountRow
        {
            public string AccountId { get; set; } = string.Empty;
            public string AccountKind { get; set; } = string.Empty;
            public string? CrossplatformId { get; set; }
            public int Enabled { get; set; }
            public int IsFrozen { get; set; }
            public long PostedBalance { get; set; }
            public long ReservedDebit { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        internal sealed class TransactionRow
        {
            public string TransactionId { get; set; } = string.Empty;
            public string TransactionType { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public long OccurredUtc { get; set; }
            public string ActorKind { get; set; } = string.Empty;
            public string ActorId { get; set; } = string.Empty;
            public string? RelatedCrossplatformId { get; set; }
            public string? BusinessKind { get; set; }
            public string? BusinessId { get; set; }
            public string? CorrelationId { get; set; }
            public string? Reason { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        internal sealed class EntryRow
        {
            public string EntryId { get; set; } = string.Empty;
            public string TransactionId { get; set; } = string.Empty;
            public string AccountId { get; set; } = string.Empty;
            public int Ordinal { get; set; }
            public string Side { get; set; } = string.Empty;
            public long Amount { get; set; }
            public long BalanceAfter { get; set; }
        }

        internal sealed class ReservationRow
        {
            public string ReservationId { get; set; } = string.Empty;
            public string AccountId { get; set; } = string.Empty;
            public long Amount { get; set; }
            public string State { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public string BusinessKind { get; set; } = string.Empty;
            public string BusinessId { get; set; } = string.Empty;
            public string? CapturedTransactionId { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long? ExpiresAtUtc { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
