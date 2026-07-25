using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteConsoleCommandAuditStoreTests
    {
        [Fact]
        public void Upgrade_creates_console_command_audit_schema_and_can_be_repeated()
        {
            using var database = new TemporaryCommandAuditDatabase();
            var bootstrapper = new SqliteDatabaseBootstrapper(database.ConnectionFactory);

            bootstrapper.Upgrade();
            bootstrapper.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                4,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'console_command_audit%';"));
            Assert.Equal(4, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaVersions;"));
        }

        [Fact]
        public void Append_preserves_raw_command_arguments_output_and_source()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            const string rawCommand = "say \"Hello  world\" \"密钥=原文\"";
            var startedAtUtc = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
            var completedAtUtc = startedAtUtc.AddMilliseconds(25);

            store.Append(new ConsoleCommandAuditEntry(
                "audit-1",
                rawCommand,
                new[] { "say", "Hello  world", "密钥=原文" },
                new[] { "line 1", "line 2" },
                "7dpanel-http",
                "owner",
                startedAtUtc,
                completedAtUtc,
                ConsoleCommandCompletionKind.Completed,
                null));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<AuditRow>(
                "SELECT * FROM console_command_audit WHERE audit_id = 'audit-1';");
            var arguments = connection.Query<string>(
                "SELECT value FROM console_command_audit_argument WHERE audit_id = 'audit-1' ORDER BY ordinal;").ToArray();
            var output = connection.Query<string>(
                "SELECT value FROM console_command_audit_output WHERE audit_id = 'audit-1' ORDER BY ordinal;").ToArray();

            Assert.Equal(rawCommand, row.raw_command);
            Assert.Equal("say", row.command_name);
            Assert.Equal("7dpanel-http", row.source);
            Assert.Equal("owner", row.actor_subject);
            Assert.Equal(startedAtUtc.ToUnixTimeMilliseconds(), row.started_utc);
            Assert.Equal(completedAtUtc.ToUnixTimeMilliseconds(), row.completed_utc);
            Assert.Equal("Completed", row.completion_kind);
            Assert.Null(row.exception_type);
            Assert.Equal(new[] { "Hello  world", "密钥=原文" }, arguments);
            Assert.Equal(new[] { "line 1", "line 2" }, output);
        }

        [Fact]
        public void AppendGap_is_idempotent()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            var startedAtUtc = new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero);
            var gap = new ConsoleCommandAuditGap(
                "gap-1",
                startedAtUtc,
                startedAtUtc.AddSeconds(2),
                3,
                "queue_full");

            store.AppendGap(gap);
            store.AppendGap(gap);

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM console_command_audit_gap WHERE gap_id = 'gap-1';"));
        }

        [Fact]
        public async Task Separate_store_instances_append_concurrently_without_mixing_records()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();

            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            {
                var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
                store.Append(Entry("audit-" + index, "command-" + index));
            })));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(8, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM console_command_audit;"));
            Assert.Equal(8, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM console_command_audit_argument;"));
            Assert.Equal(8, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM console_command_audit_output;"));
        }

        [Fact]
        public void Duplicate_audit_id_fails_without_changing_the_existing_record()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            store.Append(Entry("duplicate", "first"));

            Assert.Throws<SqliteException>(() => store.Append(Entry("duplicate", "second")));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal("first", connection.ExecuteScalar<string>(
                "SELECT raw_command FROM console_command_audit WHERE audit_id = 'duplicate';"));
            Assert.Equal("first-argument", connection.ExecuteScalar<string>(
                "SELECT value FROM console_command_audit_argument WHERE audit_id = 'duplicate';"));
            Assert.Equal("first-output", connection.ExecuteScalar<string>(
                "SELECT value FROM console_command_audit_output WHERE audit_id = 'duplicate';"));
        }

        [Fact]
        public void Locked_database_failure_releases_resources_and_the_next_append_succeeds()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            using (var lockConnection = database.ConnectionFactory.Open())
            using (var lockTransaction = lockConnection.BeginTransaction(deferred: false))
            {
                Assert.Throws<SqliteException>(() => store.Append(Entry("locked", "locked")));
            }

            store.Append(Entry("recovered", "recovered"));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM console_command_audit WHERE audit_id = 'locked';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM console_command_audit WHERE audit_id = 'recovered';"));
        }

        [Fact]
        public void Child_insert_failure_rolls_back_the_main_audit_record()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var invalidEntry = new ConsoleCommandAuditEntry(
                "rollback",
                "command",
                new[] { "command", null! },
                Array.Empty<string>(),
                "local-game",
                null,
                startedAtUtc,
                startedAtUtc,
                ConsoleCommandCompletionKind.Completed,
                null);

            Assert.Throws<SqliteException>(() => store.Append(invalidEntry));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM console_command_audit WHERE audit_id = 'rollback';"));
        }

        [Fact]
        public void Thrown_entry_preserves_exception_type_and_completion_constraint()
        {
            using var database = new TemporaryCommandAuditDatabase();
            database.Upgrade();
            var store = new SqliteConsoleCommandAuditStore(database.ConnectionFactory);
            var startedAtUtc = DateTimeOffset.UtcNow;

            store.Append(new ConsoleCommandAuditEntry(
                "threw",
                "failing-command",
                new[] { "failing-command" },
                Array.Empty<string>(),
                "network",
                null,
                startedAtUtc,
                startedAtUtc.AddMilliseconds(1),
                ConsoleCommandCompletionKind.Threw,
                typeof(InvalidOperationException).FullName));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<AuditRow>(
                "SELECT * FROM console_command_audit WHERE audit_id = 'threw';");
            Assert.Equal("Threw", row.completion_kind);
            Assert.Equal(typeof(InvalidOperationException).FullName, row.exception_type);
        }

        private static ConsoleCommandAuditEntry Entry(string auditId, string command)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            return new ConsoleCommandAuditEntry(
                auditId,
                command,
                new[] { command, command + "-argument" },
                new[] { command + "-output" },
                "local-game",
                null,
                startedAtUtc,
                startedAtUtc.AddMilliseconds(1),
                ConsoleCommandCompletionKind.Completed,
                null);
        }

        private sealed class AuditRow
        {
            public string raw_command { get; set; } = string.Empty;
            public string? command_name { get; set; }
            public string source { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public long started_utc { get; set; }
            public long completed_utc { get; set; }
            public string completion_kind { get; set; } = string.Empty;
            public string? exception_type { get; set; }
        }

        private sealed class TemporaryCommandAuditDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryCommandAuditDatabase()
            {
                directory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-console-command-audit-tests",
                    Guid.NewGuid().ToString("N"));
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

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
