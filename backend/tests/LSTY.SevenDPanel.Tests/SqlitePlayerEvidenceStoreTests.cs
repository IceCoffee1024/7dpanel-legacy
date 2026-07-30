using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqlitePlayerEvidenceStoreTests
    {
        [Fact]
        public void Upgrade_creates_player_evidence_schema_foreign_keys_and_query_indexes()
        {
            using var database = new TemporaryDatabase();

            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                new[]
                {
                    "inventory_gaps",
                    "player_activity_events",
                    "player_inventory_item_mods",
                    "player_inventory_items",
                    "player_inventory_snapshots",
                    "player_sessions",
                    "player_skill_snapshots",
                    "player_skill_values",
                    "skill_gaps"
                },
                connection.Query<string>(
                    @"SELECT name
                      FROM sqlite_master
                      WHERE type = 'table' AND name IN (
                          'player_sessions', 'player_activity_events',
                          'player_inventory_snapshots', 'player_inventory_items',
                          'player_inventory_item_mods', 'player_skill_snapshots',
                          'player_skill_values', 'inventory_gaps', 'skill_gaps')
                      ORDER BY name;").ToArray());
            Assert.Equal(
                new[] { "crossplatform_id", "observed_at_utc", "id" },
                IndexColumns(connection, "ix_player_inventory_snapshots_player_observed"));
            Assert.Equal(
                new[] { "crossplatform_id", "observed_at_utc", "id" },
                IndexColumns(connection, "ix_player_skill_snapshots_player_observed"));
            Assert.NotEmpty(connection.Query<ForeignKeyRow>(
                "PRAGMA foreign_key_list(player_inventory_items);"));
            Assert.NotEmpty(connection.Query<ForeignKeyRow>(
                "PRAGMA foreign_key_list(player_inventory_item_mods);"));
            Assert.NotEmpty(connection.Query<ForeignKeyRow>(
                "PRAGMA foreign_key_list(player_skill_values);"));
        }

        [Fact]
        public void Upgrade_from_009_preserves_existing_data_and_repeat_bootstrap_is_safe()
        {
            using var database = new TemporaryDatabase();
            UpgradeThrough009(database.ConnectionFactory);
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    @"INSERT INTO jobs (
                          id, kind, status, actor_subject, idempotency_key, created_at_utc)
                      VALUES ('job-before-010', 'WorldBackup', 'Queued', 'owner',
                          'job-before-010-key', 1785052800000);");
            }

            database.Upgrade();
            database.Upgrade();

            using var upgraded = database.ConnectionFactory.Open();
            Assert.Equal(
                "Queued",
                upgraded.ExecuteScalar<string>(
                    "SELECT status FROM jobs WHERE id = 'job-before-010';"));
            Assert.Equal(
                1,
                upgraded.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName LIKE '%Migrations.010_PlayerEvidenceActions.sql';"));
        }

        [Fact]
        public void Sessions_and_activity_round_trip_utc_and_use_deterministic_descending_ranges()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var observedAtUtc = Utc(10, 0, 0, 123);
            store.AppendSession(new PlayerSession(
                101, "EOS-1", "server-1", "world-1", observedAtUtc,
                observedAtUtc.AddMinutes(30), "Disconnected",
                new PlayerPosition(1.25f, 70.5f, -9.75f), PlayerProfileSectionState.Available));
            store.AppendSession(new PlayerSession(
                102, "EOS-1", "server-1", "world-1", observedAtUtc,
                null, null, null, PlayerProfileSectionState.Partial));
            store.AppendActivity(new PlayerActivityEvent(
                201, "EOS-1", "server-1", "world-1", "Joined",
                observedAtUtc, "corr-1", PlayerProfileSectionState.Available));
            store.AppendActivity(new PlayerActivityEvent(
                202, "EOS-1", "server-1", "world-1", "Saved",
                observedAtUtc, null, PlayerProfileSectionState.Partial));

            var range = new PlayerEvidenceRangeQuery(
                "EOS-1", observedAtUtc, observedAtUtc.AddHours(1), 20);
            var sessions = store.GetSessions(range);
            var activity = store.GetActivity(range);

            Assert.Equal(new long[] { 102, 101 }, sessions.Select(value => value.SessionId));
            Assert.Equal(new long[] { 202, 201 }, activity.Select(value => value.ActivityId));
            Assert.Equal(TimeSpan.Zero, sessions[1].StartedAtUtc.Offset);
            Assert.Equal(observedAtUtc, sessions[1].StartedAtUtc);
            Assert.Equal(observedAtUtc.AddMinutes(30), sessions[1].EndedAtUtc);
            Assert.Equal(1.25f, sessions[1].LastPosition!.Value.X);
            Assert.Equal("corr-1", activity[1].CorrelationId);
        }

        [Fact]
        public void Inventory_snapshot_aggregate_round_trips_with_keyset_and_intersecting_gaps()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var observedAtUtc = Utc(11, 0, 0, 321);
            store.AppendInventoryGap(new PlayerEvidenceGap(
                1, "EOS-1", observedAtUtc.AddMinutes(-1), observedAtUtc.AddMinutes(1),
                "QueueFull", 2));
            store.AppendInventorySnapshot(InventorySnapshot(
                301, observedAtUtc, "fingerprint-a", false,
                new InventoryItemScalar(
                    "Bag", 0, "meleeToolRepairT0StoneAxe", 2, 3, 0.125m,
                    new[] { "modA", "modB" }),
                new InventoryItemScalar(
                    "Bag", 2, "resourceWood", 50, null, null,
                    Array.Empty<string>())));
            store.AppendInventorySnapshot(InventorySnapshot(
                302, observedAtUtc, "fingerprint-b", true,
                new InventoryItemScalar(
                    "Bag", 1, "resourceStone", 10, null, null,
                    Array.Empty<string>())));

            var first = store.GetInventorySnapshots(
                new PlayerInventorySnapshotsQuery("EOS-1", 1, null));
            var second = store.GetInventorySnapshots(
                new PlayerInventorySnapshotsQuery("EOS-1", 1, first.NextCursor));

            Assert.Equal(302, Assert.Single(first.Snapshots).SnapshotId);
            Assert.Equal(301, Assert.Single(second.Snapshots).SnapshotId);
            Assert.NotNull(first.NextCursor);
            Assert.Null(second.NextCursor);
            var detailed = second.Snapshots[0];
            Assert.Equal(observedAtUtc, detailed.ObservedAtUtc);
            Assert.Equal(CatalogResolutionState.Resolved, detailed.CatalogResolution);
            Assert.Equal(2, detailed.Items.Count);
            Assert.Equal(0.125m, detailed.Items[0].UseAmount);
            Assert.Equal(new[] { "modA", "modB" }, detailed.Items[0].ModInternalNames);
            Assert.Equal(1, Assert.Single(second.Gaps).GapId);
        }

        [Fact]
        public void Skill_snapshot_values_round_trip_with_keyset_and_gap_metadata()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var observedAtUtc = Utc(12, 0, 0, 456);
            store.AppendSkillGap(new PlayerEvidenceGap(
                11, "EOS-1", observedAtUtc.AddSeconds(-1), observedAtUtc.AddSeconds(1),
                "StoreFailure", 1));
            store.AppendSkillSnapshot(SkillSnapshot(401, observedAtUtc,
                new PlayerSkillValue("skillA", SkillValueState.Known, 4, 0, 10, 2, "parent"),
                new PlayerSkillValue("skillB", SkillValueState.UnsupportedByVersion, null, null, null, null, null)));
            store.AppendSkillSnapshot(SkillSnapshot(402, observedAtUtc,
                new PlayerSkillValue("skillA", SkillValueState.Known, 5, 0, 10, 2, "parent")));

            var first = store.GetSkillSnapshots(new PlayerSkillSnapshotsQuery("EOS-1", 1, null));
            var second = store.GetSkillSnapshots(
                new PlayerSkillSnapshotsQuery("EOS-1", 1, first.NextCursor));

            Assert.Equal(402, Assert.Single(first.Snapshots).SnapshotId);
            var detailed = Assert.Single(second.Snapshots);
            Assert.Equal(401, detailed.SnapshotId);
            Assert.Equal(2, detailed.Values.Count);
            Assert.Null(detailed.Values[1].Value);
            Assert.Equal(SkillValueState.UnsupportedByVersion, detailed.Values[1].State);
            Assert.Equal(11, Assert.Single(second.Gaps).GapId);
        }

        [Fact]
        public void Aggregate_append_rolls_back_parent_children_and_mods_when_a_child_fails()
        {
            using var database = UpgradedDatabase();
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    @"CREATE TRIGGER fail_test_inventory_mod
                      BEFORE INSERT ON player_inventory_item_mods
                      WHEN NEW.internal_name = 'mod-fail'
                      BEGIN
                          SELECT RAISE(ABORT, 'injected_inventory_mod_failure');
                      END;");
            }
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);

            Assert.Throws<SqliteException>(() => store.AppendInventorySnapshot(InventorySnapshot(
                501, Utc(13), "rollback", false,
                new InventoryItemScalar(
                    "Bag", 0, "resourceWood", 1, null, null,
                    new[] { "mod-ok", "mod-fail" }))));

            using var verification = database.ConnectionFactory.Open();
            Assert.Equal(0, verification.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM player_inventory_snapshots WHERE id = 501;"));
            Assert.Equal(0, verification.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM player_inventory_items WHERE snapshot_id = 501;"));
            Assert.Equal(0, verification.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM player_inventory_item_mods WHERE snapshot_id = 501;"));
        }

        [Fact]
        public void Overlapping_gaps_merge_by_player_and_reason_while_duplicate_ids_are_idempotent()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var start = Utc(14);
            var first = new PlayerEvidenceGap(601, "EOS-1", start, start.AddMinutes(5), "QueueFull", 2);
            store.AppendInventoryGap(first);
            store.AppendInventoryGap(first);
            store.AppendInventoryGap(new PlayerEvidenceGap(
                602, "EOS-1", start.AddMinutes(4), start.AddMinutes(10), "QueueFull", 3));
            store.AppendInventoryGap(new PlayerEvidenceGap(
                603, "EOS-1", start.AddMinutes(4), start.AddMinutes(10), "StoreFailure", 4));

            var gaps = store.GetInventoryGaps(new PlayerEvidenceRangeQuery(
                "EOS-1", start, start.AddMinutes(15), 20));

            Assert.Equal(2, gaps.Count);
            var merged = Assert.Single(gaps, gap => gap.Reason == "QueueFull");
            Assert.Equal(601, merged.GapId);
            Assert.Equal(start, merged.StartedAtUtc);
            Assert.Equal(start.AddMinutes(10), merged.EndedAtUtc);
            Assert.Equal(5, merged.EstimatedLostCount);
        }

        [Fact]
        public async Task Concurrent_writers_are_visible_to_range_reads_without_lost_rows()
        {
            using var database = UpgradedDatabase();
            using var secondFactory = new SqliteConnectionFactory(database.DatabasePath);
            var firstStore = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var secondStore = new SqlitePlayerEvidenceStore(secondFactory);
            var observedAtUtc = Utc(15);

            var firstWriter = Task.Run(() =>
            {
                for (var index = 0; index < 10; index++)
                    firstStore.AppendActivity(Activity(700 + index, observedAtUtc.AddMilliseconds(index)));
            }, TestContext.Current.CancellationToken);
            var secondWriter = Task.Run(() =>
            {
                for (var index = 0; index < 10; index++)
                    secondStore.AppendActivity(Activity(800 + index, observedAtUtc.AddMilliseconds(index)));
            }, TestContext.Current.CancellationToken);

            await Task.WhenAll(firstWriter, secondWriter);

            Assert.Equal(
                20,
                firstStore.GetActivity(new PlayerEvidenceRangeQuery(
                    "EOS-1", observedAtUtc, observedAtUtc.AddMinutes(1), 100)).Count);
        }

        [Fact]
        public void Duplicate_snapshot_ids_fail_without_overwriting_existing_evidence()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            store.AppendInventorySnapshot(InventorySnapshot(901, Utc(16), "original", false));

            Assert.Throws<SqliteException>(() => store.AppendInventorySnapshot(
                InventorySnapshot(901, Utc(16).AddSeconds(1), "replacement", false)));

            var snapshot = Assert.Single(store.GetInventorySnapshots(
                new PlayerInventorySnapshotsQuery("EOS-1", 10, null)).Snapshots);
            Assert.Equal("original", snapshot.Fingerprint);
        }

        [Fact]
        public void Compact_keeps_first_latest_changes_admin_boundaries_and_one_stable_bucket_winner()
        {
            using var database = UpgradedDatabase();
            var store = new SqlitePlayerEvidenceStore(database.ConnectionFactory);
            var start = Utc(1);
            store.AppendInventorySnapshot(InventorySnapshot(1001, start, "a", false));
            store.AppendInventorySnapshot(InventorySnapshot(1002, start.AddMinutes(10), "a", false));
            store.AppendInventorySnapshot(InventorySnapshot(1003, start.AddMinutes(20), "a", false));
            store.AppendInventorySnapshot(InventorySnapshot(1004, start.AddMinutes(30), "b", false));
            store.AppendInventorySnapshot(InventorySnapshot(1005, start.AddMinutes(40), "b", true));
            store.AppendInventorySnapshot(InventorySnapshot(1006, start.AddMinutes(50), "b", false));
            store.AppendInventorySnapshot(InventorySnapshot(1007, start.AddHours(3), "b", false));
            for (var index = 0; index < 6; index++)
                store.AppendSkillSnapshot(SkillSnapshot(1101 + index, start.AddMinutes(index * 10)));
            store.AppendSkillSnapshot(SkillSnapshot(1107, start.AddHours(3)));
            var request = new PlayerEvidenceCompactionRequest(start.AddHours(2), TimeSpan.FromHours(1));

            store.Compact(request);
            store.Compact(request);

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                new long[] { 1001, 1004, 1005, 1006, 1007 },
                connection.Query<long>(
                    "SELECT id FROM player_inventory_snapshots ORDER BY id;").ToArray());
            Assert.Equal(
                new long[] { 1101, 1106, 1107 },
                connection.Query<long>(
                    "SELECT id FROM player_skill_snapshots ORDER BY id;").ToArray());
        }

        private static TemporaryDatabase UpgradedDatabase()
        {
            var database = new TemporaryDatabase();
            database.Upgrade();
            return database;
        }

        private static PlayerInventorySnapshot InventorySnapshot(
            long id,
            DateTimeOffset observedAtUtc,
            string fingerprint,
            bool adminBoundary,
            params InventoryItemScalar[] items) =>
            new PlayerInventorySnapshot(
                id, "EOS-1", "server-1", "world-1", observedAtUtc,
                "3.0.1-b4", "catalog-v1", CatalogResolutionState.Resolved,
                fingerprint, adminBoundary, items);

        private static PlayerSkillSnapshot SkillSnapshot(
            long id,
            DateTimeOffset observedAtUtc,
            params PlayerSkillValue[] values) =>
            new PlayerSkillSnapshot(
                id, "EOS-1", "server-1", "world-1", observedAtUtc,
                "3.0.1-b4", 12, 3, values);

        private static PlayerActivityEvent Activity(long id, DateTimeOffset observedAtUtc) =>
            new PlayerActivityEvent(
                id, "EOS-1", "server-1", "world-1", "Saved",
                observedAtUtc, null, PlayerProfileSectionState.Available);

        private static DateTimeOffset Utc(int hour, int minute = 0, int second = 0, int millisecond = 0) =>
            new DateTimeOffset(2026, 7, 27, hour, minute, second, millisecond, TimeSpan.Zero);

        private static string[] IndexColumns(SqliteConnection connection, string indexName) =>
            connection.Query<IndexColumnRow>("PRAGMA index_info(" + indexName + ");")
                .OrderBy(row => row.seqno)
                .Select(row => row.name)
                .ToArray();

        private static void UpgradeThrough009(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    IsMigrationThrough009)
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        private static bool IsMigrationThrough009(string resourceName)
        {
            const string marker = ".Migrations.";
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)) return false;

            var markerIndex = resourceName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0) return false;

            var versionStart = markerIndex + marker.Length;
            return resourceName.Length >= versionStart + 3
                   && int.TryParse(resourceName.Substring(versionStart, 3), out var version)
                   && version <= 9;
        }

        private sealed class IndexColumnRow
        {
            public int seqno { get; set; }
            public string name { get; set; } = string.Empty;
        }

        private sealed class ForeignKeyRow
        {
            public int id { get; set; }
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-player-evidence-store-tests",
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
