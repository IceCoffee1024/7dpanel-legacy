using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class SpawnWorldEntityUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public SpawnWorldEntityUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(SpawnWorldEntityRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MaintenanceSubmission.RequireStrong(request);
            MaintenanceSubmission.RequireEntityType(
                catalog, request.CatalogVersion, request.EntityTypeResourceId);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject, WorldOperationKind.SpawnEntity,
                request.WorldId, request.WorldVersion, request.MapResourceVersion,
                request.CorrelationId, "Spawn approved entities", false,
                new WorldEntityOperationTarget(
                    "spawn-" + request.CorrelationId, null, null, request.EntityTypeResourceId, null,
                    null, null, null, request.Center.X, request.Center.Y, request.Center.Z,
                    request.Quantity, request.Radius, null),
                request.RequestedAtUtc));
        }
    }

    public sealed class DeleteWorldEntityUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        private readonly IWorldToolCatalog catalog;

        public DeleteWorldEntityUseCase(IWorldOperationJobBridge bridge, IWorldToolCatalog catalog)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldOperationReceipt Execute(DeleteWorldEntityRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MaintenanceSubmission.RequireStrong(request);
            MaintenanceSubmission.RequireEntityType(
                catalog, request.CatalogVersion, request.EntityTypeResourceId);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject, WorldOperationKind.DeleteEntity,
                request.WorldId, request.WorldVersion, request.MapResourceVersion,
                request.CorrelationId, "Delete fixed world entity", false,
                new WorldEntityOperationTarget(
                    request.TargetId, request.EntityId, request.TargetId,
                    request.EntityTypeResourceId, request.OwnerStableIdentity,
                    request.ObservedPosition.X, request.ObservedPosition.Y, request.ObservedPosition.Z,
                    null, null, null),
                request.RequestedAtUtc));
        }
    }

    public sealed class CleanupWorldEntitiesUseCase
    {
        private readonly IWorldOperationJobBridge bridge;
        public CleanupWorldEntitiesUseCase(IWorldOperationJobBridge bridge) =>
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));

        public WorldOperationReceipt Execute(CleanupWorldEntitiesRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            MaintenanceSubmission.RequireStrong(request);
            return bridge.Enqueue(new WorldOperationIntent(
                request.ActorSubject, WorldOperationKind.CleanupEntities,
                request.WorldId, request.WorldVersion, request.MapResourceVersion,
                request.CorrelationId, "Cleanup approved entity category", false,
                new WorldEntityOperationTarget(
                    "cleanup-" + request.Category, null, null, null, null,
                    null, null, null, request.Center.X, request.Center.Y, request.Center.Z,
                    request.MaximumCount, request.Radius, request.Category.ToString()),
                request.RequestedAtUtc));
        }
    }

    internal static class MaintenanceSubmission
    {
        internal static void RequireStrong(MaintenanceOperationRequest request)
        {
            MapWorldOperationValidation.RequireConfirmation(request.Confirmed);
            if (!request.StrongConfirmed)
                throw new WorldOperationStrongConfirmationRequiredException();
        }

        internal static void RequireEntityType(
            IWorldToolCatalog catalog,
            string catalogVersion,
            string entityTypeResourceId)
        {
            var snapshot = catalog.Read();
            if (snapshot.CatalogVersion != catalogVersion ||
                !snapshot.EntityTypeResourceIds.Contains(entityTypeResourceId, StringComparer.Ordinal))
            {
                throw new WorldOperationConflictException("world_entity_catalog_changed");
            }
        }
    }
}
