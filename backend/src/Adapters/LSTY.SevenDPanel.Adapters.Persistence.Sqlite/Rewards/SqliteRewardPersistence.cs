using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Rewards;
using LSTY.SevenDPanel.Domain.Rewards;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Rewards
{
    internal static class RewardPersistence
    {
        internal static RewardPackageSnapshot LoadPackage(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string packageId)
        {
            var row = connection.QuerySingleOrDefault<SqliteRewardStore.PackageRow>(
                SqliteRewardStore.PackageSelect + " WHERE package_id = @PackageId;",
                new { PackageId = packageId }, transaction) ??
                throw new RewardPackageNotFoundException();
            return ToPackage(row, LoadPackageEntryRows(connection, transaction, packageId));
        }

        internal static SqliteRewardStore.PackageEntryRow[] LoadPackageEntryRows(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string packageId) => connection.Query<SqliteRewardStore.PackageEntryRow>(
                SqliteRewardStore.PackageEntrySelect +
                " WHERE package_id = @PackageId ORDER BY ordinal ASC;",
                new { PackageId = packageId }, transaction).ToArray();

        internal static GrantOperationSnapshot LoadGrant(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string operationId)
        {
            var row = connection.QuerySingleOrDefault<SqliteRewardStore.GrantRow>(
                SqliteRewardStore.GrantSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId }, transaction) ??
                throw new RewardGrantNotFoundException();
            var entries = connection.Query<SqliteRewardStore.GrantEntryRow>(
                SqliteRewardStore.GrantEntrySelect +
                " WHERE operation_id = @OperationId ORDER BY ordinal ASC;",
                new { OperationId = operationId }, transaction).ToArray();
            return ToGrant(row, entries);
        }

        internal static RewardPackageSnapshot ToPackage(
            SqliteRewardStore.PackageRow row,
            IEnumerable<SqliteRewardStore.PackageEntryRow> entries) => new RewardPackageSnapshot(
                row.PackageId,
                row.Name,
                row.Description,
                row.Enabled != 0,
                row.SortOrder,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.RowVersion,
                entries.Select(entry => new RewardPackageEntrySnapshot(
                    entry.EntryId,
                    entry.Ordinal,
                    ParseEntryKind(entry.EntryKind),
                    entry.ItemInternalName,
                    entry.ItemKind == null
                        ? (GameResourceKind?)null
                        : (GameResourceKind)Enum.Parse(typeof(GameResourceKind), entry.ItemKind),
                    entry.Quantity,
                    entry.MinQuality,
                    entry.MaxQuality,
                    entry.CatalogVersion,
                    entry.CurrencyAmount,
                    entry.RegisteredAction)));

        internal static GrantOperationSnapshot ToGrant(
            SqliteRewardStore.GrantRow row,
            IEnumerable<SqliteRewardStore.GrantEntryRow> entries) => new GrantOperationSnapshot(
                row.OperationId,
                row.PackageId,
                row.CrossplatformId,
                row.ExpectedEntityId,
                row.ExpectedWorldId,
                ParseState(row.State),
                row.IdempotencyKey,
                row.EligibilityKey,
                row.SourceKind,
                row.SourceId,
                row.ActorKind,
                row.ActorId,
                row.ReservationId,
                row.CompensatesOperationId,
                row.CorrelationId,
                row.ErrorCode,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
                DateTimeOffset.FromUnixTimeMilliseconds(row.UpdatedAtUtc),
                row.CompletedAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                    : (DateTimeOffset?)null,
                row.ReconciledAtUtc.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(row.ReconciledAtUtc.Value)
                    : (DateTimeOffset?)null,
                row.ReconciledBy,
                row.RowVersion,
                entries.Select(entry => new GrantOperationEntrySnapshot(
                    entry.OperationEntryId,
                    entry.PackageEntryId,
                    entry.Ordinal,
                    ParseEntryKind(entry.EntryKind),
                    ParseState(entry.State),
                    entry.DeliveryOperationId,
                    entry.LedgerTransactionId,
                    entry.ErrorCode,
                    DateTimeOffset.FromUnixTimeMilliseconds(entry.UpdatedAtUtc),
                    entry.RowVersion)));

        internal static void EnsureGrantMatches(
            SqliteRewardStore.GrantRow row,
            GrantOperationDraft operation,
            bool requireIdempotency)
        {
            if (!string.Equals(row.PackageId, operation.PackageId, StringComparison.Ordinal) ||
                !string.Equals(row.CrossplatformId, operation.CrossplatformId, StringComparison.Ordinal) ||
                row.ExpectedEntityId != operation.ExpectedEntityId ||
                !string.Equals(row.ExpectedWorldId, operation.ExpectedWorldId, StringComparison.Ordinal) ||
                !string.Equals(row.EligibilityKey, operation.EligibilityKey, StringComparison.Ordinal) ||
                !string.Equals(row.SourceKind, operation.SourceKind, StringComparison.Ordinal) ||
                !string.Equals(row.SourceId, operation.SourceId, StringComparison.Ordinal) ||
                !string.Equals(row.CompensatesOperationId, operation.CompensatesOperationId, StringComparison.Ordinal) ||
                (requireIdempotency &&
                 !string.Equals(row.IdempotencyKey, operation.IdempotencyKey, StringComparison.Ordinal)))
            {
                throw new RewardIdempotencyConflictException();
            }
        }

        internal static bool PackageEntriesMatch(
            IReadOnlyList<SqliteRewardStore.PackageEntryRow> rows,
            IReadOnlyList<RewardPackageEntryDraft> entries)
        {
            if (rows.Count != entries.Count) return false;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var entry = entries[index];
                if (row.Ordinal != index ||
                    !string.Equals(row.EntryId, entry.EntryId, StringComparison.Ordinal) ||
                    !string.Equals(row.EntryKind, entry.Kind.ToString(), StringComparison.Ordinal) ||
                    !string.Equals(row.ItemInternalName, entry.ItemInternalName, StringComparison.Ordinal) ||
                    !string.Equals(row.ItemKind, entry.ItemKind?.ToString(), StringComparison.Ordinal) ||
                    row.Quantity != entry.Quantity || row.MinQuality != entry.MinQuality ||
                    row.MaxQuality != entry.MaxQuality ||
                    !string.Equals(row.CatalogVersion, entry.CatalogVersion, StringComparison.Ordinal) ||
                    row.CurrencyAmount != entry.CurrencyAmount ||
                    !string.Equals(row.RegisteredAction, entry.RegisteredAction, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        internal static RewardEntryKind ParseEntryKind(string value) =>
            (RewardEntryKind)Enum.Parse(typeof(RewardEntryKind), value);

        internal static GrantOperationState ParseState(string value) =>
            (GrantOperationState)Enum.Parse(typeof(GrantOperationState), value);
    }
}
