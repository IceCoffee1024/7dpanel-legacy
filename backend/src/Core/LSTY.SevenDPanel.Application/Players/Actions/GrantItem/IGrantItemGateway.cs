using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IGrantItemGateway
    {
        Task<GrantItemInventorySnapshot> CaptureInventorySnapshotAsync(
            GrantItemSnapshotCommand command,
            CancellationToken cancellationToken);

        Task<GrantItemGatewayResult> GrantAsync(
            GrantItemCommand command,
            Func<DateTimeOffset, bool> tryStart,
            CancellationToken cancellationToken);
    }

    public enum GrantItemSnapshotPhase
    {
        Before,
        After
    }

    public sealed class GrantItemSnapshotCommand
    {
        internal GrantItemSnapshotCommand(
            string operationId,
            PlayerTargetStamp target,
            string catalogVersion,
            GrantItemSnapshotPhase phase)
        {
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            CatalogVersion = PlayerEvidenceValidation.RequireText(
                catalogVersion,
                nameof(catalogVersion));
            PlayerEvidenceValidation.RequireDefined(phase, nameof(phase));
            Phase = phase;
        }

        public string OperationId { get; }
        public PlayerTargetStamp Target { get; }
        public string CatalogVersion { get; }
        public GrantItemSnapshotPhase Phase { get; }
    }

    public sealed class GrantItemInventorySnapshot
    {
        private readonly InventoryItemScalar[] items;

        public GrantItemInventorySnapshot(
            DateTimeOffset observedAtUtc,
            string gameVersion,
            string? catalogVersion,
            CatalogResolutionState catalogResolution,
            string fingerprint,
            IEnumerable<InventoryItemScalar> items)
        {
            PlayerEvidenceValidation.RequireDefined(catalogResolution, nameof(catalogResolution));
            ObservedAtUtc = PlayerEvidenceValidation.RequireUtc(
                observedAtUtc,
                nameof(observedAtUtc));
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
            CatalogVersion = PlayerEvidenceValidation.OptionalText(
                catalogVersion,
                nameof(catalogVersion));
            if (catalogResolution == CatalogResolutionState.Resolved && CatalogVersion == null)
            {
                throw new ArgumentException(
                    "A resolved inventory requires a catalog version.",
                    nameof(catalogVersion));
            }
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

    public sealed class GrantItemCommand
    {
        internal GrantItemCommand(
            string operationId,
            PlayerTargetStamp target,
            string catalogVersion,
            string resourceId,
            int numericId,
            string internalName,
            GameResourceKind itemKind,
            GameResourceVisibility visibility,
            bool hiddenItemConfirmed,
            int quantity,
            int? quality,
            int maxStack,
            bool hasQuality,
            string gameVersion)
        {
            if (numericId < 0) throw new ArgumentOutOfRangeException(nameof(numericId));
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            if (maxStack <= 0) throw new ArgumentOutOfRangeException(nameof(maxStack));

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            CatalogVersion = PlayerEvidenceValidation.RequireText(
                catalogVersion,
                nameof(catalogVersion));
            ResourceId = PlayerEvidenceValidation.RequireText(resourceId, nameof(resourceId));
            NumericId = numericId;
            InternalName = PlayerEvidenceValidation.RequireText(internalName, nameof(internalName));
            PlayerEvidenceValidation.RequireDefined(itemKind, nameof(itemKind));
            ItemKind = itemKind;
            PlayerEvidenceValidation.RequireDefined(visibility, nameof(visibility));
            if (visibility == GameResourceVisibility.Hidden && !hiddenItemConfirmed)
            {
                throw new ArgumentException(
                    "A hidden item requires explicit confirmation.",
                    nameof(hiddenItemConfirmed));
            }
            Visibility = visibility;
            HiddenItemConfirmed = hiddenItemConfirmed;
            Quantity = quantity;
            Quality = quality;
            MaxStack = maxStack;
            HasQuality = hasQuality;
            GameVersion = PlayerEvidenceValidation.RequireText(gameVersion, nameof(gameVersion));
        }

        public string OperationId { get; }
        public PlayerTargetStamp Target { get; }
        public string CatalogVersion { get; }
        public string ResourceId { get; }
        public int NumericId { get; }
        public string InternalName { get; }
        public GameResourceKind ItemKind { get; }
        public GameResourceVisibility Visibility { get; }
        public bool HiddenItemConfirmed { get; }
        public int Quantity { get; }
        public int? Quality { get; }
        public int MaxStack { get; }
        public bool HasQuality { get; }
        public string GameVersion { get; }
    }

    public enum GrantItemGatewayStatus
    {
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class GrantItemGatewayResult
    {
        private GrantItemGatewayResult(
            GrantItemGatewayStatus status,
            string? failureCode,
            int? actualQuantity)
        {
            PlayerEvidenceValidation.RequireDefined(status, nameof(status));
            if (actualQuantity < 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            Status = status;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            ActualQuantity = actualQuantity;
        }

        public GrantItemGatewayStatus Status { get; }
        public string? FailureCode { get; }
        public int? ActualQuantity { get; }

        public static GrantItemGatewayResult Succeeded(int actualQuantity)
        {
            if (actualQuantity <= 0) throw new ArgumentOutOfRangeException(nameof(actualQuantity));
            return new GrantItemGatewayResult(
                GrantItemGatewayStatus.Succeeded,
                null,
                actualQuantity);
        }

        public static GrantItemGatewayResult Rejected(string failureCode) =>
            new GrantItemGatewayResult(
                GrantItemGatewayStatus.Rejected,
                failureCode,
                null);

        public static GrantItemGatewayResult Failed(string failureCode) =>
            new GrantItemGatewayResult(
                GrantItemGatewayStatus.Failed,
                failureCode,
                null);

        public static GrantItemGatewayResult Cancelled() =>
            new GrantItemGatewayResult(
                GrantItemGatewayStatus.Cancelled,
                null,
                null);

        public static GrantItemGatewayResult ResultUnknown(string failureCode) =>
            new GrantItemGatewayResult(
                GrantItemGatewayStatus.ResultUnknown,
                failureCode,
                null);
    }
}
