using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class CollectGameGarbageUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        public CollectGameGarbageUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(CollectGameGarbageRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject, WorldOperationKind.CollectGarbage,
                request.WorldId, request.WorldVersion, request.MapResourceVersion,
                request.CorrelationId, "Collect game garbage", false,
                new WorldMaintenanceOperationTarget(null), request.RequestedAtUtc));
        }
    }
}
