using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IResetSkillsOperationStore
    {
        PlayerActionOperation CreatePending(ResetSkillsPendingIntent intent);

        bool TryStart(string operationId, DateTimeOffset startedAtUtc);

        bool TryComplete(ResetSkillsOperationCompletion completion);
    }

    public sealed class ResetSkillsPendingIntent
    {
        public ResetSkillsPendingIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string correlationId,
            DateTimeOffset createdAtUtc,
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
            DangerConfirmed = dangerConfirmed;
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public bool DangerConfirmed { get; }

        public bool HasSameParameters(ResetSkillsPendingIntent other)
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
    }

    public sealed class ResetSkillsOperationCompletion
    {
        public ResetSkillsOperationCompletion(
            string operationId,
            ResetSkillsOperationStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId)
        {
            if (status == ResetSkillsOperationStatus.Pending ||
                !Enum.IsDefined(typeof(ResetSkillsOperationStatus), status))
            {
                throw new ArgumentException("A terminal reset-skills status is required.", nameof(status));
            }

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeSkillSnapshotId = RequireOptionalId(beforeSkillSnapshotId, nameof(beforeSkillSnapshotId));
            AfterSkillSnapshotId = RequireOptionalId(afterSkillSnapshotId, nameof(afterSkillSnapshotId));
            if (status == ResetSkillsOperationStatus.Succeeded &&
                (!BeforeSkillSnapshotId.HasValue || !AfterSkillSnapshotId.HasValue))
            {
                throw new ArgumentException("Successful skill reset requires exact before and after skill snapshots.");
            }
        }

        public string OperationId { get; }
        public ResetSkillsOperationStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ResetSkillsIdempotencyConflictException : InvalidOperationException
    {
        public ResetSkillsIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different reset-skills parameters.")
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
