using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public abstract class RegionOperationRequest
    {
        protected RegionOperationRequest(
            string actorSubject,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            WorldRegion region,
            string correlationId,
            bool confirmed,
            bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            Region = region ?? throw new ArgumentNullException(nameof(region));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            StrongConfirmed = strongConfirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public WorldRegion Region { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public bool StrongConfirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class CopyRegionRequest : RegionOperationRequest
    {
        public CopyRegionRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldRegion region, string correlationId, bool confirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, region, correlationId,
                confirmed, false, requestedAtUtc) { }
    }

    public sealed class FillRegionRequest : RegionOperationRequest
    {
        public FillRegionRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldRegion region, string catalogVersion, string blockInternalName,
            string correlationId, bool confirmed, bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, region, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            CatalogVersion = MapWorldOperationValidation.RequireText(catalogVersion, nameof(catalogVersion));
            BlockInternalName = MapWorldOperationValidation.RequireText(blockInternalName, nameof(blockInternalName));
        }

        public string CatalogVersion { get; }
        public string BlockInternalName { get; }
    }

    public sealed class ClearRegionRequest : RegionOperationRequest
    {
        public ClearRegionRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldRegion region, string correlationId, bool confirmed, bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, region, correlationId,
                confirmed, strongConfirmed, requestedAtUtc) { }
    }

    public sealed class PasteRegionRequest : RegionOperationRequest
    {
        public PasteRegionRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldRegion region, string sourceChangeSetId, string correlationId, bool confirmed,
            bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, region, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            SourceChangeSetId = MapWorldOperationValidation.RequireText(
                sourceChangeSetId,
                nameof(sourceChangeSetId));
        }

        public string SourceChangeSetId { get; }
    }

    public sealed class CopyRegionUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        public CopyRegionUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        public WorldOperationReceipt Execute(CopyRegionRequest request) =>
            RegionOperationSubmission.Enqueue(bridge, request, WorldOperationKind.CopyRegion, null, null, false);
    }

    public sealed class FillRegionUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public FillRegionUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(FillRegionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var snapshot = catalog.Read();
            if (snapshot.CatalogVersion != request.CatalogVersion ||
                !snapshot.BlockInternalNames.Contains(request.BlockInternalName, StringComparer.Ordinal))
            {
                throw new WorldOperationConflictException("world_block_catalog_changed");
            }
            return RegionOperationSubmission.Enqueue(
                bridge, request, WorldOperationKind.FillRegion, null, request.BlockInternalName, true);
        }
    }

    public sealed class ClearRegionUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        public ClearRegionUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        public WorldOperationReceipt Execute(ClearRegionRequest request) =>
            RegionOperationSubmission.Enqueue(bridge, request, WorldOperationKind.ClearRegion, null, null, true);
    }

    public sealed class PasteRegionUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldChangeSetMetadataStore changeSets;

        public PasteRegionUseCase(
            IWorldOperationJobBridge bridge,
            IWorldChangeSetMetadataStore changeSets)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
        }

        public WorldOperationReceipt Execute(PasteRegionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var source = changeSets.Read(request.SourceChangeSetId);
            if (!string.Equals(source.WorldId, request.WorldId, StringComparison.Ordinal))
                throw new WorldOperationConflictException("world_change_set_world_mismatch");
            return RegionOperationSubmission.Enqueue(
                bridge, request, WorldOperationKind.PasteRegion, request.SourceChangeSetId, null, true);
        }
    }

    internal static class RegionOperationSubmission
    {
        internal static WorldOperationReceipt Enqueue(
            IWorldOperationJobBridge bridge,
            RegionOperationRequest request,
            WorldOperationKind kind,
            string? sourceChangeSetId,
            string? blockInternalName,
            bool strongConfirmationRequired)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            if (strongConfirmationRequired && !request.StrongConfirmed)
                throw new WorldOperationStrongConfirmationRequiredException();
            var region = request.Region;
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                kind,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                kind == WorldOperationKind.CopyRegion
                    ? "Copy world region"
                    : kind == WorldOperationKind.FillRegion
                        ? "Fill world region"
                        : kind == WorldOperationKind.ClearRegion
                            ? "Clear world region"
                            : "Paste world region",
                kind != WorldOperationKind.CopyRegion,
                new WorldRegionOperationTarget(
                    region.MinimumX, region.MinimumY, region.MinimumZ,
                    region.MaximumX, region.MaximumY, region.MaximumZ,
                    sourceChangeSetId, blockInternalName),
                request.RequestedAtUtc));
        }
    }
}
