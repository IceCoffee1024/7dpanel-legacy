using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IRemoveItemOperationStore
    {
        PlayerActionOperation CreatePending(RemoveItemPendingIntent intent);

        bool TryStart(string operationId, DateTimeOffset startedAtUtc);

        bool TryComplete(RemoveItemOperationCompletion completion);
    }

    public sealed class RemoveItemPendingIntent
    {
        public RemoveItemPendingIntent(
            string operationId,
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string? correlationId,
            DateTimeOffset createdAtUtc,
            string catalogVersion,
            string resourceId,
            string internalName,
            GameResourceKind itemKind,
            int quantity,
            int? quality,
            PlayerItemRemovalScope removalScope,
            PlayerItemRemovalMode removalMode)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            PlayerEvidenceValidation.RequireDefined(itemKind, nameof(itemKind));
            PlayerEvidenceValidation.RequireDefined(removalScope, nameof(removalScope));
            PlayerEvidenceValidation.RequireDefined(removalMode, nameof(removalMode));
            if (removalScope != PlayerItemRemovalScope.BagOnly)
                throw new ArgumentOutOfRangeException(nameof(removalScope));

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
            CatalogVersion = PlayerEvidenceValidation.RequireText(catalogVersion, nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            InternalName = PlayerEvidenceValidation.RequireText(internalName, nameof(internalName));
            ItemKind = itemKind;
            Quantity = quantity;
            Quality = quality;
            RemovalScope = removalScope;
            RemovalMode = removalMode;
        }

        public string OperationId { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public string CatalogVersion { get; }
        public string ResourceId { get; }
        public string InternalName { get; }
        public GameResourceKind ItemKind { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public PlayerItemRemovalScope RemovalScope { get; }
        public PlayerItemRemovalMode RemovalMode { get; }

        public bool HasSameRequest(RemoveItemPendingIntent other)
        {
            if (other == null) return false;
            return string.Equals(OperatorId, other.OperatorId, StringComparison.Ordinal) &&
                   string.Equals(ClientRequestKey, other.ClientRequestKey, StringComparison.Ordinal) &&
                   Target == other.Target &&
                   string.Equals(CatalogVersion, other.CatalogVersion, StringComparison.Ordinal) &&
                   string.Equals(ResourceId, other.ResourceId, StringComparison.Ordinal) &&
                   string.Equals(InternalName, other.InternalName, StringComparison.Ordinal) &&
                   ItemKind == other.ItemKind &&
                   Quantity == other.Quantity &&
                   Quality == other.Quality &&
                   RemovalScope == other.RemovalScope &&
                   RemovalMode == other.RemovalMode &&
                   string.Equals(CorrelationId, other.CorrelationId, StringComparison.Ordinal);
        }
    }

    public sealed class RemoveItemOperationCompletion
    {
        public RemoveItemOperationCompletion(
            string operationId,
            PlayerActionStatus status,
            DateTimeOffset completedAtUtc,
            string? failureCode,
            int? actualQuantity,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId)
        {
            PlayerEvidenceValidation.RequireDefined(status, nameof(status));
            if (status == PlayerActionStatus.Pending)
                throw new ArgumentException("A terminal player action status is required.", nameof(status));
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            if (beforeInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeInventorySnapshotId));
            if (afterInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(afterInventorySnapshotId));

            if (status == PlayerActionStatus.Succeeded)
            {
                if (!actualQuantity.HasValue || !beforeInventorySnapshotId.HasValue ||
                    !afterInventorySnapshotId.HasValue)
                {
                    throw new ArgumentException(
                        "A successful removal requires its actual quantity and exact inventory snapshot links.",
                        nameof(status));
                }
            }
            else if (actualQuantity.HasValue || beforeInventorySnapshotId.HasValue ||
                     afterInventorySnapshotId.HasValue)
            {
                throw new ArgumentException(
                    "Only a successful removal can carry an actual quantity and confirmed snapshot links.",
                    nameof(status));
            }

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            ActualQuantity = actualQuantity;
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            AfterInventorySnapshotId = afterInventorySnapshotId;
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public int? ActualQuantity { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
    }

    public sealed class RemoveItemIdempotencyConflictException : InvalidOperationException
    {
        public RemoveItemIdempotencyConflictException(
            string operatorId,
            string clientRequestKey,
            string existingOperationId)
            : base("The client request key is already associated with different remove-item parameters.")
        {
            OperatorId = operatorId;
            ClientRequestKey = clientRequestKey;
            ExistingOperationId = existingOperationId;
        }

        public string OperatorId { get; }
        public string ClientRequestKey { get; }
        public string ExistingOperationId { get; }
    }
}
