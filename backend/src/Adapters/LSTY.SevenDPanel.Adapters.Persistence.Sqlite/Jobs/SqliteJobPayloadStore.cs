using System;
using System.Collections.Generic;
using System.Data;
using Dapper;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs
{
    public sealed class SqliteJobPayloadStore : IJobSubmissionStore, IJobPayloadReader
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteJobPayloadStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public JobRecord Enqueue(NewJob job, WorldBackupPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var worldName = RequireText(payload.WorldName, nameof(payload));
            return Enqueue(job, JobKind.WorldBackup, (connection, transaction, jobId) =>
                connection.Execute(
                    "INSERT OR IGNORE INTO world_backup_job_payloads (job_id, world_name) VALUES (@JobId, @WorldName);",
                    new { JobId = jobId, WorldName = worldName }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM world_backup_job_payloads WHERE job_id = @JobId AND world_name = @WorldName;",
                    new { JobId = jobId, WorldName = worldName }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Enqueue(job, JobKind.PanelDatabaseBackup, (connection, transaction, jobId) =>
                connection.Execute(
                    "INSERT OR IGNORE INTO panel_database_backup_job_payloads (job_id) VALUES (@JobId);",
                    new { JobId = jobId }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM panel_database_backup_job_payloads WHERE job_id = @JobId;",
                    new { JobId = jobId }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Enqueue(job, JobKind.ServerConfigurationBackup, (connection, transaction, jobId) =>
                connection.Execute(
                    "INSERT OR IGNORE INTO server_configuration_backup_job_payloads (job_id) VALUES (@JobId);",
                    new { JobId = jobId }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM server_configuration_backup_job_payloads WHERE job_id = @JobId;",
                    new { JobId = jobId }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, RestorePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return Enqueue(job, JobKind.Restore, (connection, transaction, jobId) =>
                connection.Execute(
                    @"INSERT OR IGNORE INTO restore_job_payloads (
                          job_id, backup_id, backup_kind, restart_after_stage)
                      VALUES (@JobId, @BackupId, @BackupKind, @RestartAfterStage);",
                    new
                    {
                        JobId = jobId,
                        BackupId = payload.BackupId.ToString("D"),
                        BackupKind = payload.BackupKind.ToString(),
                        RestartAfterStage = payload.RestartAfterStage ? 1 : 0
                    }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM restore_job_payloads
                      WHERE job_id = @JobId AND backup_id = @BackupId
                        AND backup_kind = @BackupKind AND restart_after_stage = @RestartAfterStage;",
                    new
                    {
                        JobId = jobId,
                        BackupId = payload.BackupId.ToString("D"),
                        BackupKind = payload.BackupKind.ToString(),
                        RestartAfterStage = payload.RestartAfterStage ? 1 : 0
                    }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var command = RequireText(payload.CommandText, nameof(payload));
            return Enqueue(job, JobKind.ScheduledConsoleCommand, (connection, transaction, jobId) =>
                connection.Execute(
                    @"INSERT OR IGNORE INTO scheduled_console_command_job_payloads (
                          job_id, schedule_id, command_text)
                      VALUES (@JobId, @ScheduleId, @CommandText);",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), CommandText = command }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM scheduled_console_command_job_payloads
                      WHERE job_id = @JobId AND schedule_id = @ScheduleId AND command_text = @CommandText;",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), CommandText = command }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.CountdownSeconds < 0 || payload.CountdownSeconds > 86400)
                throw new ArgumentOutOfRangeException(nameof(payload));
            return Enqueue(job, JobKind.ScheduledRestart, (connection, transaction, jobId) =>
                connection.Execute(
                    @"INSERT OR IGNORE INTO scheduled_restart_job_payloads (
                          job_id, schedule_id, countdown_seconds)
                      VALUES (@JobId, @ScheduleId, @CountdownSeconds);",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), payload.CountdownSeconds }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM scheduled_restart_job_payloads
                      WHERE job_id = @JobId AND schedule_id = @ScheduleId
                        AND countdown_seconds = @CountdownSeconds;",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), payload.CountdownSeconds }, transaction) == 1);
        }

        public JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrEmpty(payload.MessageText) || payload.MessageText.Length > 500)
                throw new ArgumentOutOfRangeException(nameof(payload));
            return Enqueue(job, JobKind.ScheduledAnnouncement, (connection, transaction, jobId) =>
                connection.Execute(
                    @"INSERT OR IGNORE INTO scheduled_announcement_job_payloads (
                          job_id, schedule_id, message_text)
                      VALUES (@JobId, @ScheduleId, @MessageText);",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), payload.MessageText }, transaction),
                (connection, transaction, jobId) => connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*) FROM scheduled_announcement_job_payloads
                      WHERE job_id = @JobId AND schedule_id = @ScheduleId AND message_text = @MessageText;",
                    new { JobId = jobId, ScheduleId = payload.ScheduleId.ToString("D"), payload.MessageText }, transaction) == 1);
        }

        public WorldBackupPayload GetWorldBackup(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var value = connection.QuerySingleOrDefault<string?>(
                "SELECT world_name FROM world_backup_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            return value == null
                ? throw PayloadNotFound(jobId)
                : new WorldBackupPayload(value);
        }

        public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM panel_database_backup_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") }) == 1
                ? new PanelDatabaseBackupPayload()
                : throw PayloadNotFound(jobId);
        }

        public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM server_configuration_backup_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") }) == 1
                ? new ServerConfigurationBackupPayload()
                : throw PayloadNotFound(jobId);
        }

        public RestorePayload GetRestore(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<RestorePayloadRow>(
                @"SELECT backup_id AS BackupId, backup_kind AS BackupKind,
                         restart_after_stage AS RestartAfterStage
                  FROM restore_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            return row == null
                ? throw PayloadNotFound(jobId)
                : new RestorePayload(
                    Guid.Parse(row.BackupId),
                    (BackupKind)Enum.Parse(typeof(BackupKind), row.BackupKind),
                    row.RestartAfterStage != 0);
        }

        public ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ScheduledTextPayloadRow>(
                @"SELECT schedule_id AS ScheduleId, command_text AS Text
                  FROM scheduled_console_command_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            return row == null
                ? throw PayloadNotFound(jobId)
                : new ScheduledConsoleCommandPayload(Guid.Parse(row.ScheduleId), row.Text);
        }

        public ScheduledRestartPayload GetScheduledRestart(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ScheduledRestartPayloadRow>(
                @"SELECT schedule_id AS ScheduleId, countdown_seconds AS CountdownSeconds
                  FROM scheduled_restart_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            return row == null
                ? throw PayloadNotFound(jobId)
                : new ScheduledRestartPayload(Guid.Parse(row.ScheduleId), row.CountdownSeconds);
        }

        public ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ScheduledTextPayloadRow>(
                @"SELECT schedule_id AS ScheduleId, message_text AS Text
                  FROM scheduled_announcement_job_payloads WHERE job_id = @JobId;",
                new { JobId = jobId.ToString("D") });
            return row == null
                ? throw PayloadNotFound(jobId)
                : new ScheduledAnnouncementPayload(Guid.Parse(row.ScheduleId), row.Text);
        }

        private JobRecord Enqueue(
            NewJob job,
            JobKind expectedKind,
            Action<IDbConnection, IDbTransaction, string> insertPayload,
            Func<IDbConnection, IDbTransaction, string, bool> payloadMatches)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (job.Kind != expectedKind) throw new ArgumentException("job_payload_kind_mismatch", nameof(job));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var inserted = SqliteJobStore.EnqueueInTransaction(connection, transaction, job);
            insertPayload(connection, transaction, inserted.Record.Id.ToString("D"));
            if (!payloadMatches(connection, transaction, inserted.Record.Id.ToString("D")))
                throw new InvalidOperationException("job_idempotency_conflict");
            transaction.Commit();
            return inserted.Record;
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static KeyNotFoundException PayloadNotFound(Guid jobId) =>
            new KeyNotFoundException("The typed payload for job " + jobId.ToString("D") + " does not exist.");

        private sealed class RestorePayloadRow
        {
            public string BackupId { get; set; } = string.Empty;
            public string BackupKind { get; set; } = string.Empty;
            public int RestartAfterStage { get; set; }
        }

        private sealed class ScheduledTextPayloadRow
        {
            public string ScheduleId { get; set; } = string.Empty;
            public string Text { get; set; } = string.Empty;
        }

        private sealed class ScheduledRestartPayloadRow
        {
            public string ScheduleId { get; set; } = string.Empty;
            public int CountdownSeconds { get; set; }
        }
    }
}
