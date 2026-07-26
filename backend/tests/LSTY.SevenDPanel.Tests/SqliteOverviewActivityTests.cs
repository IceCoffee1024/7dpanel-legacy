using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteOverviewActivityTests
    {
        [Fact]
        public void Upgrade_from_version_three_applies_overview_migration_and_can_be_repeated()
        {
            using var database = new TemporaryOverviewDatabase();
            database.CreateVersionThree();
            var bootstrapper = new SqliteDatabaseBootstrapper(database.ConnectionFactory);

            using (var versionThreeConnection = database.ConnectionFactory.Open())
            {
                Assert.Equal(3, versionThreeConnection.ExecuteScalar<int>("SELECT COUNT(*) FROM SchemaVersions;"));
                Assert.Equal(0, versionThreeConnection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'recent_activity';"));
            }

            bootstrapper.Upgrade();
            bootstrapper.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName LIKE '%Migrations.005_OverviewActivityAndServerOperations.sql';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'recent_activity';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'server_operation_audit';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_recent_activity_occurred_utc';"));
            var activityColumns = connection.Query<string>(
                "SELECT name FROM pragma_table_info('recent_activity');").ToArray();
            var auditColumns = connection.Query<string>(
                "SELECT name FROM pragma_table_info('server_operation_audit');").ToArray();
            Assert.DoesNotContain("password", activityColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", activityColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("api_key", activityColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("ip", activityColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("script_path", auditColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("command", auditColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("output", auditColumns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", auditColumns, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Recent_activity_schema_rejects_invalid_message_arguments_json()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO recent_activity (
                      event_type, message_key, message_args, actor_subject, actor_display_name, occurred_utc)
                  VALUES (
                      'player_joined', 'player_joined', '{invalid', NULL, NULL, @OccurredUtc);",
                new { OccurredUtc = ToUtcText(new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero)) }));
        }

        [Fact]
        public async Task Recent_activity_is_returned_newest_first_with_at_most_eight_items_and_a_read_timestamp()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(database.ConnectionFactory, retentionLimit: 32);
            var first = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

            for (var index = 0; index < 9; index++)
            {
                await store.RecordPlayerJoinedAsync(
                    "Player " + index,
                    first.AddMinutes(index),
                    CancellationToken.None);
            }

            var beforeRead = DateTimeOffset.UtcNow;
            var result = await store.GetRecentActivityAsync(CancellationToken.None);
            var afterRead = DateTimeOffset.UtcNow;

            Assert.Equal(AvailabilityState.Available, result.Availability);
            Assert.Equal(8, result.Items.Count);
            Assert.Equal(9, result.TotalCount);
            Assert.Equal(first.AddMinutes(8), result.LatestOccurredAtUtc);
            Assert.Equal("player_joined", result.Items[0].MessageKey);
            Assert.Equal(first.AddMinutes(8), result.Items[0].OccurredAtUtc);
            Assert.Equal(first.AddMinutes(1), result.Items[7].OccurredAtUtc);
            Assert.NotNull(result.SampledAtUtc);
            Assert.InRange(result.SampledAtUtc!.Value, beforeRead, afterRead);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(9, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recent_activity;"));
        }

        [Fact]
        public async Task Recent_activity_retention_keeps_the_newest_configured_records()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(database.ConnectionFactory, retentionLimit: 3);
            var start = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);

            for (var index = 0; index < 4; index++)
            {
                await store.RecordPlayerLeftAsync(
                    "Player " + index,
                    start.AddMinutes(index),
                    CancellationToken.None);
            }

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recent_activity;"));
            Assert.Equal(ToUtcText(start.AddMinutes(1)), connection.ExecuteScalar<string>(
                "SELECT MIN(occurred_utc) FROM recent_activity;"));
        }

        [Fact]
        public async Task Player_activity_round_trips_only_the_controlled_display_name_argument()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(database.ConnectionFactory, retentionLimit: 8);
            var occurredAtUtc = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);
            const string joinedDisplayName = "玩家 \"一号\"\n\t🎮";
            const string leftDisplayName = "Игрок\r\nTwo";

            await store.RecordPlayerJoinedAsync(
                joinedDisplayName,
                occurredAtUtc,
                CancellationToken.None);
            await store.RecordPlayerLeftAsync(
                leftDisplayName,
                occurredAtUtc.AddMinutes(1),
                CancellationToken.None);

            var result = await store.GetRecentActivityAsync(CancellationToken.None);

            Assert.Equal("player_left", result.Items[0].MessageKey);
            Assert.Equal(leftDisplayName, Assert.Single(result.Items[0].MessageArguments).Value);
            Assert.Equal("player_joined", result.Items[1].MessageKey);
            Assert.Equal(joinedDisplayName, result.Items[1].MessageArguments["displayName"]);
            Assert.All(result.Items, item => Assert.Equal("displayName", Assert.Single(item.MessageArguments).Key));
            Assert.Throws<NotSupportedException>(() =>
                ((IDictionary<string, string>)result.Items[0].MessageArguments).Add("ip", "192.0.2.1"));

            using var connection = database.ConnectionFactory.Open();
            var rows = connection.Query<ActivityStorageRow>(
                @"SELECT event_type, message_key, message_args, actor_subject, actor_display_name, occurred_utc
                  FROM recent_activity
                  ORDER BY occurred_utc;").ToArray();
            Assert.All(rows, row => Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT json_valid(@MessageArguments);",
                new { MessageArguments = row.message_args })));
            Assert.Equal(new[] { joinedDisplayName, leftDisplayName }, connection.Query<string>(
                "SELECT json_extract(message_args, '$.displayName') FROM recent_activity ORDER BY occurred_utc;"));
            Assert.All(rows, row => Assert.Null(row.actor_subject));
            Assert.All(rows, row => Assert.Null(row.actor_display_name));
            Assert.DoesNotContain("ip", string.Join(" ", rows.Select(row => row.message_args)), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Server_operation_audit_records_pending_started_and_failed_states_without_unsafe_fields()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var audit = new SqliteServerOperationAuditTrail(database.ConnectionFactory);
            var requestedAtUtc = new DateTimeOffset(2026, 7, 25, 1, 0, 0, TimeSpan.Zero);

            audit.CreateRestartPending("restart-started", "owner", requestedAtUtc);
            Assert.True(audit.TryMarkStarted(
                "restart-started",
                requestedAtUtc.AddSeconds(1)));
            audit.CreateRestartPending("restart-1", "owner", requestedAtUtc);
            Assert.True(audit.TryMarkFailed(
                "restart-1",
                requestedAtUtc.AddSeconds(2),
                "restart_script_start_failed"));

            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<ServerOperationAuditRow>(
                "SELECT * FROM server_operation_audit WHERE operation_id = 'restart-1';");
            Assert.Equal("restart", row.operation_type);
            Assert.Equal("owner", row.actor_subject);
            Assert.Equal("Failed", row.status);
            Assert.Equal("restart_script_start_failed", row.failure_code);
            Assert.Equal(ToUtcText(requestedAtUtc), row.requested_utc);
            Assert.Equal(ToUtcText(requestedAtUtc.AddSeconds(2)), row.updated_utc);
        }

        [Fact]
        public void Started_server_operation_audit_is_terminal()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var audit = new SqliteServerOperationAuditTrail(database.ConnectionFactory);
            var requestedAtUtc = new DateTimeOffset(2026, 7, 25, 1, 0, 0, TimeSpan.Zero);
            audit.CreateRestartPending("restart-started", "owner", requestedAtUtc);
            Assert.True(audit.TryMarkStarted(
                "restart-started",
                requestedAtUtc.AddSeconds(1)));

            var changed = audit.TryMarkFailed(
                "restart-started",
                requestedAtUtc.AddSeconds(2),
                "restart_script_start_failed");

            Assert.False(changed);
            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<ServerOperationAuditRow>(
                "SELECT * FROM server_operation_audit WHERE operation_id = 'restart-started';");
            Assert.Equal("Started", row.status);
            Assert.Null(row.failure_code);
            Assert.Equal(ToUtcText(requestedAtUtc.AddSeconds(1)), row.updated_utc);
        }

        [Fact]
        public void Failed_server_operation_audit_is_terminal()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var audit = new SqliteServerOperationAuditTrail(database.ConnectionFactory);
            var requestedAtUtc = new DateTimeOffset(2026, 7, 25, 1, 0, 0, TimeSpan.Zero);
            audit.CreateRestartPending("restart-failed", "owner", requestedAtUtc);
            Assert.True(audit.TryMarkFailed(
                "restart-failed",
                requestedAtUtc.AddSeconds(1),
                "restart_script_start_failed"));

            var changed = audit.TryMarkStarted(
                "restart-failed",
                requestedAtUtc.AddSeconds(2));

            Assert.False(changed);
            using var connection = database.ConnectionFactory.Open();
            var row = connection.QuerySingle<ServerOperationAuditRow>(
                "SELECT * FROM server_operation_audit WHERE operation_id = 'restart-failed';");
            Assert.Equal("Failed", row.status);
            Assert.Equal("restart_script_start_failed", row.failure_code);
            Assert.Equal(ToUtcText(requestedAtUtc.AddSeconds(1)), row.updated_utc);
        }

        [Fact]
        public async Task Recent_activity_writer_records_only_fixed_safe_activity_shapes()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(database.ConnectionFactory, retentionLimit: 8);
            var occurredAtUtc = new DateTimeOffset(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

            Assert.IsAssignableFrom<IRecentActivityQuery>(store);
            Assert.IsAssignableFrom<IRecentActivityWriter>(store);
            await store.RecordPanelLoginSucceededAsync("owner-1", "Owner", occurredAtUtc, CancellationToken.None);
            await store.RecordPlayerJoinedAsync("Player One", occurredAtUtc.AddMinutes(1), CancellationToken.None);
            await store.RecordPlayerLeftAsync("Player One", occurredAtUtc.AddMinutes(2), CancellationToken.None);
            await store.RecordRestartScriptStartedAsync("owner-1", occurredAtUtc.AddMinutes(3), CancellationToken.None);
            await store.RecordShutdownRequestedAsync("owner-1", occurredAtUtc.AddMinutes(4), CancellationToken.None);
            await store.RecordServerOperationFailedAsync(
                "owner-1",
                "restart_script",
                "restart_script_start_failed",
                occurredAtUtc.AddMinutes(5),
                CancellationToken.None);

            using var connection = database.ConnectionFactory.Open();
            var rows = connection.Query<ActivityStorageRow>(
                "SELECT event_type, message_key, message_args, occurred_utc FROM recent_activity ORDER BY occurred_utc;").ToArray();
            Assert.Equal(new[]
            {
                "panel_login_succeeded",
                "player_joined",
                "player_left",
                "restart_script_started",
                "shutdown_requested",
                "server_operation_failed"
            }, rows.Select(row => row.event_type));
            Assert.All(rows, row => Assert.Equal(row.event_type, row.message_key));
            Assert.Equal("{}", rows[0].message_args);
            Assert.Equal("{\"displayName\":\"Player One\"}", rows[1].message_args);
            Assert.Equal("{\"displayName\":\"Player One\"}", rows[2].message_args);
            Assert.All(rows.Skip(3).Take(2), row => Assert.Equal("{}", row.message_args));
            Assert.Equal("{\"failureCode\":\"restart_script_start_failed\",\"operationCode\":\"restart_script\"}", rows[5].message_args);
            Assert.DoesNotContain("Owner", string.Join(" ", rows.Select(row => row.message_args)), StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("restart_script", "restart_script_not_configured")]
        [InlineData("restart_script", "restart_script_missing")]
        [InlineData("restart_script", "restart_script_platform_unsupported")]
        [InlineData("restart_script", "restart_script_start_failed")]
        [InlineData("shutdown", "shutdown_unavailable")]
        [InlineData("shutdown", "shutdown_timeout")]
        [InlineData("shutdown", "shutdown_cancelled")]
        [InlineData("shutdown", "shutdown_failed")]
        public async Task Recent_activity_writer_accepts_only_each_fixed_server_operation_failure(
            string operationCode,
            string failureCode)
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(
                database.ConnectionFactory,
                retentionLimit: 8);

            await store.RecordServerOperationFailedAsync(
                "owner-1",
                operationCode,
                failureCode,
                new DateTimeOffset(2026, 7, 25, 2, 0, 0, TimeSpan.Zero),
                CancellationToken.None);

            var result = await store.GetRecentActivityAsync(CancellationToken.None);
            var item = Assert.Single(result.Items);
            Assert.Equal(operationCode, item.MessageArguments["operationCode"]);
            Assert.Equal(failureCode, item.MessageArguments["failureCode"]);
        }

        [Fact]
        public async Task Recent_activity_writer_rejects_operation_or_failure_codes_outside_its_allowlist()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var store = new SqliteRecentActivityStore(database.ConnectionFactory, retentionLimit: 8);
            var occurredAtUtc = new DateTimeOffset(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

            await Assert.ThrowsAsync<ArgumentException>(() => store.RecordServerOperationFailedAsync(
                "owner-1",
                "restart; drop table recent_activity",
                "restart_script_start_failed",
                occurredAtUtc,
                CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.RecordServerOperationFailedAsync(
                "owner-1",
                "restart_script",
                "token=secret",
                occurredAtUtc,
                CancellationToken.None));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM recent_activity;"));
        }

        [Fact]
        public void Server_operation_audit_rejects_unsafe_failure_details()
        {
            using var database = new TemporaryOverviewDatabase();
            database.Upgrade();
            var audit = new SqliteServerOperationAuditTrail(database.ConnectionFactory);
            var requestedAtUtc = new DateTimeOffset(2026, 7, 25, 3, 0, 0, TimeSpan.Zero);
            audit.CreateRestartPending("restart-unsafe", "owner", requestedAtUtc);

            Assert.Throws<ArgumentException>(() => audit.TryMarkFailed(
                "restart-unsafe",
                requestedAtUtc.AddSeconds(1),
                "Exception: C:\\scripts\\restart.ps1 token=secret output=details"));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal("Pending", connection.ExecuteScalar<string>(
                "SELECT status FROM server_operation_audit WHERE operation_id = 'restart-unsafe';"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM server_operation_audit WHERE failure_code LIKE '%secret%' OR failure_code LIKE '%scripts%';"));
        }

        private static string ToUtcText(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        private sealed class ActivityStorageRow
        {
            public string event_type { get; set; } = string.Empty;
            public string message_key { get; set; } = string.Empty;
            public string message_args { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public string? actor_display_name { get; set; }
            public string occurred_utc { get; set; } = string.Empty;
        }

        private sealed class ServerOperationAuditRow
        {
            public string operation_type { get; set; } = string.Empty;
            public string actor_subject { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string requested_utc { get; set; } = string.Empty;
            public string updated_utc { get; set; } = string.Empty;
            public string? failure_code { get; set; }
        }

        private sealed class TemporaryOverviewDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryOverviewDatabase()
            {
                directory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-overview-activity-tests",
                    Guid.NewGuid().ToString("N"));
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void CreateVersionThree()
            {
                Directory.CreateDirectory(directory);
                var assembly = typeof(SqliteDatabaseBootstrapper).Assembly;
                var migrationNames = assembly.GetManifestResourceNames()
                    .Where(name => name.IndexOf(".Migrations.00", StringComparison.Ordinal) >= 0)
                    .Where(name =>
                        name.EndsWith("001_Authentication.sql", StringComparison.Ordinal) ||
                        name.EndsWith("002_PlayerActionAudit.sql", StringComparison.Ordinal) ||
                        name.EndsWith("003_ConsoleCommandAudit.sql", StringComparison.Ordinal))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(3, migrationNames.Length);

                using var connection = ConnectionFactory.Open();
                connection.Execute(
                    @"CREATE TABLE SchemaVersions (
                          Id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
                          ScriptName TEXT NOT NULL,
                          Applied TEXT NOT NULL);"
                );
                foreach (var migrationName in migrationNames)
                {
                    using var stream = assembly.GetManifestResourceStream(migrationName);
                    Assert.NotNull(stream);
                    using var reader = new StreamReader(stream!);
                    connection.Execute(reader.ReadToEnd());
                    connection.Execute(
                        "INSERT INTO SchemaVersions (ScriptName, Applied) VALUES (@ScriptName, @Applied);",
                        new
                        {
                            ScriptName = migrationName,
                            Applied = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                        });
                }
            }

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
