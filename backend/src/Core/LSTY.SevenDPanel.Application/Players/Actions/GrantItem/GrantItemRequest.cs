using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GrantItemRequest
    {
        public GrantItemRequest(
            string operatorId,
            PlayerTargetStamp target,
            string catalogVersion,
            string resourceId,
            int quantity,
            int? quality,
            bool hiddenItemConfirmed,
            string clientRequestKey,
            string? correlationId)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            CatalogVersion = PlayerEvidenceValidation.RequireText(
                catalogVersion,
                nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            Quantity = quantity;
            Quality = quality;
            HiddenItemConfirmed = hiddenItemConfirmed;
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            CorrelationId = PlayerEvidenceValidation.OptionalText(
                correlationId,
                nameof(correlationId));
        }

        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string CatalogVersion { get; }
        public string ResourceId { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public bool HiddenItemConfirmed { get; }
        public string ClientRequestKey { get; }
        public string? CorrelationId { get; }
    }

    public sealed class GrantItemResult
    {
        internal GrantItemResult(
            string operationId,
            PlayerActionStatus status,
            string? failureCode,
            int? actualQuantity,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            bool reused,
            bool terminalStatePersisted)
        {
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            PlayerEvidenceValidation.RequireDefined(status, nameof(status));
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            if (beforeInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeInventorySnapshotId));
            if (afterInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(afterInventorySnapshotId));

            Status = status;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            ActualQuantity = actualQuantity;
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            AfterInventorySnapshotId = afterInventorySnapshotId;
            Reused = reused;
            TerminalStatePersisted = terminalStatePersisted;
        }

        public string OperationId { get; }
        public PlayerActionStatus Status { get; }
        public string? FailureCode { get; }
        public int? ActualQuantity { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool Reused { get; }
        public bool TerminalStatePersisted { get; }
    }

    public sealed class GrantItemRequestRejectedException : InvalidOperationException
    {
        public GrantItemRequestRejectedException(string code)
            : base("The grant item request was rejected before an operation was created.")
        {
            Code = PlayerEvidenceValidation.RequireText(code, nameof(code));
        }

        public string Code { get; }
    }

    public static class GrantItemFailureCodes
    {
        public const string CatalogUnavailable = "CatalogUnavailable";
        public const string CatalogChanged = "CatalogChanged";
        public const string ResourceNotFound = "ResourceNotFound";
        public const string ResourceNotGrantable = "ResourceNotGrantable";
        public const string HiddenItemConfirmationRequired = "HiddenItemConfirmationRequired";
        public const string QuantityLimitExceeded = "QuantityLimitExceeded";
        public const string StackLimitExceeded = "StackLimitExceeded";
        public const string QualityUnsupported = "QualityUnsupported";
        public const string PlayerNotOnline = "PlayerNotOnline";
        public const string TargetChanged = "TargetChanged";
        public const string VersionUnsupported = "VersionUnsupported";
        public const string InsufficientSpace = "InsufficientSpace";
        public const string OperationStartConflict = "OperationStartConflict";
        public const string SnapshotUnavailable = "SnapshotUnavailable";
        public const string GatewayFailure = "GatewayFailure";
        public const string ResultUnknown = "ResultUnknown";
    }
}
