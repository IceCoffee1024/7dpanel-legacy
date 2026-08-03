using System;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteRestoreResultMergeStoreTests
    {
        [Fact]
        public void Missing_job_and_catalog_artifact_are_merged_with_typed_payload_once()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteRestoreResultMergeStore(database.ConnectionFactory);
            var snapshot = Snapshot();
            var payload = Payload();
            var completion = new JobCompletion(Utc(5), null, null);

            store.MergeOnce(snapshot, payload, JobStatus.Succeeded, completion);
            store.MergeOnce(snapshot, payload, JobStatus.Succeeded, completion);

            using var connection = database.ConnectionFactory.Open();
            var job = connection.QuerySingle<JobRow>(
                "SELECT * FROM jobs WHERE id = @Id;",
                new { Id = snapshot.JobId.ToString("D") });
            Assert.Equal("Restore", job.kind);
            Assert.Equal("Succeeded", job.status);
            Assert.Equal(snapshot.ActorSubject, job.actor_subject);
            Assert.Equal(snapshot.IdempotencyKey, job.idempotency_key);
            Assert.Equal(snapshot.CorrelationId, job.correlation_id);
            Assert.Equal(snapshot.CreatedAtUtc.ToUnixTimeMilliseconds(), job.created_at_utc);
            Assert.Equal(completion.CompletedAtUtc.ToUnixTimeMilliseconds(), job.completed_at_utc);
            Assert.Null(job.error_code);
            Assert.Equal(1, job.row_version);

            var restored = connection.QuerySingle<RestorePayloadRow>(
                "SELECT * FROM restore_job_payloads WHERE job_id = @Id;",
                new { Id = snapshot.JobId.ToString("D") });
            Assert.Equal(payload.BackupId.ToString("D"), restored.backup_id);
            Assert.Equal(payload.BackupKind.ToString(), restored.backup_kind);
            Assert.Equal(1, restored.restart_after_stage);
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM jobs WHERE id = @Id;",
                new { Id = snapshot.JobId.ToString("D") }));
        }

        [Theory]
        [InlineData(JobStatus.Succeeded, null)]
        [InlineData(JobStatus.Failed, "restore_apply_failed_rolled_back")]
        [InlineData(JobStatus.ResultUnknown, "restore_result_unknown")]
        public void Existing_PendingRestart_job_moves_to_each_restore_terminal(
            JobStatus terminal,
            string? errorCode)
        {
            using var database = new TemporaryDatabase();
            var snapshot = Snapshot();
            InsertPendingRestart(database.ConnectionFactory, snapshot, rowVersion: 8);
            var payload = Payload();
            var store = new SqliteRestoreResultMergeStore(database.ConnectionFactory);

            store.MergeOnce(
                snapshot,
                payload,
                terminal,
                new JobCompletion(Utc(6), null, errorCode));

            using var connection = database.ConnectionFactory.Open();
            var job = connection.QuerySingle<JobRow>(
                "SELECT * FROM jobs WHERE id = @Id;",
                new { Id = snapshot.JobId.ToString("D") });
            Assert.Equal(terminal.ToString(), job.status);
            Assert.Equal(errorCode, job.error_code);
            Assert.Equal(9, job.row_version);
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM restore_job_payloads WHERE job_id = @Id;",
                new { Id = snapshot.JobId.ToString("D") }));
        }

        [Fact]
        public void Existing_terminal_job_is_never_overwritten_by_a_later_result()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteRestoreResultMergeStore(database.ConnectionFactory);
            var snapshot = Snapshot();
            var payload = Payload();
            var first = new JobCompletion(Utc(7), null, null);
            store.MergeOnce(snapshot, payload, JobStatus.Succeeded, first);

            store.MergeOnce(
                snapshot,
                payload,
                JobStatus.Failed,
                new JobCompletion(Utc(8), null, "late_failure"));

            using var connection = database.ConnectionFactory.Open();
            var job = connection.QuerySingle<JobRow>(
                "SELECT * FROM jobs WHERE id = @Id;",
                new { Id = snapshot.JobId.ToString("D") });
            Assert.Equal("Succeeded", job.status);
            Assert.Equal(first.CompletedAtUtc.ToUnixTimeMilliseconds(), job.completed_at_utc);
            Assert.Null(job.error_code);
            Assert.Equal(1, job.row_version);
        }

        [Fact]
        public void Payload_conflict_rolls_back_the_terminal_transition()
        {
            using var database = new TemporaryDatabase();
            var snapshot = Snapshot();
            InsertPendingRestart(database.ConnectionFactory, snapshot, rowVersion: 3);
            var existingPayload = Payload();
            InsertPayload(database.ConnectionFactory, snapshot.JobId, existingPayload);
            var store = new SqliteRestoreResultMergeStore(database.ConnectionFactory);

            var error = Assert.Throws<RestoreResultMergeException>(() => store.MergeOnce(
                snapshot,
                existingPayload with { BackupId = Guid.NewGuid() },
                JobStatus.Failed,
                new JobCompletion(Utc(9), null, "restore_failed")));

            Assert.Equal(SqliteRestoreResultMergeStore.MergeConflictError, error.ErrorCode);
            using var connection = database.ConnectionFactory.Open();
            var job = connection.QuerySingle<JobRow>(
                "SELECT * FROM jobs WHERE id = @Id;",
                new { Id = snapshot.JobId.ToString("D") });
            Assert.Equal("PendingRestart", job.status);
            Assert.Equal(3, job.row_version);
            Assert.Null(job.completed_at_utc);
            Assert.Equal(existingPayload.BackupId.ToString("D"), connection.ExecuteScalar<string>(
                "SELECT backup_id FROM restore_job_payloads WHERE job_id = @Id;",
                new { Id = snapshot.JobId.ToString("D") }));
        }

        private static RestoreMergeJobSnapshot Snapshot() => new RestoreMergeJobSnapshot(
            Guid.NewGuid(),
            JobKind.Restore,
            JobStatus.PendingRestart,
            "owner",
            "restore-after-restart",
            "corr-restore",
            Utc(0));

        private static RestorePayload Payload() =>
            new RestorePayload(Guid.NewGuid(), BackupKind.PanelDatabase, true);

        private static void InsertPendingRestart(
            SqliteConnectionFactory factory,
            RestoreMergeJobSnapshot snapshot,
            long rowVersion)
        {
            using var connection = factory.Open();
            connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, actor_subject, idempotency_key,
                      correlation_id, created_at_utc, row_version)
                  VALUES (@Id, 'Restore', 'PendingRestart', @ActorSubject,
                      @IdempotencyKey, @CorrelationId, @CreatedAtUtc, @RowVersion);",
                new
                {
                    Id = snapshot.JobId.ToString("D"),
                    snapshot.ActorSubject,
                    snapshot.IdempotencyKey,
                    snapshot.CorrelationId,
                    CreatedAtUtc = snapshot.CreatedAtUtc.ToUnixTimeMilliseconds(),
                    RowVersion = rowVersion
                });
        }

        private static void InsertPayload(
            SqliteConnectionFactory factory,
            Guid jobId,
            RestorePayload payload)
        {
            using var connection = factory.Open();
            connection.Execute(
                @"INSERT INTO restore_job_payloads (
                      job_id, backup_id, backup_kind, restart_after_stage)
                  VALUES (@JobId, @BackupId, @BackupKind, @RestartAfterStage);",
                new
                {
                    JobId = jobId.ToString("D"),
                    BackupId = payload.BackupId.ToString("D"),
                    BackupKind = payload.BackupKind.ToString(),
                    RestartAfterStage = payload.RestartAfterStage ? 1 : 0
                });
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 7, minute, 0, TimeSpan.Zero);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Persistence")]

        private sealed class JobRow
        {
            public string kind { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public string idempotency_key { get; set; } = string.Empty;
            public string? correlation_id { get; set; }
            public long created_at_utc { get; set; }
            public long? completed_at_utc { get; set; }
            public string? error_code { get; set; }
            public long row_version { get; set; }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Persistence")]

        private sealed class RestorePayloadRow
        {
            public string backup_id { get; set; } = string.Empty;
            public string backup_kind { get; set; } = string.Empty;
            public int restart_after_stage { get; set; }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-restore-merge-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
