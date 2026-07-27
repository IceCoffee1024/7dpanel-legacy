using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IClearInventoryOperationStore
    {
        PlayerActionOperation CreatePending(ClearInventoryPendingIntent intent);

        bool TryStart(string operationId, DateTimeOffset startedAtUtc);

        bool TryComplete(ClearInventoryOperationCompletion completion);
    }

    public sealed class ClearInventoryPendingIntent
    {
        public ClearInventoryPendingIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string correlationId,
            DateTimeOffset createdAtUtc,
            PlayerItemRemovalScope removalScope,
            bool dangerConfirmed)
        {
            if (removalScope != PlayerItemRemovalScope.BagOnly)
                throw new ArgumentOutOfRangeException(nameof(removalScope));
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            CorrelationId = PlayerEvidenceValidation.RequireText(correlationId, nameof(correlationId));
            CreatedAtUtc = PlayerEvidenceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            RemovalScope = removalScope;
            DangerConfirmed = dangerConfirmed;
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public PlayerItemRemovalScope RemovalScope { get; }
        public bool DangerConfirmed { get; }

        public bool HasSameParameters(ClearInventoryPendingIntent other)
        {
            if (other == null) return false;
            return string.Equals(OperatorId, other.OperatorId, StringComparison.Ordinal)
                && string.Equals(ClientRequestKey, other.ClientRequestKey, StringComparison.Ordinal)
                && string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal)
                && RemovalScope == other.RemovalScope
                && DangerConfirmed == other.DangerConfirmed
                && string.Equals(Target.CrossplatformId, other.Target.CrossplatformId, StringComparison.Ordinal)
                && Target.EntityId == other.Target.EntityId
                && Target.OnlineObservedAtUtc == other.Target.OnlineObservedAtUtc
                && string.Equals(Target.WorldId, other.Target.WorldId, StringComparison.Ordinal);
        }
    }

    public sealed class ClearInventoryOperationCompletion
    {
        public ClearInventoryOperationCompletion(
            string operationId,
            ClearInventoryOperationStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId)
        {
            if (status == ClearInventoryOperationStatus.Pending ||
                !Enum.IsDefined(typeof(ClearInventoryOperationStatus), status))
            {
                throw new ArgumentException("A terminal clear-inventory status is required.", nameof(status));
            }

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            AfterInventorySnapshotId = RequireOptionalId(
                afterInventorySnapshotId,
                nameof(afterInventorySnapshotId));
            if (status == ClearInventoryOperationStatus.Succeeded &&
                (!BeforeInventorySnapshotId.HasValue || !AfterInventorySnapshotId.HasValue))
            {
                throw new ArgumentException("Successful inventory clearing requires exact before and after inventory snapshots.");
            }
        }

        public string OperationId { get; }
        public ClearInventoryOperationStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    public sealed class ClearInventoryIdempotencyConflictException : InvalidOperationException
    {
        public ClearInventoryIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different clear-inventory parameters.")
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
