using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class PlacePrefabUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public PlacePrefabUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(PlacePrefabRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            BlockPrefabSubmission.RequireConfirmations(request);
            BlockPrefabSubmission.RequirePrefab(catalog, request.CatalogVersion, request.PrefabResourceId);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.PlacePrefab,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Place approved prefab",
                true,
                BlockPrefabSubmission.Target(
                    request.PrefabResourceId,
                    null,
                    request.AnchorX,
                    request.AnchorY,
                    request.AnchorZ,
                    request.Rotation,
                    request.KnownBounds),
                request.RequestedAtUtc));
        }
    }
}
