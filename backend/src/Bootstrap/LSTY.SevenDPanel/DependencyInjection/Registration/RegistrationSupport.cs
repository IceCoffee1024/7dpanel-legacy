using System;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class FeatureModuleJobActivityQuery : IFeatureModuleActivityQuery
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public FeatureModuleJobActivityQuery(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public bool HasActiveWork(FeatureModuleId moduleId)
        {
            string[] kinds;
            switch (moduleId)
            {
                case FeatureModuleId.WorldTools:
                    kinds = new[] { JobKind.WorldOperation.ToString() };
                    break;
                case FeatureModuleId.Backups:
                    kinds = new[]
                    {
                        JobKind.WorldBackup.ToString(),
                        JobKind.PanelDatabaseBackup.ToString(),
                        JobKind.ServerConfigurationBackup.ToString(),
                        JobKind.Restore.ToString()
                    };
                    break;
                case FeatureModuleId.AnnouncementsAndScheduling:
                    kinds = new[]
                    {
                        JobKind.ScheduledConsoleCommand.ToString(),
                        JobKind.ScheduledRestart.ToString(),
                        JobKind.ScheduledAnnouncement.ToString()
                    };
                    break;
                default:
                    return false;
            }

            using var connection = connectionFactory.Open();
            return connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM jobs
                  WHERE status IN ('Queued', 'Running') AND kind IN @Kinds;",
                new { Kinds = kinds }) != 0;
        }
    }

    internal sealed class BackgroundWorkerJobStore : IJobStore
    {
        private const string InterruptedError = "worker_restart_interrupted";
        private static readonly string[] GenericWorkerSupportedKinds =
        {
            JobKind.WorldBackup.ToString(),
            JobKind.PanelDatabaseBackup.ToString(),
            JobKind.ServerConfigurationBackup.ToString(),
            JobKind.ScheduledConsoleCommand.ToString(),
            JobKind.ScheduledRestart.ToString(),
            JobKind.ScheduledAnnouncement.ToString(),
            JobKind.WorldOperation.ToString()
        };
        private const string SelectColumns = @"SELECT
            id AS Id, kind AS Kind, status AS Status, actor_subject AS ActorSubject,
            source_schedule_id AS SourceScheduleId, idempotency_key AS IdempotencyKey,
            correlation_id AS CorrelationId, created_at_utc AS CreatedAtUtc,
            started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc,
            progress_current AS ProgressCurrent, progress_total AS ProgressTotal,
            error_code AS ErrorCode, worker_id AS WorkerId, row_version AS RowVersion
            FROM jobs";

        private readonly SqliteJobStore inner;
        private readonly SqliteConnectionFactory connectionFactory;

        public BackgroundWorkerJobStore(
            SqliteJobStore inner,
            SqliteConnectionFactory connectionFactory)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public JobRecord Enqueue(NewJob job) => inner.Enqueue(job);

        public int InterruptRunningJobs(DateTimeOffset now)
        {
            RequireUtc(now, nameof(now));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE jobs
                  SET status = 'Interrupted', completed_at_utc = @CompletedAtUtc,
                      error_code = @ErrorCode, worker_id = NULL,
                      row_version = row_version + 1
                  WHERE status = 'Running';",
                new
                {
                    CompletedAtUtc = now.ToUnixTimeMilliseconds(),
                    ErrorCode = InterruptedError
                });
        }

        public JobRecord? TryClaimNext(string workerId, DateTimeOffset now)
        {
            workerId = RequireText(workerId, nameof(workerId));
            RequireUtc(now, nameof(now));
            using var connection = connectionFactory.Open();
            connection.Execute("BEGIN IMMEDIATE;");
            try
            {
                var queued = connection.QueryFirstOrDefault<JobRow>(
                    SelectColumns +
                    " WHERE status = 'Queued' AND kind IN @SupportedKinds" +
                    " ORDER BY created_at_utc ASC, id ASC LIMIT 1;",
                    new { SupportedKinds = GenericWorkerSupportedKinds });
                if (queued == null)
                {
                    connection.Execute("COMMIT;");
                    return null;
                }

                var changed = connection.Execute(
                    @"UPDATE jobs
                      SET status = 'Running', started_at_utc = @StartedAtUtc,
                          worker_id = @WorkerId, row_version = row_version + 1
                      WHERE id = @Id AND status = 'Queued'
                        AND kind IN @SupportedKinds AND row_version = @RowVersion;",
                    new
                    {
                        queued.Id,
                        StartedAtUtc = now.ToUnixTimeMilliseconds(),
                        WorkerId = workerId,
                        SupportedKinds = GenericWorkerSupportedKinds,
                        queued.RowVersion
                    });
                if (changed != 1)
                {
                    connection.Execute("ROLLBACK;");
                    return null;
                }

                var claimed = connection.QuerySingle<JobRow>(
                    SelectColumns + " WHERE id = @Id;",
                    new { queued.Id });
                connection.Execute("COMMIT;");
                return ToRecord(claimed);
            }
            catch
            {
                TryRollback(connection);
                throw;
            }
        }

        public bool TryTransition(
            Guid jobId,
            long expectedRowVersion,
            JobStatus expected,
            JobStatus next,
            JobCompletion completion) => inner.TryTransition(
                jobId,
                expectedRowVersion,
                expected,
                next,
                completion);

        public JobRecord Get(Guid jobId) => inner.Get(jobId);

        public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
            inner.List(query);

        private static JobRecord ToRecord(JobRow row) => new JobRecord(
            Guid.Parse(row.Id),
            (JobKind)Enum.Parse(typeof(JobKind), row.Kind),
            (JobStatus)Enum.Parse(typeof(JobStatus), row.Status),
            row.ActorSubject,
            string.IsNullOrEmpty(row.SourceScheduleId)
                ? null
                : Guid.Parse(row.SourceScheduleId),
            row.IdempotencyKey,
            row.CorrelationId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            row.StartedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUtc.Value)
                : null,
            row.CompletedAtUtc.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value)
                : null,
            row.ProgressCurrent.HasValue || row.ProgressTotal.HasValue
                ? new JobProgress(row.ProgressCurrent, row.ProgressTotal)
                : null,
            row.ErrorCode,
            row.WorkerId,
            row.RowVersion);

        private static void TryRollback(System.Data.IDbConnection connection)
        {
            try { connection.Execute("ROLLBACK;"); }
            catch { }
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(
                    "A non-empty value is required.",
                    parameterName);
            return value.Trim();
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException(
                    "A UTC timestamp is required.",
                    parameterName);
        }

        private sealed class JobRow
        {
            public string Id { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string? ActorSubject { get; set; }
            public string? SourceScheduleId { get; set; }
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? CorrelationId { get; set; }
            public long CreatedAtUtc { get; set; }
            public long? StartedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long? ProgressCurrent { get; set; }
            public long? ProgressTotal { get; set; }
            public string? ErrorCode { get; set; }
            public string? WorkerId { get; set; }
            public long RowVersion { get; set; }
        }
    }
}
