using System;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed record RestoreResultReceipt(
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

        public static RestoreResultReceipt FromMarker(
            PendingRestoreMarker marker,
            RestoreExecutionStage stage)
        {
            if (marker == null) throw new ArgumentNullException(nameof(marker));
            marker.Validate();
            var receipt = new RestoreResultReceipt(
                CurrentVersion,
                marker.ArtifactId,
                marker.BackupKind,
                marker.BackupRootId,
                marker.RelativeResourceId,
                marker.Sha256,
                marker.JobSnapshot,
                stage);
            receipt.Validate();
            return receipt;
        }

        internal void Validate() => RestoreStateValidation.ValidateCommon(
            Version,
            ArtifactId,
            BackupKind,
            BackupRootId,
            RelativeResourceId,
            Sha256,
            JobSnapshot,
            Stage);

        internal bool HasSameIdentity(PendingRestoreMarker marker) =>
            marker != null &&
            ArtifactId == marker.ArtifactId &&
            BackupKind == marker.BackupKind &&
            string.Equals(BackupRootId, marker.BackupRootId, StringComparison.Ordinal) &&
            string.Equals(RelativeResourceId, marker.RelativeResourceId, StringComparison.Ordinal) &&
            string.Equals(Sha256, marker.Sha256, StringComparison.OrdinalIgnoreCase) &&
            JobSnapshot == marker.JobSnapshot;

        internal bool HasSameIdentity(RestoreResultReceipt other) =>
            other != null &&
            ArtifactId == other.ArtifactId &&
            BackupKind == other.BackupKind &&
            string.Equals(BackupRootId, other.BackupRootId, StringComparison.Ordinal) &&
            string.Equals(RelativeResourceId, other.RelativeResourceId, StringComparison.Ordinal) &&
            string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase) &&
            JobSnapshot == other.JobSnapshot;
    }
}
