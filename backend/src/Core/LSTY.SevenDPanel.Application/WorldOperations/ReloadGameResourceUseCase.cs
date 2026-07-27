using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class ReloadGameResourceUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        public ReloadGameResourceUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(ReloadGameResourceRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MaintenanceSubmission.RequireStrong(request);
            var kind = request.ResourceKind switch
            {
                WorldReloadResourceKind.Blocks => WorldOperationKind.ReloadBlocks,
                WorldReloadResourceKind.Items => WorldOperationKind.ReloadItems,
                WorldReloadResourceKind.EntityClasses => WorldOperationKind.ReloadEntityClasses,
                WorldReloadResourceKind.Prefabs => WorldOperationKind.ReloadPrefabs,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject, kind, request.WorldId, request.WorldVersion,
                request.MapResourceVersion, request.CorrelationId,
                "Reload approved game resource catalog", false,
                new WorldMaintenanceOperationTarget(null), request.RequestedAtUtc));
        }
    }
}
