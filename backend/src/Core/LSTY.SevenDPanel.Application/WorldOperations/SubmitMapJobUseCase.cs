using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class SubmitMapJobUseCase
    {
        private readonly IWorldOperationJobBridge bridge;

        public SubmitMapJobUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(SubmitMapJobRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            if (request.Kind == MapJobKind.RenderFull && !request.StrongConfirmed)
                throw new WorldOperationStrongConfirmationRequiredException();

            var kind = request.Kind switch
            {
                MapJobKind.RefreshResources => WorldOperationKind.RefreshMapResources,
                MapJobKind.RenderExplored => WorldOperationKind.RenderExploredMap,
                MapJobKind.RenderFull => WorldOperationKind.RenderFullMap,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            };
            var bounds = request.Bounds;
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                kind,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                request.Kind == MapJobKind.RefreshResources
                    ? "Refresh map resources"
                    : request.Kind == MapJobKind.RenderExplored
                        ? "Render explored map"
                        : "Render full map",
                false,
                new WorldMapOperationTarget(
                    bounds?.MinimumX,
                    bounds?.MinimumZ,
                    bounds?.MaximumX,
                    bounds?.MaximumZ),
                request.RequestedAtUtc));
        }
    }
}
