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
    public sealed class SqliteRewardStore : IRewardStore
    {
        private const string PackageSelect = @"SELECT
            package_id AS PackageId, name AS Name, description AS Description,
            enabled AS Enabled, sort_order AS SortOrder,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion
            FROM reward_packages";

        private const string PackageEntrySelect = @"SELECT
            entry_id AS EntryId, package_id AS PackageId, ordinal AS Ordinal,
            entry_kind AS EntryKind, item_internal_name AS ItemInternalName,
            item_kind AS ItemKind, quantity AS Quantity,
            min_quality AS MinQuality, max_quality AS MaxQuality,
            catalog_version AS CatalogVersion, currency_amount AS CurrencyAmount,
            registered_action AS RegisteredAction
            FROM reward_package_entries";

        private const string GrantSelect = @"SELECT
            operation_id AS OperationId, package_id AS PackageId,
            crossplatform_id AS CrossplatformId,
            expected_entity_id AS ExpectedEntityId,
            expected_world_id AS ExpectedWorldId, state AS State,
            idempotency_key AS IdempotencyKey, eligibility_key AS EligibilityKey,
            source_kind AS SourceKind, source_id AS SourceId,
            actor_kind AS ActorKind, actor_id AS ActorId,
            reservation_id AS ReservationId,
            compensates_operation_id AS CompensatesOperationId,
            correlation_id AS CorrelationId, error_code AS ErrorCode,
            created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
            completed_at_utc AS CompletedAtUtc,
            reconciled_at_utc AS ReconciledAtUtc, reconciled_by AS ReconciledBy,
            row_version AS RowVersion
            FROM grant_operations";

        private const string GrantEntrySelect = @"SELECT
            operation_entry_id AS OperationEntryId, operation_id AS OperationId,
            package_entry_id AS PackageEntryId, ordinal AS Ordinal,
            entry_kind AS EntryKind, state AS State,
            delivery_operation_id AS DeliveryOperationId,
            ledger_transaction_id AS LedgerTransactionId,
            error_code AS ErrorCode, updated_at_utc AS UpdatedAtUtc,
            row_version AS RowVersion
            FROM grant_operation_entries";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteRewardStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public RewardPackageSnapshot SavePackage(
            RewardPackageDraft package,
            DateTimeOffset occurredAtUtc)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<PackageRow>(
                PackageSelect + " WHERE package_id = @PackageId;",
                new { package.PackageId }, transaction);
            var referenced = existing != null && connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM grant_operations WHERE package_id = @PackageId;",
                new { package.PackageId }, transaction) > 0;
            if (referenced)
            {
                var storedEntries = LoadPackageEntryRows(connection, transaction, package.PackageId);
                if (!PackageEntriesMatch(storedEntries, package.Entries))
                    throw new InvalidOperationException("reward_package_entries_are_in_use");
            }

            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO reward_packages (
                      package_id, name, description, enabled, sort_order,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES (@PackageId, @Name, @Description, @Enabled, @SortOrder,
                      @OccurredUtc, @OccurredUtc, 0)
                  ON CONFLICT(package_id) DO UPDATE SET
                      name = excluded.name,
                      description = excluded.description,
                      enabled = excluded.enabled,
                      sort_order = excluded.sort_order,
                      updated_at_utc = excluded.updated_at_utc,
                      row_version = reward_packages.row_version + 1;",
                new
                {
                    package.PackageId,
                    package.Name,
                    package.Description,
                    Enabled = package.Enabled ? 1 : 0,
                    package.SortOrder,
                    OccurredUtc = occurredUtc
                }, transaction);

            if (!referenced)
            {
                connection.Execute(
                    "DELETE FROM reward_package_entries WHERE package_id = @PackageId;",
                    new { package.PackageId }, transaction);
                for (var ordinal = 0; ordinal < package.Entries.Count; ordinal++)
                {
                    var entry = package.Entries[ordinal];
                    connection.Execute(
                        @"INSERT INTO reward_package_entries (
                              entry_id, package_id, ordinal, entry_kind,
                              item_internal_name, item_kind, quantity,
                              min_quality, max_quality, catalog_version,
                              currency_amount, registered_action)
                          VALUES (@EntryId, @PackageId, @Ordinal, @EntryKind,
                              @ItemInternalName, @ItemKind, @Quantity,
                              @MinQuality, @MaxQuality, @CatalogVersion,
                              @CurrencyAmount, @RegisteredAction);",
                        new
                        {
                            entry.EntryId,
                            package.PackageId,
                            Ordinal = ordinal,
                            EntryKind = entry.Kind.ToString(),
                            entry.ItemInternalName,
                            ItemKind = entry.ItemKind?.ToString(),
                            entry.Quantity,
                            entry.MinQuality,
                            entry.MaxQuality,
                            entry.CatalogVersion,
                            entry.CurrencyAmount,
                            entry.RegisteredAction
                        }, transaction);
                }
            }

            var result = LoadPackage(connection, transaction, package.PackageId);
            transaction.Commit();
            return result;
        }

        public RewardPackageSnapshot GetPackage(string packageId)
        {
            packageId = RequireText(packageId, nameof(packageId));
            using var connection = connectionFactory.Open();
            return LoadPackage(connection, null, packageId);
        }

        public GrantCreationResult GetOrCreateGrant(GrantOperationDraft operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<GrantRow>(
                GrantSelect + " WHERE idempotency_key = @IdempotencyKey;",
                new { operation.IdempotencyKey }, transaction);
            if (existing != null)
            {
                EnsureGrantMatches(existing, operation, requireIdempotency: true);
                var replay = LoadGrant(connection, transaction, existing.OperationId);
                transaction.Commit();
                return new GrantCreationResult(replay, false);
            }

            if (operation.EligibilityKey != null)
            {
                existing = connection.QuerySingleOrDefault<GrantRow>(
                    GrantSelect + @" WHERE source_kind = @SourceKind AND source_id = @SourceId
                        AND crossplatform_id = @CrossplatformId
                        AND eligibility_key = @EligibilityKey;",
                    new
                    {
                        operation.SourceKind,
                        operation.SourceId,
                        operation.CrossplatformId,
                        operation.EligibilityKey
                    }, transaction);
                if (existing != null)
                {
                    EnsureGrantMatches(existing, operation, requireIdempotency: false);
                    var duplicate = LoadGrant(connection, transaction, existing.OperationId);
                    transaction.Commit();
                    return new GrantCreationResult(duplicate, false);
                }
            }

            var packageEntries = LoadPackageEntryRows(connection, transaction, operation.PackageId);
            if (packageEntries.Length == 0) throw new RewardPackageNotFoundException();
            if (operation.Entries.Count != packageEntries.Length ||
                operation.Entries.Any(entry => !packageEntries.Any(packageEntry =>
                    string.Equals(packageEntry.EntryId, entry.PackageEntryId, StringComparison.Ordinal) &&
                    string.Equals(packageEntry.EntryKind, entry.Kind.ToString(), StringComparison.Ordinal) &&
                    packageEntry.Ordinal == entry.Ordinal)))
            {
                throw new InvalidOperationException("reward_grant_entries_do_not_match_package");
            }

            var createdUtc = operation.CreatedAtUtc.ToUnixTimeMilliseconds();
            connection.Execute(
                @"INSERT INTO grant_operations (
                      operation_id, package_id, crossplatform_id,
                      expected_entity_id, expected_world_id, state,
                      idempotency_key, eligibility_key, source_kind, source_id,
                      actor_kind, actor_id, reservation_id,
                      compensates_operation_id, correlation_id, error_code,
                      created_at_utc, updated_at_utc, completed_at_utc,
                      reconciled_at_utc, reconciled_by, row_version)
                  VALUES (@OperationId, @PackageId, @CrossplatformId,
                      @ExpectedEntityId, @ExpectedWorldId, 'Reserved',
                      @IdempotencyKey, @EligibilityKey, @SourceKind, @SourceId,
                      @ActorKind, @ActorId, @ReservationId,
                      @CompensatesOperationId, @CorrelationId, NULL,
                      @CreatedUtc, @CreatedUtc, NULL, NULL, NULL, 0);",
                new
                {
                    operation.OperationId,
                    operation.PackageId,
                    operation.CrossplatformId,
                    operation.ExpectedEntityId,
                    operation.ExpectedWorldId,
                    operation.IdempotencyKey,
                    operation.EligibilityKey,
                    operation.SourceKind,
                    operation.SourceId,
                    operation.ActorKind,
                    operation.ActorId,
                    operation.ReservationId,
                    operation.CompensatesOperationId,
                    operation.CorrelationId,
                    CreatedUtc = createdUtc
                }, transaction);
            foreach (var entry in operation.Entries)
            {
                connection.Execute(
                    @"INSERT INTO grant_operation_entries (
                          operation_entry_id, operation_id, package_entry_id,
                          ordinal, entry_kind, state, delivery_operation_id,
                          ledger_transaction_id, error_code, updated_at_utc, row_version)
                      VALUES (@OperationEntryId, @OperationId, @PackageEntryId,
                          @Ordinal, @EntryKind, 'Reserved', NULL, NULL, NULL,
                          @CreatedUtc, 0);",
                    new
                    {
                        entry.OperationEntryId,
                        operation.OperationId,
                        entry.PackageEntryId,
                        entry.Ordinal,
                        EntryKind = entry.Kind.ToString(),
                        CreatedUtc = createdUtc
                    }, transaction);
            }

            var created = LoadGrant(connection, transaction, operation.OperationId);
            transaction.Commit();
            return new GrantCreationResult(created, true);
        }

        public GrantOperationSnapshot GetGrant(string operationId)
        {
            operationId = RequireText(operationId, nameof(operationId));
            using var connection = connectionFactory.Open();
            return LoadGrant(connection, null, operationId);
        }

        public bool TryStartDispatch(
            string operationId,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = 'Dispatching', updated_at_utc = @OccurredUtc,
                      error_code = NULL, row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = 'Reserved'
                    AND row_version = @ExpectedRowVersion;",
                new { OperationId = operationId, ExpectedRowVersion = expectedRowVersion, OccurredUtc = occurredUtc },
                transaction);
            if (changed == 0)
            {
                transaction.Commit();
                return false;
            }
            connection.Execute(
                @"UPDATE grant_operation_entries
                  SET state = 'Dispatching', updated_at_utc = @OccurredUtc,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = 'Reserved';",
                new { OperationId = operationId, OccurredUtc = occurredUtc }, transaction);
            transaction.Commit();
            return true;
        }

        public void RecordDeliveryOperation(
            string grantOperationId,
            string operationEntryId,
            string deliveryOperationId,
            DateTimeOffset occurredAtUtc)
        {
            grantOperationId = RequireText(grantOperationId, nameof(grantOperationId));
            operationEntryId = RequireText(operationEntryId, nameof(operationEntryId));
            deliveryOperationId = RequireText(deliveryOperationId, nameof(deliveryOperationId));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<GrantEntryRow>(
                GrantEntrySelect + @" WHERE operation_id = @OperationId
                    AND operation_entry_id = @OperationEntryId;",
                new { OperationId = grantOperationId, OperationEntryId = operationEntryId }, transaction) ??
                throw new RewardGrantNotFoundException();
            if (existing.DeliveryOperationId != null)
            {
                if (!string.Equals(existing.DeliveryOperationId, deliveryOperationId, StringComparison.Ordinal))
                    throw new RewardIdempotencyConflictException();
                transaction.Commit();
                return;
            }
            var changed = connection.Execute(
                @"UPDATE grant_operation_entries
                  SET delivery_operation_id = @DeliveryOperationId,
                      updated_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId
                    AND operation_entry_id = @OperationEntryId
                    AND state = 'Dispatching' AND delivery_operation_id IS NULL;",
                new
                {
                    OperationId = grantOperationId,
                    OperationEntryId = operationEntryId,
                    DeliveryOperationId = deliveryOperationId,
                    OccurredUtc = occurredAtUtc.ToUnixTimeMilliseconds()
                }, transaction);
            if (changed != 1) throw new RewardConcurrencyException();
            transaction.Commit();
        }

        public bool TryResolveDispatch(GrantDispatchResolution resolution)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var operation = connection.QuerySingleOrDefault<GrantRow>(
                GrantSelect + " WHERE operation_id = @OperationId;",
                new { resolution.OperationId }, transaction) ??
                throw new RewardGrantNotFoundException();
            if (string.Equals(operation.State, resolution.State.ToString(), StringComparison.Ordinal))
            {
                transaction.Commit();
                return true;
            }
            var current = ParseState(operation.State);
            if (operation.RowVersion != resolution.ExpectedRowVersion ||
                !GrantStateMachine.CanTransition(current, resolution.State))
            {
                transaction.Commit();
                return false;
            }

            var occurredUtc = resolution.OccurredAtUtc.ToUnixTimeMilliseconds();
            var completedUtc = resolution.State == GrantOperationState.PendingReconciliation
                ? (long?)null
                : occurredUtc;
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = @State, error_code = @ErrorCode,
                      updated_at_utc = @OccurredUtc,
                      completed_at_utc = @CompletedUtc,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = @CurrentState
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    State = resolution.State.ToString(),
                    resolution.ErrorCode,
                    OccurredUtc = occurredUtc,
                    CompletedUtc = completedUtc,
                    resolution.OperationId,
                    CurrentState = operation.State,
                    resolution.ExpectedRowVersion
                }, transaction);
            if (changed != 1)
            {
                transaction.Commit();
                return false;
            }

            var supplied = resolution.Entries.ToDictionary(
                entry => entry.OperationEntryId,
                StringComparer.Ordinal);
            var entries = connection.Query<GrantEntryRow>(
                GrantEntrySelect + " WHERE operation_id = @OperationId ORDER BY ordinal ASC;",
                new { resolution.OperationId }, transaction).ToArray();
            foreach (var entry in entries)
            {
                var target = supplied.TryGetValue(entry.OperationEntryId, out var suppliedEntry)
                    ? suppliedEntry
                    : new GrantEntryResolution(
                        entry.OperationEntryId,
                        resolution.State == GrantOperationState.PendingReconciliation
                            ? GrantOperationState.PendingReconciliation
                            : resolution.State,
                        null,
                        null,
                        resolution.ErrorCode);
                var entryChanged = connection.Execute(
                    @"UPDATE grant_operation_entries
                      SET state = @State,
                          delivery_operation_id = COALESCE(@DeliveryOperationId, delivery_operation_id),
                          ledger_transaction_id = COALESCE(@LedgerTransactionId, ledger_transaction_id),
                          error_code = @ErrorCode, updated_at_utc = @OccurredUtc,
                          row_version = row_version + 1
                      WHERE operation_entry_id = @OperationEntryId
                        AND operation_id = @OperationId;",
                    new
                    {
                        State = target.State.ToString(),
                        target.DeliveryOperationId,
                        target.LedgerTransactionId,
                        target.ErrorCode,
                        OccurredUtc = occurredUtc,
                        target.OperationEntryId,
                        resolution.OperationId
                    }, transaction);
                if (entryChanged != 1) throw new RewardConcurrencyException();
            }

            if (resolution.State == GrantOperationState.Completed &&
                operation.CompensatesOperationId != null)
            {
                MarkCompensated(
                    connection,
                    transaction,
                    operation.CompensatesOperationId,
                    occurredUtc);
            }
            transaction.Commit();
            return true;
        }

        public bool TryMarkPendingReconciliation(
            string operationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            errorCode = Normalize(errorCode);
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = 'PendingReconciliation', error_code = @ErrorCode,
                      updated_at_utc = @OccurredUtc, completed_at_utc = NULL,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = 'Dispatching';",
                new { OperationId = operationId, ErrorCode = errorCode, OccurredUtc = occurredUtc },
                transaction);
            if (changed == 0)
            {
                var state = connection.ExecuteScalar<string?>(
                    "SELECT state FROM grant_operations WHERE operation_id = @OperationId;",
                    new { OperationId = operationId }, transaction);
                transaction.Commit();
                return string.Equals(state, "PendingReconciliation", StringComparison.Ordinal);
            }
            connection.Execute(
                @"UPDATE grant_operation_entries
                  SET state = 'PendingReconciliation', error_code = COALESCE(error_code, @ErrorCode),
                      updated_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state IN ('Reserved', 'Dispatching');",
                new { OperationId = operationId, ErrorCode = errorCode, OccurredUtc = occurredUtc },
                transaction);
            transaction.Commit();
            return true;
        }

        public IReadOnlyList<GrantOperationSnapshot> ListPendingReconciliation(int take)
        {
            if (take < 1 || take > 200) throw new ArgumentOutOfRangeException(nameof(take));
            using var connection = connectionFactory.Open();
            var ids = connection.Query<string>(
                @"SELECT operation_id FROM grant_operations
                  WHERE state = 'PendingReconciliation'
                  ORDER BY updated_at_utc ASC, operation_id ASC LIMIT @Take;",
                new { Take = take }).ToArray();
            return ids.Select(id => LoadGrant(connection, null, id)).ToArray();
        }

        public bool TryConfirmReconciled(
            string operationId,
            long expectedRowVersion,
            string actorId,
            string correlationId,
            string? ledgerTransactionId,
            DateTimeOffset occurredAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            actorId = RequireText(actorId, nameof(actorId));
            correlationId = RequireText(correlationId, nameof(correlationId));
            ledgerTransactionId = Normalize(ledgerTransactionId);
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var operation = connection.QuerySingleOrDefault<GrantRow>(
                GrantSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId }, transaction);
            if (operation == null ||
                !GrantStateMachine.CanTransition(ParseState(operation.State), GrantOperationState.Completed))
            {
                transaction.Commit();
                return false;
            }
            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = 'Completed', error_code = NULL,
                      correlation_id = @CorrelationId,
                      updated_at_utc = @OccurredUtc, completed_at_utc = @OccurredUtc,
                      reconciled_at_utc = @OccurredUtc, reconciled_by = @ActorId,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId
                    AND state = 'PendingReconciliation'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    OperationId = operationId,
                    ExpectedRowVersion = expectedRowVersion,
                    ActorId = actorId,
                    CorrelationId = correlationId,
                    OccurredUtc = occurredUtc
                }, transaction);
            if (changed == 0)
            {
                transaction.Commit();
                return false;
            }
            connection.Execute(
                @"UPDATE grant_operation_entries
                  SET state = 'Completed', error_code = NULL,
                      ledger_transaction_id = CASE
                          WHEN entry_kind = 'Currency' THEN COALESCE(@LedgerTransactionId, ledger_transaction_id)
                          ELSE ledger_transaction_id END,
                      updated_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state != 'Completed';",
                new
                {
                    OperationId = operationId,
                    LedgerTransactionId = ledgerTransactionId,
                    OccurredUtc = occurredUtc
                }, transaction);
            if (operation.CompensatesOperationId != null)
                MarkCompensated(connection, transaction, operation.CompensatesOperationId, occurredUtc);
            transaction.Commit();
            return true;
        }

        public bool TryMarkRefunded(
            string operationId,
            long expectedRowVersion,
            string refundLedgerTransactionId,
            string correlationId,
            DateTimeOffset occurredAtUtc)
        {
            operationId = RequireText(operationId, nameof(operationId));
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            refundLedgerTransactionId = RequireText(
                refundLedgerTransactionId,
                nameof(refundLedgerTransactionId));
            correlationId = RequireText(correlationId, nameof(correlationId));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var occurredUtc = occurredAtUtc.ToUnixTimeMilliseconds();
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = 'Refunded', correlation_id = @CorrelationId,
                      error_code = NULL, updated_at_utc = @OccurredUtc,
                      completed_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = 'Completed'
                    AND row_version = @ExpectedRowVersion;",
                new
                {
                    OperationId = operationId,
                    ExpectedRowVersion = expectedRowVersion,
                    CorrelationId = correlationId,
                    OccurredUtc = occurredUtc
                }, transaction);
            if (changed == 0)
            {
                transaction.Commit();
                return false;
            }
            connection.Execute(
                @"UPDATE grant_operation_entries
                  SET state = 'Refunded', error_code = NULL,
                      updated_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId;",
                new { OperationId = operationId, OccurredUtc = occurredUtc }, transaction);
            transaction.Commit();
            return true;
        }

        private static void MarkCompensated(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string sourceOperationId,
            long occurredUtc)
        {
            var source = connection.QuerySingleOrDefault<GrantRow>(
                GrantSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = sourceOperationId }, transaction) ??
                throw new RewardGrantNotFoundException();
            var state = ParseState(source.State);
            if (!GrantStateMachine.CanTransition(state, GrantOperationState.Compensated))
                throw new InvalidOperationException("reward_grant_not_compensatable");
            var changed = connection.Execute(
                @"UPDATE grant_operations
                  SET state = 'Compensated', updated_at_utc = @OccurredUtc,
                      completed_at_utc = @OccurredUtc, row_version = row_version + 1
                  WHERE operation_id = @OperationId AND state = @State
                    AND row_version = @RowVersion;",
                new
                {
                    OperationId = sourceOperationId,
                    State = source.State,
                    source.RowVersion,
                    OccurredUtc = occurredUtc
                }, transaction);
            if (changed != 1) throw new RewardConcurrencyException();
            connection.Execute(
                @"UPDATE grant_operation_entries
                  SET state = 'Compensated', updated_at_utc = @OccurredUtc,
                      row_version = row_version + 1
                  WHERE operation_id = @OperationId;",
                new { OperationId = sourceOperationId, OccurredUtc = occurredUtc }, transaction);
        }

        private static RewardPackageSnapshot LoadPackage(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string packageId)
        {
            var row = connection.QuerySingleOrDefault<PackageRow>(
                PackageSelect + " WHERE package_id = @PackageId;",
                new { PackageId = packageId }, transaction) ??
                throw new RewardPackageNotFoundException();
            return ToPackage(row, LoadPackageEntryRows(connection, transaction, packageId));
        }

        private static PackageEntryRow[] LoadPackageEntryRows(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string packageId) => connection.Query<PackageEntryRow>(
                PackageEntrySelect + " WHERE package_id = @PackageId ORDER BY ordinal ASC;",
                new { PackageId = packageId }, transaction).ToArray();

        private static GrantOperationSnapshot LoadGrant(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string operationId)
        {
            var row = connection.QuerySingleOrDefault<GrantRow>(
                GrantSelect + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId }, transaction) ??
                throw new RewardGrantNotFoundException();
            var entries = connection.Query<GrantEntryRow>(
                GrantEntrySelect + " WHERE operation_id = @OperationId ORDER BY ordinal ASC;",
                new { OperationId = operationId }, transaction).ToArray();
            return ToGrant(row, entries);
        }

        private static RewardPackageSnapshot ToPackage(
            PackageRow row,
            IEnumerable<PackageEntryRow> entries) => new RewardPackageSnapshot(
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

        private static GrantOperationSnapshot ToGrant(
            GrantRow row,
            IEnumerable<GrantEntryRow> entries) => new GrantOperationSnapshot(
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

        private static void EnsureGrantMatches(
            GrantRow row,
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

        private static bool PackageEntriesMatch(
            IReadOnlyList<PackageEntryRow> rows,
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

        private static RewardEntryKind ParseEntryKind(string value) =>
            (RewardEntryKind)Enum.Parse(typeof(RewardEntryKind), value);

        private static GrantOperationState ParseState(string value) =>
            (GrantOperationState)Enum.Parse(typeof(GrantOperationState), value);

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private sealed class PackageRow
        {
            public string PackageId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public int SortOrder { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class PackageEntryRow
        {
            public string EntryId { get; set; } = string.Empty;
            public string PackageId { get; set; } = string.Empty;
            public int Ordinal { get; set; }
            public string EntryKind { get; set; } = string.Empty;
            public string? ItemInternalName { get; set; }
            public string? ItemKind { get; set; }
            public int? Quantity { get; set; }
            public int? MinQuality { get; set; }
            public int? MaxQuality { get; set; }
            public string? CatalogVersion { get; set; }
            public long? CurrencyAmount { get; set; }
            public string? RegisteredAction { get; set; }
        }

        private sealed class GrantRow
        {
            public string OperationId { get; set; } = string.Empty;
            public string PackageId { get; set; } = string.Empty;
            public string CrossplatformId { get; set; } = string.Empty;
            public int ExpectedEntityId { get; set; }
            public string ExpectedWorldId { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? EligibilityKey { get; set; }
            public string? SourceKind { get; set; }
            public string? SourceId { get; set; }
            public string ActorKind { get; set; } = string.Empty;
            public string ActorId { get; set; } = string.Empty;
            public string? ReservationId { get; set; }
            public string? CompensatesOperationId { get; set; }
            public string? CorrelationId { get; set; }
            public string? ErrorCode { get; set; }
            public long CreatedAtUtc { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long? ReconciledAtUtc { get; set; }
            public string? ReconciledBy { get; set; }
            public long RowVersion { get; set; }
        }

        private sealed class GrantEntryRow
        {
            public string OperationEntryId { get; set; } = string.Empty;
            public string OperationId { get; set; } = string.Empty;
            public string PackageEntryId { get; set; } = string.Empty;
            public int Ordinal { get; set; }
            public string EntryKind { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public string? DeliveryOperationId { get; set; }
            public string? LedgerTransactionId { get; set; }
            public string? ErrorCode { get; set; }
            public long UpdatedAtUtc { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
