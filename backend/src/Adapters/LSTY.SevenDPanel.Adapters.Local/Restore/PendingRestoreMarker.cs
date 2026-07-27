using System;
using System.IO;
using System.Linq;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public enum RestoreExecutionStage
    {
        Prepared,
        Applied,
        RolledBack,
        RollbackFailed
    }

    public sealed record RestoreJobSnapshot(
        Guid JobId,
        JobKind JobKind,
        JobStatus JobStatus,
        string? ActorSubject,
        string IdempotencyKey,
        string? CorrelationId,
        DateTimeOffset CreatedAtUtc);

    public sealed record PendingRestoreMarker(
        int Version,
        Guid ArtifactId,
        BackupKind BackupKind,
        string BackupRootId,
        string RelativeResourceId,
        string Sha256,
        RestoreJobSnapshot JobSnapshot,
        RestoreExecutionStage Stage)
    {
        public const int CurrentVersion = 1;

        internal void Validate()
        {
            RestoreStateValidation.ValidateCommon(
                Version,
                ArtifactId,
                BackupKind,
                BackupRootId,
                RelativeResourceId,
                Sha256,
                JobSnapshot,
                Stage);
            if (Stage != RestoreExecutionStage.Prepared)
                throw new FormatException("pending_restore_marker_stage_invalid");
        }
    }

    public sealed class RestoreStateException : Exception
    {
        public RestoreStateException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public RestoreStateException(string errorCode, Exception innerException)
            : base(errorCode, innerException) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }

    internal static class RestoreStateValidation
    {
        internal static void ValidateCommon(
            int version,
            Guid artifactId,
            BackupKind backupKind,
            string backupRootId,
            string relativeResourceId,
            string sha256,
            RestoreJobSnapshot snapshot,
            RestoreExecutionStage stage)
        {
            if (version != PendingRestoreMarker.CurrentVersion)
                throw new FormatException("restore_state_version_invalid");
            if (artifactId == Guid.Empty)
                throw new FormatException("restore_artifact_id_invalid");
            if (!Enum.IsDefined(typeof(BackupKind), backupKind))
                throw new FormatException("restore_backup_kind_invalid");
            RequireOpaque(backupRootId, "restore_backup_root_id_invalid");
            RequireOpaque(relativeResourceId, "restore_resource_id_invalid");
            if (sha256 == null || sha256.Length != 64 || !sha256.All(IsHex))
                throw new FormatException("restore_sha256_invalid");
            if (!Enum.IsDefined(typeof(RestoreExecutionStage), stage))
                throw new FormatException("restore_stage_invalid");
            ValidateSnapshot(snapshot);
        }

        internal static void ValidateSnapshot(RestoreJobSnapshot snapshot)
        {
            if (snapshot == null || snapshot.JobId == Guid.Empty)
                throw new FormatException("restore_job_snapshot_invalid");
            if (snapshot.JobKind != JobKind.Restore ||
                snapshot.JobStatus != JobStatus.PendingRestart)
            {
                throw new FormatException("restore_job_snapshot_invalid");
            }
            RequireText(snapshot.IdempotencyKey, 256, "restore_job_snapshot_invalid");
            RequireOptionalText(snapshot.ActorSubject, 512, "restore_job_snapshot_invalid");
            RequireOptionalText(snapshot.CorrelationId, 256, "restore_job_snapshot_invalid");
            if (snapshot.CreatedAtUtc.Offset != TimeSpan.Zero)
                throw new FormatException("restore_job_snapshot_invalid");
        }

        internal static void RequireOpaque(string value, string errorCode)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                Path.IsPathRooted(value) ||
                value.IndexOf('/') >= 0 ||
                value.IndexOf('\\') >= 0 ||
                value.IndexOf(':') >= 0 ||
                value.Contains("..") ||
                value == ".")
            {
                throw new FormatException(errorCode);
            }
        }

        private static void RequireText(string? value, int maximumLength, string errorCode)
        {
            if (string.IsNullOrWhiteSpace(value) || value!.Length > maximumLength || HasControl(value))
                throw new FormatException(errorCode);
        }

        private static void RequireOptionalText(string? value, int maximumLength, string errorCode)
        {
            if (value != null && (value.Length > maximumLength || HasControl(value)))
                throw new FormatException(errorCode);
        }

        private static bool HasControl(string value) => value.Any(char.IsControl);

        private static bool IsHex(char value) =>
            (value >= '0' && value <= '9') ||
            (value >= 'a' && value <= 'f') ||
            (value >= 'A' && value <= 'F');
    }
}
