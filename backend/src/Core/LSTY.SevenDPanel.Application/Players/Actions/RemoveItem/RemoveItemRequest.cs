using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class RemoveItemRequest
    {
        public RemoveItemRequest(
            string operatorId,
            PlayerTargetStamp target,
            string catalogVersion,
            string resourceId,
            int quantity,
            int? quality,
            PlayerItemRemovalScope removalScope = PlayerItemRemovalScope.BagOnly,
            PlayerItemRemovalMode removalMode = PlayerItemRemovalMode.Exact,
            string clientRequestKey = "",
            string? correlationId = null)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (quality < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            PlayerEvidenceValidation.RequireDefined(removalScope, nameof(removalScope));
            PlayerEvidenceValidation.RequireDefined(removalMode, nameof(removalMode));
            if (removalScope != PlayerItemRemovalScope.BagOnly)
                throw new ArgumentOutOfRangeException(nameof(removalScope));

            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            CatalogVersion = PlayerEvidenceValidation.RequireText(catalogVersion, nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            Quantity = quantity;
            Quality = quality;
            RemovalScope = removalScope;
            RemovalMode = removalMode;
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
        public PlayerItemRemovalScope RemovalScope { get; }
        public PlayerItemRemovalMode RemovalMode { get; }
        public string ClientRequestKey { get; }
        public string? CorrelationId { get; }
    }
}
