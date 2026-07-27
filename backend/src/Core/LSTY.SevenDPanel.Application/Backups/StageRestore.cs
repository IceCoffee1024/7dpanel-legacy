using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public interface IPendingRestoreMarkerStore
    {
        bool TryCreateMarker(BackupArtifact artifact, JobRecord pendingRestartJob);
    }

    public sealed class StageRestore
    {
        public const int SupportedManifestVersion = 1;
        public const string AlreadyPendingError = "restore_already_pending";
        public const string BackupNotFoundError = "backup_not_found";
        public const string BackupIntegrityFailedError = "backup_integrity_failed";
        public const string JobStateConflictError = "job_state_conflict";

        private readonly IBackupCatalog catalog;
        private readonly IJobStore jobs;
        private readonly IPendingRestoreMarkerStore markerStore;
        private readonly IRestartScriptLauncher launcher;
        private readonly Func<DateTimeOffset> utcNow;

        public StageRestore(
            IBackupCatalog catalog,
            IJobStore jobs,
            IPendingRestoreMarkerStore markerStore,
            IRestartScriptLauncher launcher,
            Func<DateTimeOffset> utcNow)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.markerStore = markerStore ?? throw new ArgumentNullException(nameof(markerStore));
            this.launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public JobRecord Execute(Guid jobId, RestorePayload payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var job = ReadJob(jobId);
            if (job.Kind != JobKind.Restore || job.Status != JobStatus.Queued)
                throw new StageRestoreException(JobStateConflictError);

            var artifact = ReadArtifact(payload.BackupId);
            ValidateArtifact(artifact, payload);

            var now = utcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("restore_stage_clock_not_utc");
            var pendingRestartJob = job with
            {
                Status = JobStatus.PendingRestart,
                CompletedAtUtc = null,
                ErrorCode = null,
                WorkerId = null,
                RowVersion = checked(job.RowVersion + 1)
            };

            if (!markerStore.TryCreateMarker(artifact, pendingRestartJob))
                throw new StageRestoreException(AlreadyPendingError);

            if (!jobs.TryTransition(
                    job.Id,
                    job.RowVersion,
                    JobStatus.Queued,
                    JobStatus.PendingRestart,
                    new JobCompletion(now, job.Progress, null)))
            {
                throw new StageRestoreException(JobStateConflictError);
            }

            if (payload.RestartAfterStage)
                launcher.StartConfiguredScript();
            return pendingRestartJob;
        }

        private JobRecord ReadJob(Guid jobId)
        {
            if (jobId == Guid.Empty) throw new JobNotFoundException();
            try
            {
                return jobs.Get(jobId);
            }
            catch (KeyNotFoundException exception)
            {
                throw new JobNotFoundException(exception);
            }
        }

        private BackupArtifact ReadArtifact(Guid backupId)
        {
            try
            {
                return catalog.Get(backupId);
            }
            catch (KeyNotFoundException exception)
            {
                throw new StageRestoreException(BackupNotFoundError, exception);
            }
        }

        private static void ValidateArtifact(BackupArtifact artifact, RestorePayload payload)
        {
            if (artifact == null ||
                artifact.Id != payload.BackupId ||
                artifact.Kind != payload.BackupKind ||
                artifact.ManifestVersion != SupportedManifestVersion ||
                !string.Equals(artifact.ValidationStatus, "Verified", StringComparison.Ordinal) ||
                artifact.Sha256 == null || artifact.Sha256.Length != 64 ||
                !artifact.Sha256.All(IsHex))
            {
                throw new StageRestoreException(BackupIntegrityFailedError);
            }
        }

        private static bool IsHex(char value) =>
            (value >= '0' && value <= '9') ||
            (value >= 'a' && value <= 'f') ||
            (value >= 'A' && value <= 'F');
    }

    public sealed class StageRestoreException : Exception
    {
        public StageRestoreException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public StageRestoreException(string errorCode, Exception innerException)
            : base(errorCode, innerException) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }
}
