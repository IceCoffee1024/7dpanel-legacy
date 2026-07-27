using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations;
using LSTY.SevenDPanel.Application.WorldOperations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteWorldOperationStoreTests
    {
        private static readonly string[] ExpectedTables =
        {
            "feature_module_states",
            "world_change_set_chunks",
            "world_change_sets",
            "world_operation_block_targets",
            "world_operation_entity_targets",
            "world_operation_maintenance_targets",
            "world_operation_map_targets",
            "world_operation_prefab_targets",
            "world_operation_region_targets",
            "world_operations"
        };

        [Fact]
        public void Empty_database_upgrade_creates_only_fixed_world_schema_and_a_safe_audit_projection_once()
        {
            using var database = new TemporaryDatabase(false);

            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                ExpectedTables,
                connection.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN (" +
                    string.Join(",", ExpectedTables.Select((_, index) => "@p" + index)) +
                    ") ORDER BY name;",
                    ExpectedTables.Select((name, index) => new KeyValuePair<string, object>("p" + index, name))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName GLOB '*.Migrations.013_*';"));

            var schemaSql = string.Join(
                "\n",
                connection.Query<string>(
                    "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL AND (name LIKE 'world_%' OR name = 'feature_module_states');"));
            Assert.DoesNotContain("payload_json", schemaSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("file_path", schemaSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("type_name", schemaSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("world_operation_jobs", schemaSql, StringComparison.OrdinalIgnoreCase);

            var viewSql = connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            Assert.Contains("world_operations", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("feature_module_states", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("world_change_set_chunks", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("storage_resource_id", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("world_operation_entity_targets", viewSql, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Upgrade_from_012_preserves_existing_rows_and_adds_013_once()
        {
            using var database = new TemporaryDatabase(false);
            UpgradeThrough012(database.ConnectionFactory);

            using (var baseline = database.ConnectionFactory.Open())
            {
                Assert.Equal(
                    1,
                    baseline.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName GLOB '*.Migrations.012_*';"));
                baseline.Execute(
                    "INSERT INTO game_events (event_id, event_type, occurred_utc, observed_utc) VALUES ('before-013', 'PlayerJoined', 1785024000000, 1785024000000);");
            }

            database.Upgrade();

            using var upgraded = database.ConnectionFactory.Open();
            Assert.Equal(1, upgraded.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM game_events WHERE event_id = 'before-013';"));
            Assert.Equal(1, upgraded.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName GLOB '*.Migrations.013_*';"));
        }

        [Fact]
        public void Bridge_persists_each_typed_target_and_queries_by_operation_id()
        {
            using var database = new TemporaryDatabase();
            var bridge = database.Bridge;
            var intents = new[]
            {
                Intent(WorldOperationKind.MoveEntity, "entity", new WorldEntityOperationTarget(
                    "entity-42", 42, "EOS-player", "zombie-basic", null,
                    1, 2, 3, 4, 5, 6)),
                Intent(WorldOperationKind.RenderFullMap, "map", new WorldMapOperationTarget(
                    -100, -200, 100, 200)),
                Intent(WorldOperationKind.FillRegion, "region", new WorldRegionOperationTarget(
                    1, 2, 3, 4, 5, 6, null, "concrete")),
                Intent(WorldOperationKind.SetBlock, "block", new WorldBlockOperationTarget(
                    7, 8, 9, "steelBlock", 1, null)),
                Intent(WorldOperationKind.PlacePrefab, "prefab", new WorldPrefabOperationTarget(
                    "prefab-resource-1", null, 10, 11, 12, 2)),
                Intent(WorldOperationKind.ReloadItems, "maintenance", new WorldMaintenanceOperationTarget(null))
            };

            var receipts = intents.Select(bridge.Enqueue).ToArray();

            Assert.All(receipts, receipt => Assert.Equal(WorldOperationStatus.Queued, receipt.Status));
            Assert.All(receipts, receipt => Assert.Equal(receipt.OperationId, bridge.Get(receipt.OperationId).OperationId));
            var page = bridge.Query(new WorldOperationQuery(20, null, null, null, null, null));
            Assert.Equal(6, page.Items.Count);
            Assert.All(page.Items, record =>
            {
                Assert.Equal(TimeSpan.Zero, record.CreatedAtUtc.Offset);
                Assert.Equal("owner", record.ActorSubject);
                Assert.Equal("world-v1", record.WorldVersion);
                Assert.DoesNotContain("/", record.ConfirmationSummary, StringComparison.Ordinal);
            });

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal("WorldOperation", connection.ExecuteScalar<string>(
                "SELECT kind FROM jobs WHERE id = @JobId;", new { JobId = receipts[0].JobId.ToString("D") }));
            foreach (var table in ExpectedTables.Where(name => name.StartsWith("world_operation_", StringComparison.Ordinal) && name.EndsWith("_targets", StringComparison.Ordinal)))
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM " + table + ";"));
        }

        [Fact]
        public void Execution_store_rehydrates_the_single_typed_target_by_second_wave_job_id()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteWorldOperationStore(database.ConnectionFactory);
            var intents = new[]
            {
                Intent(WorldOperationKind.MoveEntity, "execute-entity", new WorldEntityOperationTarget(
                    "entity-42", 42, "EOS-player", "zombie-basic", "EOS-owner",
                    1, 2, 3, 4, 5, 6, 7, 8, "Hostile")),
                Intent(WorldOperationKind.RenderFullMap, "execute-map", new WorldMapOperationTarget(
                    -100, -200, 100, 200)),
                Intent(WorldOperationKind.FillRegion, "execute-region", new WorldRegionOperationTarget(
                    1, 2, 3, 4, 5, 6, "source-change", "concrete")),
                Intent(WorldOperationKind.SetBlock, "execute-block", new WorldBlockOperationTarget(
                    7, 8, 9, "steelBlock", 1, "cube")),
                Intent(WorldOperationKind.PlacePrefab, "execute-prefab", new WorldPrefabOperationTarget(
                    "prefab-resource-1", "prefab-instance-1", 10, 11, 12, 2,
                    10, 11, 12, 20, 21, 22)),
                Intent(WorldOperationKind.ReloadItems, "execute-maintenance", new WorldMaintenanceOperationTarget(
                    "entity-resource-1"))
            };

            foreach (var intent in intents)
            {
                var receipt = database.Bridge.Enqueue(intent);
                var execution = store.ReadForExecution(receipt.JobId);

                Assert.Equal(receipt.OperationId, execution.OperationId);
                Assert.Equal(receipt.JobId, execution.JobId);
                Assert.Equal(intent.Kind, execution.Intent.Kind);
                Assert.Equal(intent.WorldId, execution.Intent.WorldId);
                Assert.Equal(intent.WorldVersion, execution.Intent.WorldVersion);
                Assert.Equal(intent.MapResourceVersion, execution.Intent.MapResourceVersion);
                Assert.Equal(intent.Target, execution.Intent.Target);
            }

            Assert.Throws<KeyNotFoundException>(() =>
                store.ReadForExecution(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")));
        }

        [Theory]
        [InlineData("Queued", WorldOperationStatus.Queued)]
        [InlineData("Running", WorldOperationStatus.Running)]
        [InlineData("Succeeded", WorldOperationStatus.Succeeded)]
        [InlineData("Failed", WorldOperationStatus.Failed)]
        [InlineData("Cancelled", WorldOperationStatus.Cancelled)]
        [InlineData("Interrupted", WorldOperationStatus.Interrupted)]
        [InlineData("ResultUnknown", WorldOperationStatus.ResultUnknown)]
        public void Bridge_maps_second_wave_job_statuses(string jobStatus, WorldOperationStatus expected)
        {
            using var database = new TemporaryDatabase();
            var receipt = database.Bridge.Enqueue(Intent(
                WorldOperationKind.CollectGarbage,
                "status-" + jobStatus,
                new WorldMaintenanceOperationTarget(null)));
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    @"UPDATE jobs SET status = @Status,
                          started_at_utc = CASE WHEN @Status = 'Queued' THEN NULL ELSE @Started END,
                          completed_at_utc = CASE WHEN @Status IN ('Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown') THEN @Completed ELSE NULL END,
                          progress_current = 3, progress_total = 5, error_code = @Error,
                          row_version = row_version + 1
                      WHERE id = @JobId;",
                    new
                    {
                        Status = jobStatus,
                        Started = Utc(1).ToUnixTimeMilliseconds(),
                        Completed = Utc(2).ToUnixTimeMilliseconds(),
                        Error = jobStatus == "Failed" ? "world_operation_failed" : null,
                        JobId = receipt.JobId.ToString("D")
                    });
            }

            var record = database.Bridge.Get(receipt.OperationId);
            Assert.Equal(expected, record.Status);
            Assert.Equal(new WorldOperationProgress(3, 5), record.Progress);
            Assert.Equal(jobStatus == "Failed" ? "world_operation_failed" : null, record.ErrorCode);
        }

        [Fact]
        public void Rollback_failure_overrides_the_second_wave_job_terminal_status()
        {
            using var database = new TemporaryDatabase();
            var receipt = database.Bridge.Enqueue(Intent(
                WorldOperationKind.UndoChangeSet,
                "rollback",
                new WorldRegionOperationTarget(1, 2, 3, 4, 5, 6, "change-set-source", null)));
            using (var connection = database.ConnectionFactory.Open())
            {
                Assert.Equal(1, connection.Execute(
                    @"UPDATE world_operations
                      SET rollback_failure_code = 'world_rollback_failed',
                          rollback_failed_at_utc = @CompletedAtUtc
                      WHERE operation_id = @OperationId;",
                    new
                    {
                        receipt.OperationId,
                        CompletedAtUtc = Utc(2).ToUnixTimeMilliseconds()
                    }));
            }

            var record = database.Bridge.Get(receipt.OperationId);
            Assert.Equal(WorldOperationStatus.RollbackFailed, record.Status);
            Assert.Equal("world_rollback_failed", record.ErrorCode);
            Assert.Equal(Utc(2), record.CompletedAtUtc);
        }

        [Fact]
        public void Only_the_owner_can_cancel_a_queued_operation()
        {
            using var database = new TemporaryDatabase();
            var receipt = database.Bridge.Enqueue(Intent(
                WorldOperationKind.CollectGarbage,
                "cancel",
                new WorldMaintenanceOperationTarget(null)));

            Assert.False(database.Bridge.RequestCancellation(receipt.OperationId, "intruder"));
            Assert.True(database.Bridge.RequestCancellation(receipt.OperationId, " owner "));
            Assert.False(database.Bridge.RequestCancellation(receipt.OperationId, "owner"));
            Assert.Equal(WorldOperationStatus.Cancelled, database.Bridge.Get(receipt.OperationId).Status);
        }

        [Fact]
        public void Failed_target_insert_rolls_back_the_job_and_operation_and_never_stores_unsafe_summary()
        {
            using var database = new TemporaryDatabase();
            var invalid = Intent(
                WorldOperationKind.SetBlock,
                "invalid-target",
                new WorldBlockOperationTarget(1, 2, 3, "steelBlock", 99, null));

            Assert.Throws<SqliteException>(() => database.Bridge.Enqueue(invalid));
            Assert.Throws<ArgumentException>(() => database.Bridge.Enqueue(new WorldOperationIntent(
                "owner", WorldOperationKind.CollectGarbage, "world", "world-v1", "map-v1",
                "unsafe-summary", "Read C:\\server\\world payload_json", false,
                new WorldMaintenanceOperationTarget(null), Utc(0))));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM jobs WHERE correlation_id IN ('invalid-target', 'unsafe-summary');"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM world_operations WHERE correlation_id IN ('invalid-target', 'unsafe-summary');"));
        }

        [Fact]
        public void Target_tables_are_mutually_exclusive_and_change_sets_validate_hashes_and_retention()
        {
            using var database = new TemporaryDatabase();
            var receipt = database.Bridge.Enqueue(Intent(
                WorldOperationKind.MoveEntity,
                "exclusive",
                new WorldEntityOperationTarget("entity-1", 1, null, null, null, null, null, null, null, null, null)));

            using var connection = database.ConnectionFactory.Open();
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO world_operation_map_targets (
                      operation_id, minimum_x, minimum_z, maximum_x, maximum_z)
                  VALUES (@OperationId, NULL, NULL, NULL, NULL);",
                new { receipt.OperationId }));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO world_change_sets (
                      change_set_id, source_operation_id, world_id, world_version,
                      minimum_x, minimum_y, minimum_z, maximum_x, maximum_y, maximum_z,
                      before_hash, after_hash, storage_resource_id, created_at_utc, expires_at_utc)
                  VALUES ('bad-hash', @OperationId, 'world', 'v1', 0, 0, 0, 1, 1, 1,
                      'short', @Hash, 'resource-one', @Created, @Expires);",
                new
                {
                    receipt.OperationId,
                    Hash = new string('b', 64),
                    Created = Utc(0).ToUnixTimeMilliseconds(),
                    Expires = Utc(10).ToUnixTimeMilliseconds()
                }));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO world_change_sets (
                      change_set_id, source_operation_id, world_id, world_version,
                      minimum_x, minimum_y, minimum_z, maximum_x, maximum_y, maximum_z,
                      before_hash, after_hash, storage_resource_id, created_at_utc, expires_at_utc)
                  VALUES ('bad-retention', @OperationId, 'world', 'v1', 0, 0, 0, 1, 1, 1,
                      @BeforeHash, @AfterHash, 'resource-two', @Created, @Created);",
                new
                {
                    receipt.OperationId,
                    BeforeHash = new string('a', 64),
                    AfterHash = new string('b', 64),
                    Created = Utc(0).ToUnixTimeMilliseconds()
                }));
        }

        private static WorldOperationIntent Intent(
            WorldOperationKind kind,
            string correlationId,
            WorldOperationTarget target) =>
            new WorldOperationIntent(
                "owner", kind, "world", "world-v1", "map-v1", correlationId,
                "Approve " + kind, kind == WorldOperationKind.FillRegion, target, Utc(0));

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private static void UpgradeThrough012(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName => resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, 12).Any(version =>
                            resourceName.IndexOf(
                                $".Migrations.{version:D3}_",
                                StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-world-operation-tests", Guid.NewGuid().ToString("N"));

            public TemporaryDatabase(bool upgrade = true)
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                Bridge = new SqliteWorldOperationJobBridge(ConnectionFactory, () => Utc(5));
                if (upgrade) Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteWorldOperationJobBridge Bridge { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
