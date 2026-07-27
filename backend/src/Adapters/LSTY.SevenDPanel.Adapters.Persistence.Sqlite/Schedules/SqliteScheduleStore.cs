using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Cronos;
using Dapper;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Schedules
{
    public sealed class SqliteScheduleStore : IScheduleStore
    {
        private const string SelectColumns = @"SELECT
            id AS Id, kind AS Kind, name AS Name, cron_expression AS CronExpression,
            time_zone_id AS TimeZoneId, enabled AS Enabled,
            concurrency_policy AS ConcurrencyPolicy,
            command_text AS CommandText, countdown_seconds AS CountdownSeconds,
            message_text AS MessageText,
            next_occurrence_utc AS NextOccurrenceUtc,
            last_occurrence_utc AS LastOccurrenceUtc, row_version AS RowVersion
            FROM schedules";

        private readonly SqliteConnectionFactory connectionFactory;
        private readonly Func<DateTimeOffset> utcNow;

        public SqliteScheduleStore(
            SqliteConnectionFactory connectionFactory,
            Func<DateTimeOffset>? utcNow = null)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public IReadOnlyList<ScheduleRecord> List()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<ScheduleRow>(
                    SelectColumns + " ORDER BY name COLLATE NOCASE ASC, id ASC;")
                .Select(ToRecord)
                .ToArray();
        }

        public ScheduleRecord? Get(Guid scheduleId)
        {
            if (scheduleId == Guid.Empty)
                throw new ArgumentException("A schedule id is required.", nameof(scheduleId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<ScheduleRow>(
                SelectColumns + " WHERE id = @Id;",
                new { Id = scheduleId.ToString("D") });
            return row == null ? null : ToRecord(row);
        }

        public ScheduleRecord Upsert(ScheduleDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            Validate(definition);
            var now = utcNow();
            RequireUtc(now, nameof(utcNow));
            var cron = CronSchedule.Create(definition.CronExpression, definition.TimeZoneId);
            var next = definition.Enabled ? cron.GetNextOccurrence(now) : null;

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var existing = connection.QuerySingleOrDefault<ScheduleRow>(
                SelectColumns + " WHERE id = @Id;",
                new { Id = definition.Id.ToString("D") }, transaction);
            if (existing == null)
            {
                if (definition.RowVersion != 0)
                    throw new InvalidOperationException("schedule_row_version_conflict");
                connection.Execute(
                    @"INSERT INTO schedules (
                          id, kind, name, cron_expression, time_zone_id, enabled,
                          concurrency_policy, command_text, countdown_seconds, message_text,
                          next_occurrence_utc, last_occurrence_utc, row_version)
                      VALUES (@Id, @Kind, @Name, @CronExpression, @TimeZoneId, @Enabled,
                          @ConcurrencyPolicy, @CommandText, @CountdownSeconds, @MessageText,
                          @NextOccurrenceUtc, NULL, 0);",
                    ToParameters(definition, next), transaction);
            }
            else
            {
                var changed = connection.Execute(
                    @"UPDATE schedules
                      SET kind = @Kind, name = @Name, cron_expression = @CronExpression,
                          time_zone_id = @TimeZoneId, enabled = @Enabled,
                          concurrency_policy = @ConcurrencyPolicy,
                          command_text = @CommandText,
                          countdown_seconds = @CountdownSeconds,
                          message_text = @MessageText,
                          next_occurrence_utc = @NextOccurrenceUtc,
                          row_version = row_version + 1
                      WHERE id = @Id AND row_version = @ExpectedRowVersion;",
                    ToParameters(definition, next), transaction);
                if (changed != 1) throw new InvalidOperationException("schedule_row_version_conflict");
            }

            var stored = connection.QuerySingle<ScheduleRow>(
                SelectColumns + " WHERE id = @Id;",
                new { Id = definition.Id.ToString("D") }, transaction);
            transaction.Commit();
            return ToRecord(stored);
        }

        public bool Delete(Guid scheduleId, long expectedRowVersion)
        {
            if (scheduleId == Guid.Empty)
                throw new ArgumentException("A schedule id is required.", nameof(scheduleId));
            if (expectedRowVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"DELETE FROM schedules
                  WHERE id = @Id AND row_version = @ExpectedRowVersion;",
                new
                {
                    Id = scheduleId.ToString("D"),
                    ExpectedRowVersion = expectedRowVersion
                }) == 1;
        }

        public IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId)
        {
            RequireUtc(now, nameof(now));
            RequireText(ownerId, nameof(ownerId));
            using var connection = connectionFactory.Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var due = connection.Query<ScheduleRow>(
                    SelectColumns +
                    " WHERE enabled = 1 AND next_occurrence_utc IS NOT NULL AND next_occurrence_utc <= @NowUtc" +
                    " ORDER BY next_occurrence_utc ASC, id ASC;",
                    new { NowUtc = now.ToUnixTimeMilliseconds() }).ToArray();
                var claimed = new List<ScheduleRecord>(due.Length);
                foreach (var row in due)
                {
                    var scheduledFor = GetLatestDueOccurrence(row, now);
                    if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM schedule_runs WHERE schedule_id = @ScheduleId AND scheduled_for_utc = @ScheduledForUtc;",
                        new { ScheduleId = row.Id, ScheduledForUtc = scheduledFor }) != 0)
                    {
                        AdvanceSchedule(connection, row, scheduledFor, now);
                        continue;
                    }

                    var running = connection.ExecuteScalar<int>(
                        @"SELECT COUNT(*) FROM jobs
                          WHERE source_schedule_id = @ScheduleId
                            AND status IN ('Running', 'PendingRestart');",
                        new { ScheduleId = row.Id });
                    var queued = connection.ExecuteScalar<int>(
                        @"SELECT COUNT(*) FROM jobs
                          WHERE source_schedule_id = @ScheduleId AND status = 'Queued';",
                        new { ScheduleId = row.Id });
                    string outcome;
                    string? jobId = null;
                    if (row.ConcurrencyPolicy == ScheduleConcurrencyPolicy.SkipIfRunning.ToString() &&
                        (running > 0 || queued > 0))
                    {
                        outcome = "SkippedRunning";
                    }
                    else if (row.ConcurrencyPolicy == ScheduleConcurrencyPolicy.QueueOne.ToString() && queued > 0)
                    {
                        outcome = "SkippedQueueOnePending";
                    }
                    else
                    {
                        jobId = EnqueueScheduledJob(connection, row, scheduledFor);
                        outcome = "Queued";
                    }

                    connection.Execute(
                        @"INSERT INTO schedule_runs (
                              id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                          VALUES (@Id, @ScheduleId, @ScheduledForUtc, @JobId, @Outcome, @CreatedAtUtc)
                          ON CONFLICT(schedule_id, scheduled_for_utc) DO NOTHING;",
                        new
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            ScheduleId = row.Id,
                            ScheduledForUtc = scheduledFor,
                            JobId = jobId,
                            Outcome = outcome,
                            CreatedAtUtc = now.ToUnixTimeMilliseconds()
                        });
                    claimed.Add(AdvanceSchedule(connection, row, scheduledFor, now));
                }

                connection.Execute("COMMIT;");
                return claimed;
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }

        public void RecordOutcome(ScheduleRunOutcome outcome)
        {
            if (outcome == null) throw new ArgumentNullException(nameof(outcome));
            RequireUtc(outcome.ScheduledForUtc, nameof(outcome));
            RequireUtc(outcome.CreatedAtUtc, nameof(outcome));
            var value = RequireText(outcome.Outcome, nameof(outcome));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO schedule_runs (
                      id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                  VALUES (@Id, @ScheduleId, @ScheduledForUtc, @JobId, @Outcome, @CreatedAtUtc)
                  ON CONFLICT(schedule_id, scheduled_for_utc) DO UPDATE SET
                      job_id = COALESCE(excluded.job_id, schedule_runs.job_id),
                      outcome = excluded.outcome,
                      created_at_utc = excluded.created_at_utc;",
                new
                {
                    Id = outcome.Id.ToString("D"),
                    ScheduleId = outcome.ScheduleId.ToString("D"),
                    ScheduledForUtc = outcome.ScheduledForUtc.ToUnixTimeMilliseconds(),
                    JobId = outcome.JobId?.ToString("D"),
                    Outcome = value,
                    CreatedAtUtc = outcome.CreatedAtUtc.ToUnixTimeMilliseconds()
                });
        }

        private static long GetLatestDueOccurrence(ScheduleRow row, DateTimeOffset now)
        {
            var cron = CronExpression.Parse(row.CronExpression, CronFormat.Standard);
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId);
            var latest = cron.GetPreviousOccurrence(now, timeZone, inclusive: true);
            if (!latest.HasValue)
                throw new InvalidOperationException("schedule_due_occurrence_missing");
            return latest.Value.ToUnixTimeMilliseconds();
        }

        private static ScheduleRecord AdvanceSchedule(
            IDbConnection connection,
            ScheduleRow row,
            long scheduledFor,
            DateTimeOffset now)
        {
            var cron = CronSchedule.Create(row.CronExpression, row.TimeZoneId);
            var next = cron.GetNextOccurrence(now);
            var changed = connection.Execute(
                @"UPDATE schedules
                  SET last_occurrence_utc = @ScheduledForUtc,
                      next_occurrence_utc = @NextOccurrenceUtc,
                      row_version = row_version + 1
                  WHERE id = @Id AND row_version = @RowVersion;",
                new
                {
                    row.Id,
                    ScheduledForUtc = scheduledFor,
                    NextOccurrenceUtc = next?.ToUnixTimeMilliseconds(),
                    row.RowVersion
                });
            if (changed != 1) throw new InvalidOperationException("schedule_row_version_conflict");
            row.LastOccurrenceUtc = scheduledFor;
            row.NextOccurrenceUtc = next?.ToUnixTimeMilliseconds();
            row.RowVersion++;
            return ToRecord(row);
        }

        private static string EnqueueScheduledJob(IDbConnection connection, ScheduleRow schedule, long scheduledFor)
        {
            var key = "schedule:" + schedule.Id + ":" + scheduledFor;
            var id = Guid.NewGuid().ToString("D");
            connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, actor_subject, source_schedule_id,
                      idempotency_key, correlation_id, created_at_utc, row_version)
                  VALUES (@Id, @Kind, 'Queued', NULL, @ScheduleId,
                      @IdempotencyKey, @CorrelationId, @CreatedAtUtc, 0)
                  ON CONFLICT(idempotency_key) DO NOTHING;",
                new
                {
                    Id = id,
                    schedule.Kind,
                    ScheduleId = schedule.Id,
                    IdempotencyKey = key,
                    CorrelationId = key,
                    CreatedAtUtc = scheduledFor
                });
            var jobId = connection.ExecuteScalar<string>(
                "SELECT id FROM jobs WHERE idempotency_key = @IdempotencyKey;",
                new { IdempotencyKey = key })!;
            InsertScheduledPayload(connection, schedule, jobId);
            return jobId;
        }

        private static void Validate(ScheduleDefinition definition)
        {
            if (definition.Id == Guid.Empty) throw new ArgumentException("A schedule id is required.", nameof(definition));
            if (definition.Action == null) throw new ArgumentNullException(nameof(definition.Action));
            RequireText(definition.Name, nameof(definition));
            switch (definition.Action)
            {
                case ScheduledConsoleCommandAction command:
                    var commandText = RequireText(command.CommandText, nameof(command.CommandText));
                    if (commandText.IndexOf('\r') >= 0 || commandText.IndexOf('\n') >= 0)
                        throw new ArgumentException("schedule_command_invalid", nameof(definition));
                    break;
                case ScheduledRestartAction restart when restart.CountdownSeconds < 0 || restart.CountdownSeconds > 86400:
                    throw new ArgumentOutOfRangeException(nameof(definition));
                case ScheduledRestartAction:
                    break;
                case ScheduledAnnouncementAction announcement
                    when string.IsNullOrEmpty(announcement.MessageText) || announcement.MessageText.Length > 500:
                    throw new ArgumentOutOfRangeException(nameof(definition));
                case ScheduledAnnouncementAction:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(definition));
            }
            if (!Enum.IsDefined(typeof(ScheduleConcurrencyPolicy), definition.ConcurrencyPolicy))
                throw new ArgumentOutOfRangeException(nameof(definition));
            if (definition.RowVersion < 0) throw new ArgumentOutOfRangeException(nameof(definition));
        }

        private static ScheduleRecord ToRecord(ScheduleRow row) => new ScheduleRecord(
            Guid.Parse(row.Id),
            row.Name,
            row.CronExpression,
            row.TimeZoneId,
            row.Enabled != 0,
            (ScheduleConcurrencyPolicy)Enum.Parse(typeof(ScheduleConcurrencyPolicy), row.ConcurrencyPolicy),
            ToAction(row),
            row.NextOccurrenceUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.NextOccurrenceUtc.Value) : null,
            row.LastOccurrenceUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.LastOccurrenceUtc.Value) : null,
            row.RowVersion);

        private static object ToParameters(ScheduleDefinition definition, DateTimeOffset? next) => new
        {
            Id = definition.Id.ToString("D"),
            Kind = definition.Kind.ToString(),
            Name = definition.Name.Trim(),
            definition.CronExpression,
            definition.TimeZoneId,
            Enabled = definition.Enabled ? 1 : 0,
            ConcurrencyPolicy = definition.ConcurrencyPolicy.ToString(),
            CommandText = (definition.Action as ScheduledConsoleCommandAction)?.CommandText.Trim(),
            CountdownSeconds = (definition.Action as ScheduledRestartAction)?.CountdownSeconds,
            MessageText = (definition.Action as ScheduledAnnouncementAction)?.MessageText,
            NextOccurrenceUtc = next?.ToUnixTimeMilliseconds(),
            ExpectedRowVersion = definition.RowVersion
        };

        private static ScheduleAction ToAction(ScheduleRow row)
        {
            var kind = (JobKind)Enum.Parse(typeof(JobKind), row.Kind);
            switch (kind)
            {
                case JobKind.ScheduledConsoleCommand:
                    return new ScheduledConsoleCommandAction(
                        row.CommandText ?? throw new InvalidOperationException("schedule_action_missing"));
                case JobKind.ScheduledRestart:
                    return new ScheduledRestartAction(
                        row.CountdownSeconds ?? throw new InvalidOperationException("schedule_action_missing"));
                case JobKind.ScheduledAnnouncement:
                    return new ScheduledAnnouncementAction(
                        row.MessageText ?? throw new InvalidOperationException("schedule_action_missing"));
                default:
                    throw new InvalidOperationException("schedule_kind_invalid");
            }
        }

        private static void InsertScheduledPayload(
            IDbConnection connection,
            ScheduleRow schedule,
            string jobId)
        {
            var scheduleId = schedule.Id;
            switch ((JobKind)Enum.Parse(typeof(JobKind), schedule.Kind))
            {
                case JobKind.ScheduledConsoleCommand:
                    connection.Execute(
                        @"INSERT OR IGNORE INTO scheduled_console_command_job_payloads (
                              job_id, schedule_id, command_text)
                          VALUES (@JobId, @ScheduleId, @CommandText);",
                        new { JobId = jobId, ScheduleId = scheduleId, schedule.CommandText });
                    break;
                case JobKind.ScheduledRestart:
                    connection.Execute(
                        @"INSERT OR IGNORE INTO scheduled_restart_job_payloads (
                              job_id, schedule_id, countdown_seconds)
                          VALUES (@JobId, @ScheduleId, @CountdownSeconds);",
                        new { JobId = jobId, ScheduleId = scheduleId, schedule.CountdownSeconds });
                    break;
                case JobKind.ScheduledAnnouncement:
                    connection.Execute(
                        @"INSERT OR IGNORE INTO scheduled_announcement_job_payloads (
                              job_id, schedule_id, message_text)
                          VALUES (@JobId, @ScheduleId, @MessageText);",
                        new { JobId = jobId, ScheduleId = scheduleId, schedule.MessageText });
                    break;
                default:
                    throw new InvalidOperationException("schedule_kind_invalid");
            }
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        private static void TryRollback(IDbConnection connection)
        {
            try { connection.Execute("ROLLBACK;"); }
            catch { }
        }

        private sealed class ScheduleRow
        {
            public string Id { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string CronExpression { get; set; } = string.Empty;
            public string TimeZoneId { get; set; } = string.Empty;
            public int Enabled { get; set; }
            public string ConcurrencyPolicy { get; set; } = string.Empty;
            public string? CommandText { get; set; }
            public int? CountdownSeconds { get; set; }
            public string? MessageText { get; set; }
            public long? NextOccurrenceUtc { get; set; }
            public long? LastOccurrenceUtc { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
