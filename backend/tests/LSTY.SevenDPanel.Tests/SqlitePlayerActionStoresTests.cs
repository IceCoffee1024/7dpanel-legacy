using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqlitePlayerActionStoresTests
    {
        private static readonly string[] OperationTables =
        {
            "player_clear_inventory_operations",
            "player_grant_item_operations",
            "player_remove_item_operations",
            "player_reset_data_operations",
            "player_reset_skills_operations"
        };

        [Fact]
        public void Upgrade_creates_five_typed_tables_unique_keys_and_redacted_projection_sources()
        {
            using var database = UpgradedDatabase();
            using var connection = database.ConnectionFactory.Open();

            Assert.Equal(
                OperationTables,
                connection.Query<string>(
                    @"SELECT name
                      FROM sqlite_master
                      WHERE type = 'table' AND name LIKE 'player_%_operations'
                      ORDER BY name;").ToArray());
            foreach (var table in OperationTables)
            {
                var columns = Columns(connection, table);
                Assert.Contains("operation_id", columns);
                Assert.Contains("operator_id", columns);
                Assert.Contains("target_crossplatform_id", columns);
                Assert.Contains("target_entity_id", columns);
                Assert.Contains("target_online_observed_at_utc", columns);
                Assert.Contains("world_id", columns);
                Assert.Contains("client_request_key", columns);
                Assert.Contains("correlation_id", columns);
                Assert.Contains("status", columns);
                Assert.Contains("created_at_utc", columns);
                Assert.Contains("started_at_utc", columns);
                Assert.Contains("completed_at_utc", columns);
                Assert.Contains("failure_code", columns);
                Assert.Contains("before_inventory_snapshot_id", columns);
                Assert.Contains("after_inventory_snapshot_id", columns);
                Assert.Contains("before_skill_snapshot_id", columns);
                Assert.Contains("after_skill_snapshot_id", columns);
                Assert.DoesNotContain("payload_json", columns);
                Assert.DoesNotContain("command_text", columns);
                Assert.DoesNotContain("path", columns);
                Assert.Equal(
                    new[] { "operator_id", "client_request_key" },
                    UniqueKeyColumns(connection, table));
            }
            Assert.Contains("actual_quantity", Columns(connection, "player_grant_item_operations"));
            Assert.Contains("removal_mode", Columns(connection, "player_remove_item_operations"));
            Assert.Contains("danger_confirmed", Columns(connection, "player_reset_data_operations"));

            var viewSql = connection.ExecuteScalar<string>(
                @"SELECT sql
                  FROM sqlite_master
                  WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            Assert.All(OperationTables, table => Assert.Contains(table, viewSql, StringComparison.Ordinal));
            Assert.Contains(
                "'GrantItem', created_at_utc, status, correlation_id, 0",
                viewSql,
                StringComparison.Ordinal);
            Assert.DoesNotContain("internal_name", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("actual_quantity", viewSql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Five_concrete_stores_persist_only_their_typed_parameters_and_share_fixed_summaries()
        {
            using var database = UpgradedDatabase();
            var createdAtUtc = Utc(8);
            var target = Target();
            var grant = new SqliteGrantItemOperationStore(database.ConnectionFactory);
            var remove = new SqliteRemoveItemOperationStore(database.ConnectionFactory);
            var resetSkills = new SqliteResetSkillsOperationStore(database.ConnectionFactory);
            var clearInventory = new SqliteClearInventoryOperationStore(database.ConnectionFactory);
            var resetData = new SqliteResetPlayerDataOperationStore(database.ConnectionFactory);

            grant.CreatePending(GrantIntent("grant-1", "grant-key", createdAtUtc));
            remove.CreatePending(RemoveIntent("remove-1", "remove-key", createdAtUtc.AddMilliseconds(1)));
            resetSkills.CreatePending(ResetSkillsIntent(
                "reset-skills-1", "reset-skills-key", createdAtUtc.AddMilliseconds(2), true));
            clearInventory.CreatePending(ClearInventoryIntent(
                "clear-1", "clear-key", createdAtUtc.AddMilliseconds(3), true));
            resetData.CreatePending(ResetDataIntent(
                "reset-data-1", "reset-data-key", createdAtUtc.AddMilliseconds(4), true));

            var query = new SqlitePlayerActionOperationQuery(database.ConnectionFactory);
            Assert.Equal(PlayerActionOperationTypes.GrantItem, query.Get("grant-1")!.OperationType);
            Assert.Equal(PlayerActionOperationTypes.RemoveItem, query.Get("remove-1")!.OperationType);
            Assert.Equal(PlayerActionOperationTypes.ResetSkills, query.Get("reset-skills-1")!.OperationType);
            Assert.Equal(PlayerActionOperationTypes.ClearInventory, query.Get("clear-1")!.OperationType);
            Assert.Equal(PlayerActionOperationTypes.ResetPlayerData, query.Get("reset-data-1")!.OperationType);
            Assert.Null(query.Get("missing"));
            var summary = query.Get("grant-1")!;
            Assert.Equal("owner", summary.OperatorId);
            Assert.Equal(target, summary.Target);
            Assert.Equal(createdAtUtc, summary.CreatedAtUtc);
            Assert.Equal(PlayerActionStatus.Pending, summary.Status);

            using var connection = database.ConnectionFactory.Open();
            var grantRow = connection.QuerySingle<GrantRow>(
                "SELECT * FROM player_grant_item_operations WHERE operation_id = 'grant-1';");
            Assert.Equal("catalog-v1", grantRow.catalog_version);
            Assert.Equal("resourceWood", grantRow.internal_name);
            Assert.Equal("Item", grantRow.item_kind);
            Assert.Equal(25, grantRow.quantity);
            Assert.Equal(2, grantRow.quality);
            Assert.Equal(1, grantRow.hidden_item_confirmed);
            var removeRow = connection.QuerySingle<RemoveRow>(
                "SELECT * FROM player_remove_item_operations WHERE operation_id = 'remove-1';");
            Assert.Equal("BagOnly", removeRow.removal_scope);
            Assert.Equal("Exact", removeRow.removal_mode);
        }

        [Fact]
        public void Same_client_key_and_parameters_reuse_the_original_but_different_parameters_conflict()
        {
            using var database = UpgradedDatabase();
            var grant = new SqliteGrantItemOperationStore(database.ConnectionFactory);
            var resetData = new SqliteResetPlayerDataOperationStore(database.ConnectionFactory);
            var createdAtUtc = Utc(9);

            var original = grant.CreatePending(GrantIntent("grant-original", "same-key", createdAtUtc));
            var retry = grant.CreatePending(GrantIntent(
                "grant-retry", "same-key", createdAtUtc.AddMinutes(1), correlationId: "retry-correlation"));

            Assert.Equal(original.OperationId, retry.OperationId);
            var conflict = Assert.Throws<PlayerActionIdempotencyConflictException>(() =>
                grant.CreatePending(GrantIntent(
                    "grant-conflict", "same-key", createdAtUtc.AddMinutes(2), quantity: 26)));
            Assert.Equal("grant-original", conflict.ExistingOperationId);
            Assert.Equal("same-key", conflict.ClientRequestKey);

            resetData.CreatePending(ResetDataIntent("reset-original", "reset-key", createdAtUtc, true));
            Assert.Equal(
                "reset-original",
                resetData.CreatePending(ResetDataIntent(
                    "reset-retry", "reset-key", createdAtUtc.AddMinutes(1), true)).OperationId);
            Assert.Throws<PlayerActionIdempotencyConflictException>(() =>
                resetData.CreatePending(ResetDataIntent(
                    "reset-conflict", "reset-key", createdAtUtc.AddMinutes(2), false)));
        }

        [Fact]
        public void Pending_start_and_terminal_updates_are_compare_and_set_and_protect_terminal_evidence()
        {
            using var database = UpgradedDatabase();
            var evidence = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            evidence.AppendInventorySnapshot(InventorySnapshot(2001, Utc(9)));
            evidence.AppendInventorySnapshot(InventorySnapshot(2002, Utc(9).AddSeconds(2)));
            var store = new SqliteGrantItemOperationStore(database.ConnectionFactory);
            var createdAtUtc = Utc(10);
            store.CreatePending(GrantIntent(
                "grant-cas", "cas-key", createdAtUtc,
                beforeInventorySnapshotId: 2001));

            Assert.True(store.TryStart("grant-cas", createdAtUtc.AddSeconds(1)));
            Assert.False(store.TryStart("grant-cas", createdAtUtc.AddSeconds(2)));
            Assert.True(store.TryComplete(
                new PlayerActionOperationCompletion(
                    "grant-cas", PlayerActionStatus.Succeeded, createdAtUtc.AddSeconds(3), null,
                    2001, 2002, null, null),
                actualQuantity: 25));
            Assert.False(store.TryComplete(
                new PlayerActionOperationCompletion(
                    "grant-cas", PlayerActionStatus.Failed, createdAtUtc.AddSeconds(4),
                    "late_failure_secret", 2001, null, null, null),
                actualQuantity: null));

            var operation = new SqlitePlayerActionOperationQuery(database.ConnectionFactory).Get("grant-cas")!;
            Assert.Equal(PlayerActionStatus.Succeeded, operation.Status);
            Assert.Equal(createdAtUtc.AddSeconds(1), operation.StartedAtUtc);
            Assert.Equal(createdAtUtc.AddSeconds(3), operation.CompletedAtUtc);
            Assert.Equal(2002, operation.AfterInventorySnapshotId);
            Assert.Null(operation.FailureCode);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(25, connection.ExecuteScalar<int>(
                "SELECT actual_quantity FROM player_grant_item_operations WHERE operation_id = 'grant-cas';"));
        }

        [Fact]
        public void Every_store_supports_terminal_compare_and_set_without_a_universal_payload()
        {
            using var database = UpgradedDatabase();
            var completedAtUtc = Utc(11);
            var remove = new SqliteRemoveItemOperationStore(database.ConnectionFactory);
            var resetSkills = new SqliteResetSkillsOperationStore(database.ConnectionFactory);
            var clear = new SqliteClearInventoryOperationStore(database.ConnectionFactory);
            var resetData = new SqliteResetPlayerDataOperationStore(database.ConnectionFactory);
            remove.CreatePending(RemoveIntent("remove-terminal", "remove-terminal-key", completedAtUtc.AddSeconds(-1)));
            resetSkills.CreatePending(ResetSkillsIntent(
                "skills-terminal", "skills-terminal-key", completedAtUtc.AddSeconds(-1), true));
            clear.CreatePending(ClearInventoryIntent(
                "clear-terminal", "clear-terminal-key", completedAtUtc.AddSeconds(-1), true));
            resetData.CreatePending(ResetDataIntent(
                "data-terminal", "data-terminal-key", completedAtUtc.AddSeconds(-1), true));

            Assert.True(remove.TryComplete(Completion("remove-terminal", PlayerActionStatus.Rejected, completedAtUtc, "insufficient_items"), 0));
            Assert.True(resetSkills.TryComplete(Completion("skills-terminal", PlayerActionStatus.Cancelled, completedAtUtc, null)));
            Assert.True(clear.TryComplete(Completion("clear-terminal", PlayerActionStatus.ResultUnknown, completedAtUtc, "connection_lost")));
            Assert.True(resetData.TryComplete(Completion("data-terminal", PlayerActionStatus.Failed, completedAtUtc, "reset_failed")));
            Assert.False(resetData.TryComplete(Completion(
                "data-terminal", PlayerActionStatus.Succeeded, completedAtUtc.AddSeconds(1), null)));

            var query = new SqlitePlayerActionOperationQuery(database.ConnectionFactory);
            Assert.Equal(PlayerActionStatus.Rejected, query.Get("remove-terminal")!.Status);
            Assert.Equal(PlayerActionStatus.Cancelled, query.Get("skills-terminal")!.Status);
            Assert.Equal(PlayerActionStatus.ResultUnknown, query.Get("clear-terminal")!.Status);
            Assert.Equal(PlayerActionStatus.Failed, query.Get("data-terminal")!.Status);
        }

        [Fact]
        public async Task Concurrent_same_key_creation_returns_one_operation_and_different_types_reject_duplicate_ids()
        {
            using var database = UpgradedDatabase();
            using var secondFactory = new SqliteConnectionFactory(database.DatabasePath);
            var firstStore = new SqliteGrantItemOperationStore(database.ConnectionFactory);
            var secondStore = new SqliteGrantItemOperationStore(secondFactory);
            var createdAtUtc = Utc(12);

            var first = Task.Run(
                () => firstStore.CreatePending(GrantIntent("concurrent-a", "concurrent-key", createdAtUtc)),
                TestContext.Current.CancellationToken);
            var second = Task.Run(
                () => secondStore.CreatePending(GrantIntent("concurrent-b", "concurrent-key", createdAtUtc)),
                TestContext.Current.CancellationToken);
            var operations = await Task.WhenAll(first, second);

            Assert.Single(operations.Select(value => value.OperationId).Distinct(StringComparer.Ordinal));
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(1, connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM player_grant_item_operations WHERE client_request_key = 'concurrent-key';"));
            }
            var duplicateId = operations[0].OperationId;
            var clear = new SqliteClearInventoryOperationStore(database.ConnectionFactory);
            Assert.Throws<PlayerActionOperationIdConflictException>(() =>
                clear.CreatePending(ClearInventoryIntent(
                    duplicateId, "different-type-key", createdAtUtc.AddMinutes(1), true)));
        }

        [Fact]
        public void Database_constraints_reject_invalid_status_duplicate_keys_and_missing_evidence()
        {
            using var database = UpgradedDatabase();
            using var connection = database.ConnectionFactory.Open();
            const string commonColumns = @"
                operation_id, operator_id, target_crossplatform_id, target_entity_id,
                target_online_observed_at_utc, world_id, client_request_key,
                status, created_at_utc, catalog_version, internal_name,
                item_kind, quantity, hidden_item_confirmed";
            connection.Execute(
                "INSERT INTO player_grant_item_operations (" + commonColumns + @")
                 VALUES ('valid', 'owner', 'EOS-1', 7, 1, 'world-1', 'key',
                     'Pending', 1, 'catalog-v1', 'resourceWood', 'Item', 1, 1);");

            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO player_grant_item_operations (" + commonColumns + @")
                 VALUES ('duplicate-key', 'owner', 'EOS-1', 7, 1, 'world-1', 'key',
                     'Pending', 1, 'catalog-v1', 'resourceWood', 'Item', 1, 1);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO player_grant_item_operations (" + commonColumns + @")
                 VALUES ('invalid-status', 'other', 'EOS-1', 7, 1, 'world-1', 'key',
                     'Running', 1, 'catalog-v1', 'resourceWood', 'Item', 1, 1);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_reset_skills_operations (
                      operation_id, operator_id, target_crossplatform_id, target_entity_id,
                      target_online_observed_at_utc, world_id, client_request_key,
                      status, created_at_utc, before_skill_snapshot_id, danger_confirmed)
                  VALUES ('missing-evidence', 'other', 'EOS-1', 7, 1, 'world-1',
                      'missing-evidence-key', 'Pending', 1, 999999, 1);"));
        }

        [Fact]
        public void Unified_projection_contains_stable_action_summaries_without_parameters_or_error_text()
        {
            using var database = UpgradedDatabase();
            var createdAtUtc = Utc(13);
            var store = new SqliteGrantItemOperationStore(database.ConnectionFactory);
            store.CreatePending(GrantIntent(
                "projection-grant", "projection-key", createdAtUtc,
                internalName: "SecretInternalName"));
            Assert.True(store.TryComplete(
                Completion(
                    "projection-grant", PlayerActionStatus.Failed,
                    createdAtUtc.AddSeconds(1), "Secret Token /private/path"),
                actualQuantity: null));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<ProjectionRow>(
                @"SELECT source_kind, source_id, actor_subject, target_ref, action,
                         occurred_utc, status, correlation_id, has_details
                  FROM unified_audit_projection
                  WHERE source_id = 'projection-grant';");
            Assert.Equal("playerAction", row.source_kind);
            Assert.Equal("GrantItem", row.action);
            Assert.Equal("EOS-1", row.target_ref);
            Assert.Equal(createdAtUtc.ToUnixTimeMilliseconds(), row.occurred_utc);
            Assert.Equal("Failed", row.status);
            Assert.Equal(0, row.has_details);
            var rendered = string.Join("|", row.source_kind, row.source_id, row.actor_subject,
                row.target_ref, row.action, row.status, row.correlation_id);
            Assert.DoesNotContain("Secret", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("path", rendered, StringComparison.OrdinalIgnoreCase);
        }

        private static GrantItemOperationIntent GrantIntent(
            string operationId,
            string clientRequestKey,
            DateTimeOffset createdAtUtc,
            int quantity = 25,
            string correlationId = "corr-grant",
            long? beforeInventorySnapshotId = null,
            string internalName = "resourceWood") =>
            new GrantItemOperationIntent(
                operationId, "owner", Target(), clientRequestKey, correlationId, createdAtUtc,
                beforeInventorySnapshotId, null, "catalog-v1", internalName, "Item",
                quantity, 2, true);

        private static RemoveItemOperationIntent RemoveIntent(
            string operationId,
            string clientRequestKey,
            DateTimeOffset createdAtUtc) =>
            new RemoveItemOperationIntent(
                operationId, "owner", Target(), clientRequestKey, "corr-remove", createdAtUtc,
                null, null, "catalog-v1", "resourceStone", "Item", 4, null,
                PlayerItemRemovalScope.BagOnly, PlayerItemRemovalMode.Exact);

        private static ResetSkillsOperationIntent ResetSkillsIntent(
            string operationId,
            string clientRequestKey,
            DateTimeOffset createdAtUtc,
            bool dangerConfirmed) =>
            new ResetSkillsOperationIntent(
                operationId, "owner", Target(), clientRequestKey, "corr-skills", createdAtUtc,
                null, null, dangerConfirmed);

        private static ClearInventoryOperationIntent ClearInventoryIntent(
            string operationId,
            string clientRequestKey,
            DateTimeOffset createdAtUtc,
            bool dangerConfirmed) =>
            new ClearInventoryOperationIntent(
                operationId, "owner", Target(), clientRequestKey, "corr-clear", createdAtUtc,
                null, null, PlayerItemRemovalScope.BagOnly, dangerConfirmed);

        private static ResetPlayerDataOperationIntent ResetDataIntent(
            string operationId,
            string clientRequestKey,
            DateTimeOffset createdAtUtc,
            bool dangerConfirmed) =>
            new ResetPlayerDataOperationIntent(
                operationId, "owner", Target(), clientRequestKey, "corr-data", createdAtUtc,
                null, null, dangerConfirmed);

        private static PlayerActionOperationCompletion Completion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode) =>
            new PlayerActionOperationCompletion(
                operationId, status, completedAtUtc, failureCode,
                null, null, null, null);

        private static PlayerTargetStamp Target() =>
            new PlayerTargetStamp("EOS-1", 7, Utc(7), "world-1");

        private static PlayerInventorySnapshot InventorySnapshot(long id, DateTimeOffset observedAtUtc) =>
            new PlayerInventorySnapshot(
                id, "EOS-1", "server-1", "world-1", observedAtUtc,
                "3.0.1-b4", "catalog-v1", CatalogResolutionState.Resolved,
                "fingerprint-" + id, false, Array.Empty<InventoryItemScalar>());

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 27, hour, 0, 0, TimeSpan.Zero);

        private static string[] Columns(SqliteConnection connection, string table) =>
            connection.Query<SchemaColumn>("PRAGMA table_info(" + table + ");")
                .OrderBy(column => column.cid)
                .Select(column => column.name)
                .ToArray();

        private static string[] UniqueKeyColumns(SqliteConnection connection, string table)
        {
            var uniqueIndex = connection.Query<IndexListRow>("PRAGMA index_list(" + table + ");")
                .Single(index => index.unique == 1 && index.origin == "c");
            return connection.Query<IndexColumnRow>("PRAGMA index_info(" + uniqueIndex.name + ");")
                .OrderBy(column => column.seqno)
                .Select(column => column.name)
                .ToArray();
        }

        private static TemporaryDatabase UpgradedDatabase()
        {
            var database = new TemporaryDatabase();
            database.Upgrade();
            return database;
        }

        private sealed class SchemaColumn
        {
            public int cid { get; set; }
            public string name { get; set; } = string.Empty;
        }

        private sealed class IndexListRow
        {
            public string name { get; set; } = string.Empty;
            public int unique { get; set; }
            public string origin { get; set; } = string.Empty;
        }

        private sealed class IndexColumnRow
        {
            public int seqno { get; set; }
            public string name { get; set; } = string.Empty;
        }

        private sealed class GrantRow
        {
            public string catalog_version { get; set; } = string.Empty;
            public string internal_name { get; set; } = string.Empty;
            public string item_kind { get; set; } = string.Empty;
            public int quantity { get; set; }
            public int? quality { get; set; }
            public int hidden_item_confirmed { get; set; }
        }

        private sealed class RemoveRow
        {
            public string removal_scope { get; set; } = string.Empty;
            public string removal_mode { get; set; } = string.Empty;
        }

        private sealed class ProjectionRow
        {
            public string source_kind { get; set; } = string.Empty;
            public string source_id { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public string? target_ref { get; set; }
            public string action { get; set; } = string.Empty;
            public long occurred_utc { get; set; }
            public string status { get; set; } = string.Empty;
            public string? correlation_id { get; set; }
            public int has_details { get; set; }
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-player-action-store-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }

            public string DatabasePath => ConnectionFactory.DatabasePath;

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
