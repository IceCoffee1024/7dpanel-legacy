using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class DeleteLandClaimUseCase
    {
        private readonly IWorldOperationJobBridge bridge;

        public DeleteLandClaimUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(DeleteLandClaimRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject,
                WorldOperationKind.DeleteLandClaim,
                request.WorldId,
                request.WorldVersion,
                request.MapResourceVersion,
                request.CorrelationId,
                "Delete land claim",
                false,
                new WorldEntityOperationTarget(
                    request.ClaimId,
                    null,
                    request.ClaimId,
                    null,
                    request.OwnerStableIdentity,
                    request.Center.X,
                    request.Center.Y,
                    request.Center.Z,
                    null,
                    null,
                    null),
                request.RequestedAtUtc));
        }
    }
}
