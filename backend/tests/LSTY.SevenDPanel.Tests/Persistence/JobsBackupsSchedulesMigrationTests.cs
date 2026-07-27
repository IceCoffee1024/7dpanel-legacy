using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    public sealed class JobsBackupsSchedulesMigrationTests
    {
        private static readonly string[] ExpectedTables =
        {
            "backup_artifacts",
            "backup_policies",
            "job_admin_operations",
            "jobs",
            "panel_database_backup_job_payloads",
            "restore_job_payloads",
            "schedule_runs",
            "scheduled_announcement_job_payloads",
            "scheduled_console_command_job_payloads",
            "scheduled_restart_job_payloads",
            "schedules",
            "server_configuration_backup_job_payloads",
            "world_backup_job_payloads"
        };

        [Fact]
        public void Empty_database_upgrade_creates_fixed_schema_constraints_indexes_and_safe_audit_projection()
        {
            using var database = new TemporaryDatabase();

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
                new[] { "ix_jobs_status_created", "ix_schedules_enabled_next" },
                connection.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'index' AND name IN ('ix_jobs_status_created', 'ix_schedules_enabled_next') ORDER BY name;"));
            foreach (var payloadTable in new[]
                     {
                         "world_backup_job_payloads",
                         "panel_database_backup_job_payloads",
                         "server_configuration_backup_job_payloads",
                         "restore_job_payloads",
                         "scheduled_console_command_job_payloads",
                         "scheduled_restart_job_payloads",
                         "scheduled_announcement_job_payloads"
                     })
            {
                AssertForeignKey(connection, payloadTable, "job_id", "jobs", "CASCADE");
            }
            AssertNoForeignKey(connection, "restore_job_payloads", "backup_id");
            AssertForeignKey(connection, "backup_artifacts", "source_job_id", "jobs", "RESTRICT");
            AssertForeignKey(connection, "schedule_runs", "job_id", "jobs", "SET NULL");

            AssertNoForeignKey(connection, "jobs", "source_schedule_id");
            AssertNoForeignKey(connection, "scheduled_console_command_job_payloads", "schedule_id");
            AssertNoForeignKey(connection, "scheduled_restart_job_payloads", "schedule_id");
            AssertNoForeignKey(connection, "scheduled_announcement_job_payloads", "schedule_id");
            AssertNoForeignKey(connection, "schedule_runs", "schedule_id");

            Assert.Equal(
                new[]
                {
                    "validate_schedule_run_schedule",
                    "validate_scheduled_announcement_payload_kind",
                    "validate_scheduled_console_command_payload_kind",
                    "validate_scheduled_job_schedule",
                    "validate_scheduled_restart_payload_kind"
                },
                connection.Query<string>(
                    @"SELECT name
                      FROM sqlite_master
                      WHERE type = 'trigger' AND name IN (
                          'validate_schedule_run_schedule',
                          'validate_scheduled_announcement_payload_kind',
                          'validate_scheduled_console_command_payload_kind',
                          'validate_scheduled_job_schedule',
                          'validate_scheduled_restart_payload_kind')
                      ORDER BY name;"));
            AssertScheduleValidationTriggers(connection);

            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO jobs (id, kind, status, idempotency_key, created_at_utc, row_version) VALUES ('bad-kind', 'Arbitrary', 'Queued', 'bad-kind', 1, 0);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO jobs (id, kind, status, idempotency_key, created_at_utc, row_version) VALUES ('bad-status', 'WorldBackup', 'Unknown', 'bad-status', 1, 0);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO world_backup_job_payloads (job_id, world_name) VALUES ('missing-job', 'Navezgane');"));

            var viewSql = connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            Assert.Contains("jobs", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("schedule_runs", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("job_admin_operations", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                new[]
                {
                    "backup_root_id", "relative_resource_id", "world_name", "command_text",
                    "message_text", "reason", "raw_command", "console_command_audit_output"
                },
                term => viewSql.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Upgrade_from_008_preserves_first_wave_rows_and_adds_only_stable_job_audit_summaries()
        {
            using var database = new TemporaryDatabase();
            UpgradeThrough008(database.ConnectionFactory);

            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    @"INSERT INTO game_events (
                          event_id, event_type, occurred_utc, observed_utc)
                      VALUES ('event-before-009', 'PlayerJoined', 1785024000000, 1785024000000);
                      INSERT INTO chat_mute (
                          crossplatform_id, reason, created_by, created_utc, updated_by, updated_utc)
                      VALUES ('EOS-before-009', 'preserve', 'owner', 1785024000000, 'owner', 1785024000000);"
                );
            }

            database.Upgrade();

            using var upgraded = database.ConnectionFactory.Open();
            Assert.Equal(1, upgraded.ExecuteScalar<int>("SELECT COUNT(*) FROM game_events WHERE event_id = 'event-before-009';"));
            Assert.Equal(1, upgraded.ExecuteScalar<int>("SELECT COUNT(*) FROM chat_mute WHERE crossplatform_id = 'EOS-before-009';"));

            var scheduleId = Guid.NewGuid().ToString("D");
            var jobId = Guid.NewGuid().ToString("D");
            var runId = Guid.NewGuid().ToString("D");
            var operationId = Guid.NewGuid().ToString("D");
            upgraded.Execute(
                @"INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, message_text, next_occurrence_utc, row_version)
                  VALUES (@ScheduleId, 'ScheduledAnnouncement', 'safe name', '* * * * *', 'UTC', 1,
                      'QueueOne', 'stored schedule message', 1785024060000, 0);
                  INSERT INTO jobs (
                      id, kind, status, actor_subject, source_schedule_id, idempotency_key,
                      correlation_id, created_at_utc, started_at_utc, completed_at_utc, row_version)
                  VALUES (@JobId, 'ScheduledAnnouncement', 'Succeeded', 'owner', @ScheduleId,
                      'schedule-safe', 'corr-safe', 1785024000000, 1785024000001, 1785024000002, 2);
                  INSERT INTO scheduled_announcement_job_payloads (job_id, schedule_id, message_text)
                  VALUES (@JobId, @ScheduleId, 'Secret announcement body /server/path');
                  INSERT INTO schedule_runs (id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                  VALUES (@RunId, @ScheduleId, 1785024000000, @JobId, 'Succeeded', 1785024000000);
                  INSERT INTO job_admin_operations (
                      id, actor_subject, action, target_kind, target_id, status, occurred_utc, correlation_id)
                  VALUES (@OperationId, 'owner', 'backup.delete', 'backup', @JobId, 'Succeeded', 1785024000003, 'corr-admin');",
                new { ScheduleId = scheduleId, JobId = jobId, RunId = runId, OperationId = operationId });

            var summaries = upgraded.Query<AuditProjectionRow>(
                @"SELECT source_kind, source_id, action, target_ref, status
                  FROM unified_audit_projection
                  WHERE source_id IN (@JobSource, @RunSource, @OperationSource)
                  ORDER BY source_id;",
                new
                {
                    JobSource = "job:" + jobId,
                    RunSource = "scheduleRun:" + runId,
                    OperationSource = "jobAdminOperation:" + operationId
                }).ToArray();
            Assert.Equal(3, summaries.Length);
            Assert.All(summaries, row => Assert.Equal("serverOperation", row.source_kind));
            var rendered = string.Join("|", summaries.SelectMany(row => new[] { row.source_id, row.action, row.target_ref, row.status }));
            Assert.DoesNotContain("Secret announcement body", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/server/path", rendered, StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertScheduleValidationTriggers(SqliteConnection connection)
        {
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, row_version)
                  VALUES ('missing-schedule-job', 'ScheduledAnnouncement', 'Queued',
                      'missing-schedule', 'missing-schedule-job', 1, 0);"));

            connection.Execute(
                @"INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, command_text, next_occurrence_utc, row_version)
                  VALUES ('command-schedule', 'ScheduledConsoleCommand', 'command', '* * * * *',
                      'UTC', 1, 'QueueOne', 'say test', 10, 0);
                  INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, countdown_seconds, next_occurrence_utc, row_version)
                  VALUES ('restart-schedule', 'ScheduledRestart', 'restart', '* * * * *',
                      'UTC', 1, 'QueueOne', 60, 10, 0);
                  INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, message_text, next_occurrence_utc, row_version)
                  VALUES ('announcement-schedule', 'ScheduledAnnouncement', 'announcement', '* * * * *',
                      'UTC', 1, 'QueueOne', 'test announcement', 10, 0);"
            );

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, row_version)
                  VALUES ('mismatched-schedule-job', 'ScheduledRestart', 'Queued',
                      'announcement-schedule', 'mismatched-schedule-job', 1, 0);"));

            connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, started_at_utc, completed_at_utc, row_version)
                  VALUES ('command-job', 'ScheduledConsoleCommand', 'Succeeded',
                      'command-schedule', 'command-job', 1, 2, 3, 2);
                  INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, started_at_utc, completed_at_utc, row_version)
                  VALUES ('restart-job', 'ScheduledRestart', 'Succeeded',
                      'restart-schedule', 'restart-job', 1, 2, 3, 2);
                  INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, started_at_utc, completed_at_utc, row_version)
                  VALUES ('announcement-job', 'ScheduledAnnouncement', 'Succeeded',
                      'announcement-schedule', 'announcement-job', 1, 2, 3, 2);"
            );

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_console_command_job_payloads (
                      job_id, schedule_id, command_text)
                  VALUES ('restart-job', 'restart-schedule', 'say invalid');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_restart_job_payloads (
                      job_id, schedule_id, countdown_seconds)
                  VALUES ('announcement-job', 'announcement-schedule', 60);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_announcement_job_payloads (
                      job_id, schedule_id, message_text)
                  VALUES ('command-job', 'command-schedule', 'invalid');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO schedule_runs (
                      id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                  VALUES ('missing-schedule-run', 'missing-schedule', 10, NULL, 'Skipped', 10);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO schedule_runs (
                      id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                  VALUES ('mismatched-schedule-run', 'restart-schedule', 10,
                      'command-job', 'Queued', 10);"));

            connection.Execute("DELETE FROM schedules;");

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_console_command_job_payloads (
                      job_id, schedule_id, command_text)
                  VALUES ('command-job', 'command-schedule', 'say history');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_restart_job_payloads (
                      job_id, schedule_id, countdown_seconds)
                  VALUES ('restart-job', 'restart-schedule', 60);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO scheduled_announcement_job_payloads (
                      job_id, schedule_id, message_text)
                  VALUES ('announcement-job', 'announcement-schedule', 'history');"));
        }

        private static void AssertForeignKey(
            SqliteConnection connection,
            string table,
            string column,
            string referencedTable,
            string onDelete) =>
            Assert.Contains(
                connection.Query<ForeignKeyRow>("PRAGMA foreign_key_list(" + table + ");"),
                key => key.table == referencedTable && key.from == column && key.on_delete == onDelete);

        private static void AssertNoForeignKey(
            SqliteConnection connection,
            string table,
            string column) =>
            Assert.DoesNotContain(
                connection.Query<ForeignKeyRow>("PRAGMA foreign_key_list(" + table + ");"),
                key => key.from == column);

        private static void UpgradeThrough008(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName =>
                        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, 8).Any(
                            version => resourceName.IndexOf(
                                $".Migrations.{version:D3}_",
                                StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        private sealed class ForeignKeyRow
        {
            public string table { get; set; } = string.Empty;
            public string from { get; set; } = string.Empty;
            public string on_delete { get; set; } = string.Empty;
        }

        private sealed class AuditProjectionRow
        {
            public string source_kind { get; set; } = string.Empty;
            public string source_id { get; set; } = string.Empty;
            public string action { get; set; } = string.Empty;
            public string? target_ref { get; set; }
            public string status { get; set; } = string.Empty;
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-jobs-migration-tests", Guid.NewGuid().ToString("N"));

            public TemporaryDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }

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
