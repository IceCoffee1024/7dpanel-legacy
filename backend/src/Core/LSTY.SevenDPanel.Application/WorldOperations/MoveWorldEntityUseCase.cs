using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class MoveWorldEntityUseCase
    {
        private readonly IWorldOperationJobBridge bridge;

        public MoveWorldEntityUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(MoveWorldEntityRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.MoveEntity,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Move world entity",
                false,
                new WorldEntityOperationTarget(
                    request.TargetId,
                    request.EntityId,
                    request.TargetId,
                    request.EntityTypeResourceId,
                    request.OwnerStableIdentity,
                    request.ObservedPosition.X,
                    request.ObservedPosition.Y,
                    request.ObservedPosition.Z,
                    request.Destination.X,
                    request.Destination.Y,
                    request.Destination.Z),
                request.RequestedAtUtc));
        }
    }
}
