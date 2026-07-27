using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class WorldPositionHttpResponse
    {
        internal WorldPositionHttpResponse(MapLayerPosition position)
        {
            X = position.X;
            Y = position.Y;
            Z = position.Z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public sealed class WorldExtentHttpResponse
    {
        internal WorldExtentHttpResponse(MapExtent extent)
        {
            MinimumX = extent.MinimumX;
            MinimumZ = extent.MinimumZ;
            MaximumX = extent.MaximumX;
            MaximumZ = extent.MaximumZ;
        }

        public float MinimumX { get; }
        public float MinimumZ { get; }
        public float MaximumX { get; }
        public float MaximumZ { get; }
    }

    public sealed class WorldSummaryHttpResponse
    {
        internal WorldSummaryHttpResponse(WorldSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            SourceState = summary.SourceState.ToString();
            WorldId = summary.WorldId;
            WorldVersion = summary.WorldVersion;
            Seed = summary.Seed;
            Width = summary.Width;
            Height = summary.Height;
            GameVersion = summary.GameVersion;
            MapResourceVersion = summary.MapResourceVersion;
            AvailableExtent = summary.AvailableExtent.HasValue
                ? new WorldExtentHttpResponse(summary.AvailableExtent.Value)
                : null;
            ObservedAtUtc = summary.ObservedAtUtc;
        }

        public string SourceState { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? Seed { get; }
        public int? Width { get; }
        public int? Height { get; }
        public string? GameVersion { get; }
        public string? MapResourceVersion { get; }
        public WorldExtentHttpResponse? AvailableExtent { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
    }

    public sealed class WorldCollectionHttpResponse<T>
    {
        internal WorldCollectionHttpResponse(
            AvailabilityState sourceState,
            DateTimeOffset? observedAtUtc,
            IReadOnlyList<T> items)
        {
            SourceState = sourceState.ToString();
            ObservedAtUtc = observedAtUtc;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string SourceState { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<T> Items { get; }
    }

    public sealed class ApprovedWorldItemHttpResponse
    {
        internal ApprovedWorldItemHttpResponse(ApprovedWorldItemSummary item)
        {
            ResourceId = item.ResourceId;
            Count = item.Count;
            Quality = item.Quality;
        }

        public string ResourceId { get; }
        public int Count { get; }
        public int? Quality { get; }
    }

    public sealed class WorldContainerHttpResponse
    {
        internal WorldContainerHttpResponse(ContainerSummary container)
        {
            ServerId = container.ServerId;
            StableIdentity = container.StableIdentity;
            ParentStableIdentity = container.ParentStableIdentity;
            Position = new WorldPositionHttpResponse(container.Position);
            LoadState = container.LoadState.ToString();
            IsLocked = container.IsLocked;
            SlotCount = container.SlotCount;
            UsedSlotCount = container.UsedSlotCount;
            Items = container.Items?.Select(item => new ApprovedWorldItemHttpResponse(item)).ToArray();
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string ParentStableIdentity { get; }
        public WorldPositionHttpResponse Position { get; }
        public string LoadState { get; }
        public bool? IsLocked { get; }
        public int? SlotCount { get; }
        public int? UsedSlotCount { get; }
        public IReadOnlyList<ApprovedWorldItemHttpResponse>? Items { get; }
    }

    public sealed class WorldLandClaimHttpResponse
    {
        internal WorldLandClaimHttpResponse(LandClaimSummary claim)
        {
            ServerId = claim.ServerId;
            StableIdentity = claim.StableIdentity;
            Position = new WorldPositionHttpResponse(claim.Position);
            OwnerStableIdentity = claim.OwnerStableIdentity;
            ProtectionRadius = claim.ProtectionRadius;
            IsValid = claim.IsValid;
            OwnerLastLoginUtc = claim.OwnerLastLoginUtc;
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public WorldPositionHttpResponse Position { get; }
        public string? OwnerStableIdentity { get; }
        public double? ProtectionRadius { get; }
        public bool? IsValid { get; }
        public DateTimeOffset? OwnerLastLoginUtc { get; }
    }

    public sealed class WorldVehicleHttpResponse
    {
        internal WorldVehicleHttpResponse(VehicleSummary vehicle)
        {
            ServerId = vehicle.ServerId;
            StableIdentity = vehicle.StableIdentity;
            EntityTypeResourceId = vehicle.EntityTypeResourceId;
            OwnerStableIdentity = vehicle.OwnerStableIdentity;
            Position = new WorldPositionHttpResponse(vehicle.Position);
            LoadState = vehicle.LoadState.ToString();
            IsLocked = vehicle.IsLocked;
            FuelPercentage = vehicle.FuelPercentage;
            Quality = vehicle.Quality;
            Container = vehicle.Container == null
                ? null
                : new WorldContainerHttpResponse(vehicle.Container);
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public WorldPositionHttpResponse Position { get; }
        public string LoadState { get; }
        public bool? IsLocked { get; }
        public double? FuelPercentage { get; }
        public int? Quality { get; }
        public WorldContainerHttpResponse? Container { get; }
    }

    public sealed class WorldDroneHttpResponse
    {
        internal WorldDroneHttpResponse(DroneSummary drone)
        {
            ServerId = drone.ServerId;
            StableIdentity = drone.StableIdentity;
            EntityTypeResourceId = drone.EntityTypeResourceId;
            OwnerStableIdentity = drone.OwnerStableIdentity;
            Position = new WorldPositionHttpResponse(drone.Position);
            LoadState = drone.LoadState.ToString();
            IsLocked = drone.IsLocked;
            Quality = drone.Quality;
            Container = drone.Container == null
                ? null
                : new WorldContainerHttpResponse(drone.Container);
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public WorldPositionHttpResponse Position { get; }
        public string LoadState { get; }
        public bool? IsLocked { get; }
        public int? Quality { get; }
        public WorldContainerHttpResponse? Container { get; }
    }

    public sealed class WorldCatalogHttpResponse
    {
        internal WorldCatalogHttpResponse(
            WorldToolCatalogSnapshot snapshot,
            IReadOnlyList<string> items)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            SourceState = snapshot.SourceState.ToString();
            CatalogVersion = snapshot.CatalogVersion;
            ObservedAtUtc = snapshot.ObservedAtUtc;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string SourceState { get; }
        public string? CatalogVersion { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<string> Items { get; }
    }

    public sealed class MapResourceVersionHttpResponse
    {
        internal MapResourceVersionHttpResponse(WorldSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            SourceState = summary.SourceState.ToString();
            WorldId = summary.WorldId;
            WorldVersion = summary.WorldVersion;
            MapResourceVersion = summary.MapResourceVersion;
            ObservedAtUtc = summary.ObservedAtUtc;
        }

        public string SourceState { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
    }
}
