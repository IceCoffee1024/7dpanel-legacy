using System;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteServerOperationStoreTests
    {
        [Fact]
        public void Upgrade_creates_lifecycle_schema_and_DbUp_journal_makes_repeat_safe()
        {
            using var database = new TemporaryDatabase();

            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'server_operation_lifecycle';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName LIKE '%Migrations.019_ServerOperationLifecycle.sql';"));
        }

        [Fact]
        public void Store_enforces_CAS_legal_transitions_terminal_protection_and_UTC_fields()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteServerOperationStore(database.ConnectionFactory);
            var requested = Utc(0);
            store.CreateQueued(Queued("operation-1", requested));

            Assert.False(store.TryTransition("operation-1", ServerOperationLifecycleStatus.Running,
                ServerOperationLifecycleStatus.Succeeded, Utc(1), null));
            Assert.True(store.TryTransition("operation-1", ServerOperationLifecycleStatus.Queued,
                ServerOperationLifecycleStatus.Running, Utc(1), null));
            Assert.True(store.TryTransition("operation-1", ServerOperationLifecycleStatus.Running,
                ServerOperationLifecycleStatus.Succeeded, Utc(2), null));
            Assert.False(store.TryTransition("operation-1", ServerOperationLifecycleStatus.Succeeded,
                ServerOperationLifecycleStatus.Failed, Utc(3), "shutdown_failed"));

            var operation = store.Get("operation-1")!;
            Assert.Equal(ServerOperationLifecycleStatus.Succeeded, operation.Status);
            Assert.Equal("owner", operation.ActorSubject);
            Assert.Equal("origin-a", operation.OriginProcessInstanceId);
            Assert.Equal(requested, operation.RequestedAtUtc);
            Assert.Equal(Utc(1), operation.StartedAtUtc);
            Assert.Equal(Utc(2), operation.CompletedAtUtc);
            Assert.Equal(TimeSpan.Zero, operation.CompletionDeadlineUtc.Offset);
            Assert.Null(operation.FailureCode);
        }

        [Fact]
        public void Recovery_marks_succeeded_only_for_a_different_process_after_game_ready_and_within_window()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            var store = new SqliteServerOperationStore(database.ConnectionFactory);
            store.CreateQueued(Queued("different-process", Utc(0)));
            store.CreateQueued(Queued("same-process", Utc(0)));
            store.CreateQueued(Queued("expired", Utc(0)));
            foreach (var operationId in new[] { "different-process", "same-process", "expired" })
            {
                Assert.True(store.TryTransition(operationId, ServerOperationLifecycleStatus.Queued,
                    ServerOperationLifecycleStatus.Running, Utc(1), null));
            }
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(@"UPDATE server_operation_lifecycle
                    SET completion_deadline_utc = @Deadline WHERE operation_id = 'expired';",
                    new { Deadline = Utc(1).ToUnixTimeMilliseconds() });
                connection.Execute(@"UPDATE server_operation_lifecycle
                    SET origin_process_instance_id = 'origin-b' WHERE operation_id = 'different-process';");
            }

            var recovery = new ReconcileServerOperationsUseCase(store, () => Utc(2));
            recovery.ReconcileAfterGameReady("origin-a");

            Assert.Equal(ServerOperationLifecycleStatus.Succeeded, store.Get("different-process")!.Status);
            Assert.Equal(ServerOperationLifecycleStatus.Running, store.Get("same-process")!.Status);
            var expired = store.Get("expired")!;
            Assert.Equal(ServerOperationLifecycleStatus.ResultUnknown, expired.Status);
            Assert.Equal("completion_timeout", expired.FailureCode);
        }

        private static ServerOperationSnapshot Queued(string operationId, DateTimeOffset requestedAtUtc) =>
            new ServerOperationSnapshot(operationId, ServerOperationCodeContract.RestartScript,
                ServerOperationLifecycleStatus.Queued, "owner", "origin-a", requestedAtUtc, null, null,
                requestedAtUtc.AddMinutes(5), null, "recorded");

        private static DateTimeOffset Utc(int seconds) =>
            new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryDatabase()
            {
                directory = Path.Combine(Path.GetTempPath(), "seven-dpanel-operation-tests-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
