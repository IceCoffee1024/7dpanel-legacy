using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class MoveOnlinePlayerUseCase
    {
        private readonly IWorldOperationJobBridge bridge;

        public MoveOnlinePlayerUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(MoveOnlinePlayerRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.MoveOnlinePlayer,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Move online player",
                false,
                new WorldEntityOperationTarget(
                    request.CrossplatformId,
                    request.EntityId,
                    request.CrossplatformId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    request.Destination.X,
                    request.Destination.Y,
                    request.Destination.Z),
                request.RequestedAtUtc));
        }
    }
}
