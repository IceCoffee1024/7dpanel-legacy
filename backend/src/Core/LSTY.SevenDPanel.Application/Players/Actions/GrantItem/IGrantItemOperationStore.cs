using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IGrantItemOperationStore
    {
        PlayerActionOperation CreatePending(GrantItemPendingIntent intent);

        bool TryStart(string operationId, DateTimeOffset startedAtUtc);

        bool TryComplete(GrantItemOperationCompletion completion);
    }

    public sealed class GrantItemPendingIntent
    {
        public GrantItemPendingIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            string catalogVersion,
            string resourceId,
            string gameVersion,
            int numericId,
            string internalName,
            GameResourceKind itemKind,
            int quantity,
            int? quality,
            bool hiddenItemConfirmed)
        {
            if (numericId < 0) throw new ArgumentOutOfRangeException(nameof(numericId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            CorrelationId = PlayerEvidenceValidation.OptionalText(
                correlationId,
                nameof(correlationId));
            CreatedAtUtc = PlayerEvidenceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            CatalogVersion = PlayerEvidenceValidation.RequireText(
                catalogVersion,
                nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
            NumericId = numericId;
            InternalName = PlayerEvidenceValidation.RequireText(internalName, nameof(internalName));
            PlayerEvidenceValidation.RequireDefined(itemKind, nameof(itemKind));
            ItemKind = itemKind;
            Quantity = quantity;
            Quality = quality;
            HiddenItemConfirmed = hiddenItemConfirmed;
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public string CatalogVersion { get; }
        public string ResourceId { get; }
        public string GameVersion { get; }
        public int NumericId { get; }
        public string InternalName { get; }
        public GameResourceKind ItemKind { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public bool HiddenItemConfirmed { get; }
    }

    public sealed class GrantItemOperationCompletion
    {
        public GrantItemOperationCompletion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            int? actualQuantity)
        {
            if (status == PlayerActionStatus.Pending ||
                !Enum.IsDefined(typeof(PlayerActionStatus), status))
            {
                throw new ArgumentException(
                    "A terminal player action status is required.",
                    nameof(status));
            }
            if (beforeInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeInventorySnapshotId));
            if (afterInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(afterInventorySnapshotId));
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(
                completedAtUtc,
                nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            AfterInventorySnapshotId = afterInventorySnapshotId;
            ActualQuantity = actualQuantity;
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public int? ActualQuantity { get; }
    }

    public sealed class GrantItemIdempotencyConflictException : InvalidOperationException
    {
        public GrantItemIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different grant item parameters.")
        {
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            ExistingOperationId = PlayerEvidenceValidation.RequireText(
                existingOperationId,
                nameof(existingOperationId));
        }

        public string OperatorId { get; }
        public string ClientRequestKey { get; }
        public string ExistingOperationId { get; }
    }
}
