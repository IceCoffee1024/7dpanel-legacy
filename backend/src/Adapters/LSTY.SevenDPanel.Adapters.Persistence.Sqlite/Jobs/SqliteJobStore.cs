using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs
{
    public sealed class SqliteJobStore : IJobStore
    {
        private const string SelectColumns = @"SELECT
            id AS Id, kind AS Kind, status AS Status, actor_subject AS ActorSubject,
            source_schedule_id AS SourceScheduleId, idempotency_key AS IdempotencyKey,
            correlation_id AS CorrelationId, created_at_utc AS CreatedAtUtc,
            started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc,
            progress_current AS ProgressCurrent, progress_total AS ProgressTotal,
            error_code AS ErrorCode, worker_id AS WorkerId, row_version AS RowVersion
            FROM jobs";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteJobStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public JobRecord Enqueue(NewJob job)
        {
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var result = EnqueueInTransaction(connection, transaction, job);
            transaction.Commit();
            return result.Record;
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
                    SelectColumns + " WHERE status = 'Queued' ORDER BY created_at_utc ASC, id ASC LIMIT 1;");
                if (queued == null)
                {
                    connection.Execute("COMMIT;");
                    return null;
                }

                var changed = connection.Execute(
                    @"UPDATE jobs
                      SET status = 'Running', started_at_utc = @StartedAtUtc,
                          worker_id = @WorkerId, row_version = row_version + 1
                      WHERE id = @Id AND status = 'Queued' AND row_version = @RowVersion;",
                    new
                    {
                        queued.Id,
                        StartedAtUtc = now.ToUnixTimeMilliseconds(),
                        WorkerId = workerId,
                        queued.RowVersion
                    });
                if (changed != 1)
                {
                    connection.Execute("ROLLBACK;");
                    return null;
                }

                var claimed = connection.QuerySingle<JobRow>(SelectColumns + " WHERE id = @Id;", new { queued.Id });
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
            JobCompletion completion)
        {
            if (expectedRowVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            RequireUtc(completion.CompletedAtUtc, nameof(completion));
            if (!JobStateMachine.CanTransition(GetKind(jobId), expected, next)) return false;

            var terminal = IsTerminal(next);
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE jobs
                  SET status = @Next,
                      completed_at_utc = @CompletedAtUtc,
                      progress_current = @ProgressCurrent,
                      progress_total = @ProgressTotal,
                      error_code = @ErrorCode,
                      worker_id = CASE WHEN @Terminal = 1 THEN NULL ELSE worker_id END,
                      row_version = row_version + 1
                  WHERE id = @Id AND status = @Expected AND row_version = @ExpectedRowVersion;",
                new
                {
                    Id = jobId.ToString("D"),
                    Expected = expected.ToString(),
                    Next = next.ToString(),
                    CompletedAtUtc = terminal ? completion.CompletedAtUtc.ToUnixTimeMilliseconds() : (long?)null,
                    ProgressCurrent = completion.Progress?.Current,
                    ProgressTotal = completion.Progress?.Total,
                    completion.ErrorCode,
                    Terminal = terminal ? 1 : 0,
                    ExpectedRowVersion = expectedRowVersion
                }) == 1;
        }

        public JobRecord Get(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<JobRow>(
                SelectColumns + " WHERE id = @Id;", new { Id = jobId.ToString("D") });
            return row == null
                ? throw new KeyNotFoundException("The job does not exist.")
                : ToRecord(row);
        }

        public PagedResult<JobRecord, JobCursor> List(JobQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (query.PageSize < 1 || query.PageSize > 100)
                throw new ArgumentOutOfRangeException(nameof(query));
            if (query.FromUtc.HasValue) RequireUtc(query.FromUtc.Value, nameof(query));
            if (query.ToUtc.HasValue) RequireUtc(query.ToUtc.Value, nameof(query));
            if (query.Cursor != null) RequireUtc(query.Cursor.CreatedAtUtc, nameof(query));

            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.Kind.HasValue)
            {
                where.Add("kind = @Kind");
                parameters.Add("Kind", query.Kind.Value.ToString());
            }
            if (query.Status.HasValue)
            {
                where.Add("status = @Status");
                parameters.Add("Status", query.Status.Value.ToString());
            }
            if (query.FromUtc.HasValue)
            {
                where.Add("created_at_utc >= @FromUtc");
                parameters.Add("FromUtc", query.FromUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.ToUtc.HasValue)
            {
                where.Add("created_at_utc <= @ToUtc");
                parameters.Add("ToUtc", query.ToUtc.Value.ToUnixTimeMilliseconds());
            }
            if (query.Cursor != null)
            {
                where.Add("(created_at_utc < @CursorUtc OR (created_at_utc = @CursorUtc AND id < @CursorId))");
                parameters.Add("CursorUtc", query.Cursor.CreatedAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorId", query.Cursor.Id.ToString("D"));
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<JobRow>(
                SelectColumns +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY created_at_utc DESC, id DESC LIMIT @Take;", parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            JobCursor? nextCursor = rows.Length > query.PageSize && pageRows.Length > 0
                ? new JobCursor(
                    DateTimeOffset.FromUnixTimeMilliseconds(pageRows[pageRows.Length - 1].CreatedAtUtc),
                    Guid.Parse(pageRows[pageRows.Length - 1].Id))
                : null;
            return new PagedResult<JobRecord, JobCursor>(pageRows.Select(ToRecord).ToArray(), nextCursor);
        }

        internal static JobInsertResult EnqueueInTransaction(
            IDbConnection connection,
            IDbTransaction transaction,
            NewJob job)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (job == null) throw new ArgumentNullException(nameof(job));
            RequireUtc(job.CreatedAtUtc, nameof(job));
            var key = RequireText(job.IdempotencyKey, nameof(job));
            var id = Guid.NewGuid().ToString("D");
            var inserted = connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, actor_subject, source_schedule_id, idempotency_key,
                      correlation_id, created_at_utc, row_version)
                  VALUES (@Id, @Kind, 'Queued', @ActorSubject, @SourceScheduleId,
                      @IdempotencyKey, @CorrelationId, @CreatedAtUtc, 0)
                  ON CONFLICT(idempotency_key) DO NOTHING;",
                new
                {
                    Id = id,
                    Kind = job.Kind.ToString(),
                    ActorSubject = Normalize(job.ActorSubject),
                    SourceScheduleId = job.SourceScheduleId?.ToString("D"),
                    IdempotencyKey = key,
                    CorrelationId = Normalize(job.CorrelationId),
                    CreatedAtUtc = job.CreatedAtUtc.ToUnixTimeMilliseconds()
                }, transaction) == 1;
            var row = connection.QuerySingle<JobRow>(
                SelectColumns + " WHERE idempotency_key = @IdempotencyKey;",
                new { IdempotencyKey = key }, transaction);
            if (!string.Equals(row.Kind, job.Kind.ToString(), StringComparison.Ordinal) ||
                !string.Equals(row.SourceScheduleId, job.SourceScheduleId?.ToString("D"), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("job_idempotency_conflict");
            }
            return new JobInsertResult(ToRecord(row), inserted);
        }

        private JobKind GetKind(Guid jobId)
        {
            using var connection = connectionFactory.Open();
            var kind = connection.ExecuteScalar<string?>(
                "SELECT kind FROM jobs WHERE id = @Id;", new { Id = jobId.ToString("D") });
            return kind == null
                ? throw new KeyNotFoundException("The job does not exist.")
                : (JobKind)Enum.Parse(typeof(JobKind), kind);
        }

        private static JobRecord ToRecord(JobRow row) => new JobRecord(
            Guid.Parse(row.Id),
            (JobKind)Enum.Parse(typeof(JobKind), row.Kind),
            (JobStatus)Enum.Parse(typeof(JobStatus), row.Status),
            row.ActorSubject,
            string.IsNullOrEmpty(row.SourceScheduleId) ? null : Guid.Parse(row.SourceScheduleId),
            row.IdempotencyKey,
            row.CorrelationId,
            DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAtUtc),
            row.StartedAtUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUtc.Value) : null,
            row.CompletedAtUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value) : null,
            row.ProgressCurrent.HasValue || row.ProgressTotal.HasValue
                ? new JobProgress(row.ProgressCurrent, row.ProgressTotal)
                : null,
            row.ErrorCode,
            row.WorkerId,
            row.RowVersion);

        private static bool IsTerminal(JobStatus status) =>
            status == JobStatus.Succeeded || status == JobStatus.Failed ||
            status == JobStatus.Cancelled || status == JobStatus.Interrupted ||
            status == JobStatus.ResultUnknown;

        private static void TryRollback(IDbConnection connection)
        {
            try { connection.Execute("ROLLBACK;"); }
            catch { }
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }

        internal sealed class JobInsertResult
        {
            public JobInsertResult(JobRecord record, bool inserted)
            {
                Record = record;
                Inserted = inserted;
            }

            public JobRecord Record { get; }
            public bool Inserted { get; }
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
