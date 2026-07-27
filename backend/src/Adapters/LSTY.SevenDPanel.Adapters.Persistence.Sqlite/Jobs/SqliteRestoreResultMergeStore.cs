using System;
using Dapper;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs
{
    public sealed class SqliteRestoreResultMergeStore : IRestoreResultMergeStore
    {
        public const string MergeConflictError = "restore_result_merge_conflict";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteRestoreResultMergeStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));

        public void MergeOnce(
            RestoreMergeJobSnapshot snapshot,
            RestorePayload payload,
            JobStatus status,
            JobCompletion completion)
        {
            Validate(snapshot, payload, status, completion);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            try
            {
                var existing = connection.QuerySingleOrDefault<JobMergeRow>(
                    @"SELECT
                          kind AS Kind, status AS Status, actor_subject AS ActorSubject,
                          idempotency_key AS IdempotencyKey, correlation_id AS CorrelationId,
                          created_at_utc AS CreatedAtUtc
                      FROM jobs
                      WHERE id = @Id;",
                    new { Id = snapshot.JobId.ToString("D") },
                    transaction);

                if (existing == null)
                {
                    InsertTerminal(connection, transaction, snapshot, status, completion);
                }
                else
                {
                    EnsureSameSnapshot(existing, snapshot);
                    if (!IsTerminal(existing.Status))
                    {
                        if (!string.Equals(
                                existing.Status,
                                JobStatus.PendingRestart.ToString(),
                                StringComparison.Ordinal))
                        {
                            throw Conflict();
                        }
                        var changed = connection.Execute(
                            @"UPDATE jobs
                              SET status = @Status,
                                  completed_at_utc = @CompletedAtUtc,
                                  progress_current = @ProgressCurrent,
                                  progress_total = @ProgressTotal,
                                  error_code = @ErrorCode,
                                  worker_id = NULL,
                                  row_version = row_version + 1
                              WHERE id = @Id AND kind = 'Restore'
                                AND status = 'PendingRestart';",
                            new
                            {
                                Id = snapshot.JobId.ToString("D"),
                                Status = status.ToString(),
                                CompletedAtUtc = completion.CompletedAtUtc.ToUnixTimeMilliseconds(),
                                ProgressCurrent = completion.Progress?.Current,
                                ProgressTotal = completion.Progress?.Total,
                                completion.ErrorCode
                            },
                            transaction);
                        if (changed != 1) throw Conflict();
                    }
                }

                EnsurePayload(connection, transaction, snapshot.JobId, payload);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static void InsertTerminal(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            RestoreMergeJobSnapshot snapshot,
            JobStatus status,
            JobCompletion completion)
        {
            try
            {
                connection.Execute(
                    @"INSERT INTO jobs (
                          id, kind, status, actor_subject, source_schedule_id,
                          idempotency_key, correlation_id, created_at_utc,
                          started_at_utc, completed_at_utc,
                          progress_current, progress_total, error_code,
                          worker_id, row_version)
                      VALUES (@Id, 'Restore', @Status, @ActorSubject, NULL,
                          @IdempotencyKey, @CorrelationId, @CreatedAtUtc,
                          NULL, @CompletedAtUtc,
                          @ProgressCurrent, @ProgressTotal, @ErrorCode,
                          NULL, 1);",
                    new
                    {
                        Id = snapshot.JobId.ToString("D"),
                        Status = status.ToString(),
                        snapshot.ActorSubject,
                        snapshot.IdempotencyKey,
                        snapshot.CorrelationId,
                        CreatedAtUtc = snapshot.CreatedAtUtc.ToUnixTimeMilliseconds(),
                        CompletedAtUtc = completion.CompletedAtUtc.ToUnixTimeMilliseconds(),
                        ProgressCurrent = completion.Progress?.Current,
                        ProgressTotal = completion.Progress?.Total,
                        completion.ErrorCode
                    },
                    transaction);
            }
            catch (Exception exception)
            {
                throw Conflict(exception);
            }
        }

        private static void EnsurePayload(
            System.Data.IDbConnection connection,
            System.Data.IDbTransaction transaction,
            Guid jobId,
            RestorePayload payload)
        {
            connection.Execute(
                @"INSERT OR IGNORE INTO restore_job_payloads (
                      job_id, backup_id, backup_kind, restart_after_stage)
                  VALUES (@JobId, @BackupId, @BackupKind, @RestartAfterStage);",
                new
                {
                    JobId = jobId.ToString("D"),
                    BackupId = payload.BackupId.ToString("D"),
                    BackupKind = payload.BackupKind.ToString(),
                    RestartAfterStage = payload.RestartAfterStage ? 1 : 0
                },
                transaction);
            var matches = connection.ExecuteScalar<int>(
                @"SELECT COUNT(*)
                  FROM restore_job_payloads
                  WHERE job_id = @JobId AND backup_id = @BackupId
                    AND backup_kind = @BackupKind
                    AND restart_after_stage = @RestartAfterStage;",
                new
                {
                    JobId = jobId.ToString("D"),
                    BackupId = payload.BackupId.ToString("D"),
                    BackupKind = payload.BackupKind.ToString(),
                    RestartAfterStage = payload.RestartAfterStage ? 1 : 0
                },
                transaction);
            if (matches != 1) throw Conflict();
        }

        private static void EnsureSameSnapshot(
            JobMergeRow existing,
            RestoreMergeJobSnapshot snapshot)
        {
            if (!string.Equals(existing.Kind, JobKind.Restore.ToString(), StringComparison.Ordinal) ||
                !string.Equals(existing.ActorSubject, snapshot.ActorSubject, StringComparison.Ordinal) ||
                !string.Equals(existing.IdempotencyKey, snapshot.IdempotencyKey, StringComparison.Ordinal) ||
                !string.Equals(existing.CorrelationId, snapshot.CorrelationId, StringComparison.Ordinal) ||
                existing.CreatedAtUtc != snapshot.CreatedAtUtc.ToUnixTimeMilliseconds())
            {
                throw Conflict();
            }
        }

        private static void Validate(
            RestoreMergeJobSnapshot snapshot,
            RestorePayload payload,
            JobStatus status,
            JobCompletion completion)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            if (snapshot.JobId == Guid.Empty || snapshot.JobKind != JobKind.Restore ||
                snapshot.JobStatus != JobStatus.PendingRestart ||
                string.IsNullOrWhiteSpace(snapshot.IdempotencyKey) ||
                snapshot.CreatedAtUtc.Offset != TimeSpan.Zero)
            {
                throw Conflict();
            }
            if (payload.BackupId == Guid.Empty ||
                !Enum.IsDefined(typeof(BackupKind), payload.BackupKind))
            {
                throw Conflict();
            }
            if (status != JobStatus.Succeeded && status != JobStatus.Failed &&
                status != JobStatus.ResultUnknown)
            {
                throw new ArgumentOutOfRangeException(nameof(status));
            }
            if (completion.CompletedAtUtc.Offset != TimeSpan.Zero ||
                completion.CompletedAtUtc < snapshot.CreatedAtUtc)
            {
                throw Conflict();
            }
        }

        private static bool IsTerminal(string status) =>
            string.Equals(status, JobStatus.Succeeded.ToString(), StringComparison.Ordinal) ||
            string.Equals(status, JobStatus.Failed.ToString(), StringComparison.Ordinal) ||
            string.Equals(status, JobStatus.Cancelled.ToString(), StringComparison.Ordinal) ||
            string.Equals(status, JobStatus.Interrupted.ToString(), StringComparison.Ordinal) ||
            string.Equals(status, JobStatus.ResultUnknown.ToString(), StringComparison.Ordinal);

        private static RestoreResultMergeException Conflict() =>
            new RestoreResultMergeException(MergeConflictError);

        private static RestoreResultMergeException Conflict(Exception innerException) =>
            new RestoreResultMergeException(MergeConflictError, innerException);

        private sealed class JobMergeRow
        {
            public string Kind { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string? ActorSubject { get; set; }
            public string IdempotencyKey { get; set; } = string.Empty;
            public string? CorrelationId { get; set; }
            public long CreatedAtUtc { get; set; }
        }
    }

    public sealed class RestoreResultMergeException : Exception
    {
        public RestoreResultMergeException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public RestoreResultMergeException(string errorCode, Exception innerException)
            : base(errorCode, innerException) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }
}
