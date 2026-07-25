using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqlitePlayerActionAuditTrailTests
    {
        [Fact]
        public void Upgrade_creates_player_action_audit_schema_and_can_be_repeated()
        {
            using var database = new TemporaryAuditDatabase();
            var bootstrapper = new SqliteDatabaseBootstrapper(database.ConnectionFactory);

            bootstrapper.Upgrade();
            bootstrapper.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'player_action_audit';"));
            Assert.Equal(
                4,
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaVersions;"));
        }

        [Fact]
        public void CreatePending_persists_the_immutable_action_snapshot()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            var store = new SqlitePlayerActionAuditTrail(database.ConnectionFactory);
            var requestedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

            store.CreatePending(Intent("operation-1", requestedAtUtc));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<PlayerActionAuditRow>(
                "SELECT * FROM player_action_audit WHERE operation_id = 'operation-1';");
            Assert.Equal("kick", row.action_type);
            Assert.Equal("owner", row.actor_subject);
            Assert.Equal(7, row.target_entity_id);
            Assert.Null(row.target_name);
            Assert.Equal("Steam_123", row.target_platform_id);
            Assert.Equal("Steam", row.target_platform);
            Assert.Equal("违反服务器规则", row.reason);
            Assert.Equal(requestedAtUtc.ToUnixTimeMilliseconds(), row.requested_utc);
            Assert.Null(row.completed_utc);
            Assert.Equal("Pending", row.status);
            Assert.Null(row.failure_code);
        }

        [Fact]
        public void TryComplete_updates_pending_once_without_overwriting_terminal_evidence()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            var store = new SqlitePlayerActionAuditTrail(database.ConnectionFactory);
            var completedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 1, TimeSpan.Zero);
            store.CreatePending(Intent("operation-1", completedAtUtc.AddSeconds(-1)));

            Assert.True(store.TryComplete(PlayerActionAuditCompletion.Succeeded(
                "operation-1",
                completedAtUtc,
                "Test Player")));
            Assert.False(store.TryComplete(PlayerActionAuditCompletion.Failed(
                "operation-1",
                completedAtUtc.AddSeconds(1),
                "Replacement",
                "player_identity_changed")));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<PlayerActionAuditRow>(
                "SELECT * FROM player_action_audit WHERE operation_id = 'operation-1';");
            Assert.Equal("Succeeded", row.status);
            Assert.Equal("Test Player", row.target_name);
            Assert.Equal(completedAtUtc.ToUnixTimeMilliseconds(), row.completed_utc);
            Assert.Null(row.failure_code);
        }

        [Fact]
        public void MarkPendingUnknown_recovers_only_pending_records_across_store_instances()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            var requestedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
            var firstStore = new SqlitePlayerActionAuditTrail(database.ConnectionFactory);
            firstStore.CreatePending(Intent("pending", requestedAtUtc));
            firstStore.CreatePending(Intent("succeeded", requestedAtUtc));
            Assert.True(firstStore.TryComplete(PlayerActionAuditCompletion.Succeeded(
                "succeeded",
                requestedAtUtc.AddSeconds(1),
                "Test Player")));

            using var reopenedFactory = new SqliteConnectionFactory(database.DatabasePath);
            var reopenedStore = new SqlitePlayerActionAuditTrail(reopenedFactory);
            var recoveredAtUtc = requestedAtUtc.AddMinutes(1);

            Assert.Equal(1, reopenedStore.MarkPendingUnknown(recoveredAtUtc));
            Assert.Equal(0, reopenedStore.MarkPendingUnknown(recoveredAtUtc.AddSeconds(1)));

            using var connection = reopenedFactory.Open();
            var rows = connection.Query<PlayerActionAuditRow>(
                "SELECT * FROM player_action_audit ORDER BY operation_id;").AsList();
            Assert.Equal("Unknown", rows[0].status);
            Assert.Equal("process_interrupted", rows[0].failure_code);
            Assert.Equal(recoveredAtUtc.ToUnixTimeMilliseconds(), rows[0].completed_utc);
            Assert.Equal("Succeeded", rows[1].status);
        }

        [Fact]
        public void Database_constraints_reject_invalid_status_and_oversized_reason()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, status)
                  VALUES ('invalid-status', 'kick', 'owner', 7, 'Steam_123', 'Steam', 'reason', 1, 'Running');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, status)
                  VALUES ('long-reason', 'kick', 'owner', 7, 'Steam_123', 'Steam', @Reason, 1, 'Pending');",
                new { Reason = new string('x', 201) }));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, status)
                  VALUES ('wrong-action', 'ban', 'owner', 7, 'Steam_123', 'Steam', 'reason', 1, 'Pending');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, status)
                  VALUES ('negative-entity', 'kick', 'owner', -1, 'Steam_123', 'Steam', 'reason', 1, 'Pending');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, completed_utc, status)
                  VALUES ('pending-completed', 'kick', 'owner', 7, 'Steam_123', 'Steam', 'reason', 1, 2, 'Pending');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id,
                      target_platform_id, target_platform, reason, requested_utc, status)
                  VALUES ('terminal-incomplete', 'kick', 'owner', 7, 'Steam_123', 'Steam', 'reason', 1, 'Failed');"));
        }

        [Fact]
        public async Task Concurrent_completion_allows_exactly_one_terminal_update()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            var requestedAtUtc = new DateTimeOffset(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
            new SqlitePlayerActionAuditTrail(database.ConnectionFactory)
                .CreatePending(Intent("operation-1", requestedAtUtc));
            using var secondFactory = new SqliteConnectionFactory(database.DatabasePath);
            var firstStore = new SqlitePlayerActionAuditTrail(database.ConnectionFactory);
            var secondStore = new SqlitePlayerActionAuditTrail(secondFactory);
            using var ready = new CountdownEvent(2);
            using var start = new ManualResetEventSlim(false);

            Task<bool> CompleteAsync(SqlitePlayerActionAuditTrail store, PlayerActionAuditCompletion completion)
            {
                return Task.Run(() =>
                {
                    ready.Signal();
                    start.Wait(TestContext.Current.CancellationToken);
                    return store.TryComplete(completion);
                }, TestContext.Current.CancellationToken);
            }

            var succeeded = CompleteAsync(
                firstStore,
                PlayerActionAuditCompletion.Succeeded(
                    "operation-1",
                    requestedAtUtc.AddSeconds(1),
                    "Test Player"));
            var failed = CompleteAsync(
                secondStore,
                PlayerActionAuditCompletion.Failed(
                    "operation-1",
                    requestedAtUtc.AddSeconds(2),
                    "Test Player",
                    "player_kick_failed"));
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            start.Set();

            var results = await Task.WhenAll(succeeded, failed);

            Assert.Single(Array.FindAll(results, result => result));
            using var connection = database.ConnectionFactory.Open();
            Assert.Contains(
                connection.ExecuteScalar<string>(
                    "SELECT status FROM player_action_audit WHERE operation_id = 'operation-1';"),
                new[] { "Succeeded", "Failed" });
        }

        [Fact]
        public void Write_lock_failure_releases_store_connection_for_a_later_write()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            using var shortTimeoutFactory = new SqliteConnectionFactory(
                database.DatabasePath,
                defaultTimeoutSeconds: 1);
            var store = new SqlitePlayerActionAuditTrail(shortTimeoutFactory);
            using (var lockConnection = database.ConnectionFactory.Open())
            using (var transaction = lockConnection.BeginTransaction(deferred: false))
            {
                Assert.Throws<SqliteException>(() =>
                    store.CreatePending(Intent("locked", DateTimeOffset.UtcNow)));
                transaction.Rollback();
            }

            store.CreatePending(Intent("after-lock", DateTimeOffset.UtcNow));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM player_action_audit WHERE operation_id = 'after-lock';"));
        }

        private static PlayerActionAuditIntent Intent(
            string operationId,
            DateTimeOffset requestedAtUtc)
        {
            return new PlayerActionAuditIntent(
                operationId,
                "owner",
                7,
                new PlayerPlatformIdentity("Steam_123", "Steam"),
                "违反服务器规则",
                requestedAtUtc);
        }

        private sealed class PlayerActionAuditRow
        {
            public string action_type { get; set; } = string.Empty;
            public string actor_subject { get; set; } = string.Empty;
            public int target_entity_id { get; set; }
            public string? target_name { get; set; }
            public string target_platform_id { get; set; } = string.Empty;
            public string target_platform { get; set; } = string.Empty;
            public string reason { get; set; } = string.Empty;
            public long requested_utc { get; set; }
            public long? completed_utc { get; set; }
            public string status { get; set; } = string.Empty;
            public string? failure_code { get; set; }
        }

        private sealed class TemporaryAuditDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryAuditDatabase()
            {
                directory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-player-action-audit-tests",
                    Guid.NewGuid().ToString("N"));
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public string DatabasePath => ConnectionFactory.DatabasePath;

            public void Upgrade()
            {
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }
    }
}
