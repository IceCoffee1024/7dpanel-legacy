using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class SetBlockUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public SetBlockUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(SetBlockRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            BlockPrefabSubmission.RequireConfirmations(request);
            var snapshot = catalog.Read();
            if (snapshot.CatalogVersion != request.CatalogVersion ||
                !snapshot.BlockInternalNames.Contains(request.BlockInternalName, StringComparer.Ordinal))
            {
                throw new WorldOperationConflictException("world_block_catalog_changed");
            }
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.SetBlock,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Set approved world block",
                true,
                new WorldBlockOperationTarget(
                    request.X,
                    request.Y,
                    request.Z,
                    request.BlockInternalName,
                    request.Rotation,
                    request.Shape?.ToString()),
                request.RequestedAtUtc));
        }
    }

    internal static class BlockPrefabSubmission
    {
        internal static void RequireConfirmations(BlockPrefabOperationRequest request)
        {
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            if (!request.StrongConfirmed)
                throw new WorldOperationStrongConfirmationRequiredException();
        }

        internal static void RequirePrefab(
            IWorldToolCatalog catalog,
            string catalogVersion,
            string prefabResourceId)
        {
            var snapshot = catalog.Read();
            if (snapshot.CatalogVersion != catalogVersion ||
                !snapshot.PrefabResourceIds.Contains(prefabResourceId, StringComparer.Ordinal))
            {
                throw new WorldOperationConflictException("world_prefab_catalog_changed");
            }
        }

        internal static WorldPrefabOperationTarget Target(
            string prefabResourceId,
            string? prefabInstanceId,
            int anchorX,
            int anchorY,
            int anchorZ,
            int rotation,
            WorldRegion bounds) =>
            new WorldPrefabOperationTarget(
                prefabResourceId,
                prefabInstanceId,
                anchorX,
                anchorY,
                anchorZ,
                rotation,
                checked((int)bounds.Minimum.X),
                checked((int)bounds.Minimum.Y),
                checked((int)bounds.Minimum.Z),
                checked((int)bounds.Maximum.X),
                checked((int)bounds.Maximum.Y),
                checked((int)bounds.Maximum.Z));
    }
}
