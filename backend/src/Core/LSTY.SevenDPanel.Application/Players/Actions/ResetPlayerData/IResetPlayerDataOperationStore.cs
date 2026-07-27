using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IResetPlayerDataOperationStore
    {
        PlayerActionOperation CreatePending(ResetPlayerDataPendingIntent intent);

        bool TryStart(string operationId, DateTimeOffset startedAtUtc);

        bool TryComplete(ResetPlayerDataOperationCompletion completion);
    }

    public sealed class ResetPlayerDataPendingIntent
    {
        public ResetPlayerDataPendingIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string correlationId,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            bool dangerConfirmed)
        {
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            CorrelationId = PlayerEvidenceValidation.RequireText(correlationId, nameof(correlationId));
            CreatedAtUtc = PlayerEvidenceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            BeforeSkillSnapshotId = RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            DangerConfirmed = dangerConfirmed;
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public bool DangerConfirmed { get; }
        public bool HasCompletePreparationEvidence =>
            BeforeInventorySnapshotId.HasValue && BeforeSkillSnapshotId.HasValue;

        public bool HasSameParameters(ResetPlayerDataPendingIntent other)
        {
            if (other == null) return false;
            return string.Equals(OperatorId, other.OperatorId, StringComparison.Ordinal)
                && string.Equals(ClientRequestKey, other.ClientRequestKey, StringComparison.Ordinal)
                && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
                && DangerConfirmed == other.DangerConfirmed
                && string.Equals(Target.CrossplatformId, other.Target.CrossplatformId, StringComparison.Ordinal)
                && Target.EntityId == other.Target.EntityId
                && Target.OnlineObservedAtUtc == other.Target.OnlineObservedAtUtc
                && string.Equals(Target.WorldId, other.Target.WorldId, StringComparison.Ordinal);
        }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ResetPlayerDataOperationCompletion
    {
        public ResetPlayerDataOperationCompletion(
            string operationId,
            ResetPlayerDataOperationStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId)
        {
            if (status == ResetPlayerDataOperationStatus.Pending ||
                !Enum.IsDefined(typeof(ResetPlayerDataOperationStatus), status))
            {
                throw new ArgumentException(
                    "A terminal reset-player-data status is required.",
                    nameof(status));
            }

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            BeforeSkillSnapshotId = RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            if (status == ResetPlayerDataOperationStatus.Succeeded &&
                (!BeforeInventorySnapshotId.HasValue || !BeforeSkillSnapshotId.HasValue))
            {
                throw new ArgumentException(
                    "A successful complete player reset requires exact inventory and skill evidence.");
            }
        }

        public string OperationId { get; }
        public ResetPlayerDataOperationStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ResetPlayerDataIdempotencyConflictException : InvalidOperationException
    {
        public ResetPlayerDataIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different reset-player-data parameters.")
        {
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(clientRequestKey, nameof(clientRequestKey));
            ExistingOperationId = PlayerEvidenceValidation.RequireText(
                existingOperationId,
                nameof(existingOperationId));
        }

        public string OperatorId { get; }
        public string ClientRequestKey { get; }
        public string ExistingOperationId { get; }
    }
}
