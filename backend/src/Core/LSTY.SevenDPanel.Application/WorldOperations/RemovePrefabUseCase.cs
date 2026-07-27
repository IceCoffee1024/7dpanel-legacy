using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class RemovePrefabUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public RemovePrefabUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(RemovePrefabRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            BlockPrefabSubmission.RequireConfirmations(request);
            BlockPrefabSubmission.RequirePrefab(catalog, request.CatalogVersion, request.PrefabResourceId);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.RemovePrefab,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Remove approved prefab",
                true,
                BlockPrefabSubmission.Target(
                    request.PrefabResourceId,
                    request.PrefabInstanceId,
                    request.AnchorX,
                    request.AnchorY,
                    request.AnchorZ,
                    request.Rotation,
                    request.KnownBounds),
                request.RequestedAtUtc));
        }
    }
}
