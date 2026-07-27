using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public enum WorldEntityCategory
    {
        Animal,
        Hostile,
        Vehicle,
        Drone,
        DroppedItem
    }

    public enum WorldReloadResourceKind
    {
        Blocks,
        Items,
        EntityClasses,
        Prefabs
    }

    public abstract class MaintenanceOperationRequest
    {
        protected MaintenanceOperationRequest(
            string actorSubject,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string correlationId,
            bool confirmed,
            bool strongConfirmed,
            DateTimeOffset requestedAtUtc)
        {
            ActorSubject = MapWorldOperationValidation.RequireText(actorSubject, nameof(actorSubject));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            MapResourceVersion = MapWorldOperationValidation.OptionalText(mapResourceVersion, nameof(mapResourceVersion));
            CorrelationId = MapWorldOperationValidation.RequireText(correlationId, nameof(correlationId));
            Confirmed = confirmed;
            StrongConfirmed = strongConfirmed;
            MapWorldOperationValidation.RequireUtc(requestedAtUtc, nameof(requestedAtUtc));
            RequestedAtUtc = requestedAtUtc;
        }

        public string ActorSubject { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public string CorrelationId { get; }
        public bool Confirmed { get; }
        public bool StrongConfirmed { get; }
        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class SpawnWorldEntityRequest : MaintenanceOperationRequest
    {
        public SpawnWorldEntityRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string catalogVersion, string entityTypeResourceId, int quantity,
            WorldCoordinate center, double radius, string correlationId, bool confirmed,
            bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            CatalogVersion = MapWorldOperationValidation.RequireText(catalogVersion, nameof(catalogVersion));
            EntityTypeResourceId = MapWorldOperationValidation.RequireText(
                entityTypeResourceId,
                nameof(entityTypeResourceId));
            if (quantity < 1 || quantity > 50) throw new ArgumentOutOfRangeException(nameof(quantity));
            Quantity = quantity;
            Center = center ?? throw new ArgumentNullException(nameof(center));
            if (!MapWorldOperationValidation.IsFinite(radius) || radius < 0 || radius > 100)
                throw new ArgumentOutOfRangeException(nameof(radius));
            Radius = radius;
        }

        public string CatalogVersion { get; }
        public string EntityTypeResourceId { get; }
        public int Quantity { get; }
        public WorldCoordinate Center { get; }
        public double Radius { get; }
    }

    public sealed class DeleteWorldEntityRequest : MaintenanceOperationRequest
    {
        public DeleteWorldEntityRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string catalogVersion, string targetId, long entityId, string entityTypeResourceId,
            string? ownerStableIdentity, WorldCoordinate observedPosition, string correlationId,
            bool confirmed, bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            CatalogVersion = MapWorldOperationValidation.RequireText(catalogVersion, nameof(catalogVersion));
            TargetId = MapWorldOperationValidation.RequireText(targetId, nameof(targetId));
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            EntityId = entityId;
            EntityTypeResourceId = MapWorldOperationValidation.RequireText(
                entityTypeResourceId,
                nameof(entityTypeResourceId));
            OwnerStableIdentity = MapWorldOperationValidation.OptionalText(
                ownerStableIdentity,
                nameof(ownerStableIdentity));
            ObservedPosition = observedPosition ?? throw new ArgumentNullException(nameof(observedPosition));
        }

        public string CatalogVersion { get; }
        public string TargetId { get; }
        public long EntityId { get; }
        public string EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public WorldCoordinate ObservedPosition { get; }
    }

    public sealed class CleanupWorldEntitiesRequest : MaintenanceOperationRequest
    {
        public CleanupWorldEntitiesRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldEntityCategory category, WorldCoordinate center, double radius, int maximumCount,
            string correlationId, bool confirmed, bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            if (!Enum.IsDefined(typeof(WorldEntityCategory), category))
                throw new ArgumentOutOfRangeException(nameof(category));
            Category = category;
            Center = center ?? throw new ArgumentNullException(nameof(center));
            if (!MapWorldOperationValidation.IsFinite(radius) || radius < 0 || radius > 1000)
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (maximumCount < 1 || maximumCount > 1000)
                throw new ArgumentOutOfRangeException(nameof(maximumCount));
            Radius = radius;
            MaximumCount = maximumCount;
        }

        public WorldEntityCategory Category { get; }
        public WorldCoordinate Center { get; }
        public double Radius { get; }
        public int MaximumCount { get; }
    }

    public sealed class ReloadGameResourceRequest : MaintenanceOperationRequest
    {
        public ReloadGameResourceRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            WorldReloadResourceKind resourceKind, string correlationId, bool confirmed,
            bool strongConfirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, correlationId,
                confirmed, strongConfirmed, requestedAtUtc)
        {
            if (!Enum.IsDefined(typeof(WorldReloadResourceKind), resourceKind))
                throw new ArgumentOutOfRangeException(nameof(resourceKind));
            ResourceKind = resourceKind;
        }

        public WorldReloadResourceKind ResourceKind { get; }
    }

    public sealed class CollectGameGarbageRequest : MaintenanceOperationRequest
    {
        public CollectGameGarbageRequest(
            string actorSubject, string worldId, string worldVersion, string? mapResourceVersion,
            string correlationId, bool confirmed, DateTimeOffset requestedAtUtc)
            : base(actorSubject, worldId, worldVersion, mapResourceVersion, correlationId,
                confirmed, false, requestedAtUtc) { }
    }
}
