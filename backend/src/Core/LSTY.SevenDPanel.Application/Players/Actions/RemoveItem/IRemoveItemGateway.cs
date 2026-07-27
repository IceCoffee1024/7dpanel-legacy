using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IRemoveItemGateway
    {
        Task<RemoveItemGatewayResult> RemoveAsync(
            RemoveItemCommand command,
            CancellationToken cancellationToken);
    }

    public sealed class RemoveItemCommand
    {
        public RemoveItemCommand(
            PlayerTargetStamp target,
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

            Target = target ?? throw new ArgumentNullException(nameof(target));
            CatalogVersion = PlayerEvidenceValidation.RequireText(catalogVersion, nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            InternalName = PlayerEvidenceValidation.RequireText(internalName, nameof(internalName));
            ItemKind = itemKind;
            Quantity = quantity;
            Quality = quality;
            RemovalScope = removalScope;
            RemovalMode = removalMode;
        }

        public PlayerTargetStamp Target { get; }
        public string CatalogVersion { get; }
        public string ResourceId { get; }
        public string InternalName { get; }
        public GameResourceKind ItemKind { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public PlayerItemRemovalScope RemovalScope { get; }
        public PlayerItemRemovalMode RemovalMode { get; }
    }

    public enum RemoveItemGatewayStatus
    {
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class RemoveItemInventorySnapshot
    {
        private readonly InventoryItemScalar[] items;

        public RemoveItemInventorySnapshot(
            DateTimeOffset observedAtUtc,
            string gameVersion,
            string? catalogVersion,
            CatalogResolutionState catalogResolution,
            string fingerprint,
            IEnumerable<InventoryItemScalar> items)
        {
            PlayerEvidenceValidation.RequireDefined(catalogResolution, nameof(catalogResolution));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
            CatalogVersion = PlayerEvidenceValidation.OptionalText(catalogVersion, nameof(catalogVersion));
            if (catalogResolution == CatalogResolutionState.Resolved && CatalogVersion == null)
                throw new ArgumentException("A resolved inventory requires a catalog version.", nameof(catalogVersion));
            CatalogResolution = catalogResolution;
            Fingerprint = PlayerEvidenceValidation.RequireText(fingerprint, nameof(fingerprint));
            this.items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
            if (this.items.Any(item => item == null))
                throw new ArgumentException("Inventory items cannot contain null.", nameof(items));
        }

        public DateTimeOffset ObservedAtUtc { get; }
        public string GameVersion { get; }
        public string? CatalogVersion { get; }
        public CatalogResolutionState CatalogResolution { get; }
        public string Fingerprint { get; }
        public IReadOnlyList<InventoryItemScalar> Items => Array.AsReadOnly(items);
    }

    public sealed class RemoveItemGatewayResult
    {
        private RemoveItemGatewayResult(
            RemoveItemGatewayStatus status,
            int? actualQuantity,
            string? failureCode,
            RemoveItemInventorySnapshot? beforeInventory,
            RemoveItemInventorySnapshot? afterInventory)
        {
            if (!Enum.IsDefined(typeof(RemoveItemGatewayStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            if (status == RemoveItemGatewayStatus.Succeeded)
            {
                if (!actualQuantity.HasValue || beforeInventory == null || afterInventory == null)
                    throw new ArgumentException("A successful removal requires exact before and after inventory snapshots.");
            }
            else if (actualQuantity.HasValue || beforeInventory != null || afterInventory != null)
            {
                throw new ArgumentException("Only a successful removal can contain quantity or snapshot evidence.");
            }

            Status = status;
            ActualQuantity = actualQuantity;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventory = beforeInventory;
            AfterInventory = afterInventory;
        }

        public RemoveItemGatewayStatus Status { get; }
        public int? ActualQuantity { get; }
        public string? FailureCode { get; }
        public RemoveItemInventorySnapshot? BeforeInventory { get; }
        public RemoveItemInventorySnapshot? AfterInventory { get; }

        public static RemoveItemGatewayResult Succeeded(
            int actualQuantity,
            RemoveItemInventorySnapshot beforeInventory,
            RemoveItemInventorySnapshot afterInventory) =>
            new RemoveItemGatewayResult(
                RemoveItemGatewayStatus.Succeeded,
                actualQuantity,
                null,
                beforeInventory,
                afterInventory);

        public static RemoveItemGatewayResult Terminal(
            RemoveItemGatewayStatus status,
            string failureCode)
        {
            if (status == RemoveItemGatewayStatus.Succeeded)
                throw new ArgumentException("Use the successful result factory.", nameof(status));
            return new RemoveItemGatewayResult(status, null, failureCode, null, null);
        }
    }
}
