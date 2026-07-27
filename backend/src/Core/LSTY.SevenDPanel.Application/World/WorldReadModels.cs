using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class WorldSummary
    {
        public WorldSummary(
            AvailabilityState sourceState,
            string? worldId,
            string? worldVersion,
            string? seed,
            int? width,
            int? height,
            string? gameVersion,
            string? mapResourceVersion,
            MapExtent? availableExtent,
            DateTimeOffset? observedAtUtc)
        {
            RequireSourceState(sourceState, nameof(sourceState));
            ValidateOptionalText(worldId, nameof(worldId));
            ValidateOptionalText(worldVersion, nameof(worldVersion));
            ValidateOptionalText(seed, nameof(seed));
            ValidateOptionalText(gameVersion, nameof(gameVersion));
            ValidateOptionalText(mapResourceVersion, nameof(mapResourceVersion));
            if (width.HasValue && width.Value <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height.HasValue && height.Value <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (observedAtUtc.HasValue)
                HistoryPlayerValidation.RequireUtc(observedAtUtc.Value, nameof(observedAtUtc));
            if (sourceState != AvailabilityState.Unavailable &&
                (worldId == null || worldVersion == null || !observedAtUtc.HasValue))
            {
                throw new ArgumentException("An available world source requires identity, version, and observation time.");
            }

            SourceState = sourceState;
            WorldId = worldId;
            WorldVersion = worldVersion;
            Seed = seed;
            Width = width;
            Height = height;
            GameVersion = gameVersion;
            MapResourceVersion = mapResourceVersion;
            AvailableExtent = availableExtent;
            ObservedAtUtc = observedAtUtc;
        }

        public AvailabilityState SourceState { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? Seed { get; }
        public int? Width { get; }
        public int? Height { get; }
        public string? GameVersion { get; }
        public string? MapResourceVersion { get; }
        public MapExtent? AvailableExtent { get; }
        public DateTimeOffset? ObservedAtUtc { get; }

        public static WorldSummary Unavailable() =>
            new WorldSummary(
                AvailabilityState.Unavailable,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);

        internal static void RequireSourceState(AvailabilityState value, string parameterName)
        {
            if (value != AvailabilityState.Available &&
                value != AvailabilityState.Stale &&
                value != AvailabilityState.Unavailable)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        internal static void ValidateOptionalText(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Optional text cannot be blank.", parameterName);
        }

        internal static void RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A value is required.", parameterName);
        }
    }

    public sealed class ApprovedWorldItemSummary
    {
        public ApprovedWorldItemSummary(string resourceId, int count, int? quality)
        {
            WorldSummary.RequireText(resourceId, nameof(resourceId));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (quality.HasValue && quality.Value < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            ResourceId = resourceId;
            Count = count;
            Quality = quality;
        }

        public string ResourceId { get; }
        public int Count { get; }
        public int? Quality { get; }
    }

    public sealed class ContainerSummary
    {
        public ContainerSummary(
            string serverId,
            string stableIdentity,
            string parentStableIdentity,
            MapLayerPosition position,
            MapEntityLoadState loadState,
            bool? isLocked,
            int? slotCount,
            int? usedSlotCount,
            IEnumerable<ApprovedWorldItemSummary>? items)
        {
            WorldSummary.RequireText(serverId, nameof(serverId));
            WorldSummary.RequireText(stableIdentity, nameof(stableIdentity));
            WorldSummary.RequireText(parentStableIdentity, nameof(parentStableIdentity));
            RequireLoadState(loadState, nameof(loadState));
            if (slotCount.HasValue && slotCount.Value < 0) throw new ArgumentOutOfRangeException(nameof(slotCount));
            if (usedSlotCount.HasValue && usedSlotCount.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(usedSlotCount));
            if (slotCount.HasValue && usedSlotCount.HasValue && usedSlotCount.Value > slotCount.Value)
                throw new ArgumentOutOfRangeException(nameof(usedSlotCount));

            ServerId = serverId;
            StableIdentity = stableIdentity;
            ParentStableIdentity = parentStableIdentity;
            Position = position;
            LoadState = loadState;
            IsLocked = isLocked;
            SlotCount = slotCount;
            UsedSlotCount = usedSlotCount;
            Items = items == null ? null : Copy(items, nameof(items));
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string ParentStableIdentity { get; }
        public MapLayerPosition Position { get; }
        public MapEntityLoadState LoadState { get; }
        public bool? IsLocked { get; }
        public int? SlotCount { get; }
        public int? UsedSlotCount { get; }
        public IReadOnlyList<ApprovedWorldItemSummary>? Items { get; }

        internal static void RequireLoadState(MapEntityLoadState value, string parameterName)
        {
            if (!Enum.IsDefined(typeof(MapEntityLoadState), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> values, string parameterName)
            where T : class
        {
            if (values == null) throw new ArgumentNullException(parameterName);
            var copy = values.ToArray();
            if (copy.Any(item => item == null))
                throw new ArgumentException("Collections cannot contain null values.", parameterName);
            return new ReadOnlyCollection<T>(copy);
        }
    }

    public sealed class LandClaimSummary
    {
        public LandClaimSummary(
            string serverId,
            string stableIdentity,
            MapLayerPosition position,
            string? ownerStableIdentity,
            double? protectionRadius,
            bool? isValid,
            DateTimeOffset? ownerLastLoginUtc)
        {
            WorldSummary.RequireText(serverId, nameof(serverId));
            WorldSummary.RequireText(stableIdentity, nameof(stableIdentity));
            WorldSummary.ValidateOptionalText(ownerStableIdentity, nameof(ownerStableIdentity));
            if (protectionRadius.HasValue &&
                (!MapLayerPosition.IsFinite(protectionRadius.Value) || protectionRadius.Value < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(protectionRadius));
            }
            if (ownerLastLoginUtc.HasValue)
                HistoryPlayerValidation.RequireUtc(ownerLastLoginUtc.Value, nameof(ownerLastLoginUtc));

            ServerId = serverId;
            StableIdentity = stableIdentity;
            Position = position;
            OwnerStableIdentity = ownerStableIdentity;
            ProtectionRadius = protectionRadius;
            IsValid = isValid;
            OwnerLastLoginUtc = ownerLastLoginUtc;
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public MapLayerPosition Position { get; }
        public string? OwnerStableIdentity { get; }
        public double? ProtectionRadius { get; }
        public bool? IsValid { get; }
        public DateTimeOffset? OwnerLastLoginUtc { get; }
    }

    public sealed class VehicleSummary
    {
        public VehicleSummary(
            string serverId,
            string stableIdentity,
            string? entityTypeResourceId,
            string? ownerStableIdentity,
            MapLayerPosition position,
            MapEntityLoadState loadState,
            bool? isLocked,
            double? fuelPercentage,
            int? quality,
            ContainerSummary? container)
        {
            WorldSummary.RequireText(serverId, nameof(serverId));
            WorldSummary.RequireText(stableIdentity, nameof(stableIdentity));
            WorldSummary.ValidateOptionalText(entityTypeResourceId, nameof(entityTypeResourceId));
            WorldSummary.ValidateOptionalText(ownerStableIdentity, nameof(ownerStableIdentity));
            ContainerSummary.RequireLoadState(loadState, nameof(loadState));
            if (fuelPercentage.HasValue &&
                (!MapLayerPosition.IsFinite(fuelPercentage.Value) ||
                 fuelPercentage.Value < 0 ||
                 fuelPercentage.Value > 100))
            {
                throw new ArgumentOutOfRangeException(nameof(fuelPercentage));
            }
            if (quality.HasValue && quality.Value < 0) throw new ArgumentOutOfRangeException(nameof(quality));

            ServerId = serverId;
            StableIdentity = stableIdentity;
            EntityTypeResourceId = entityTypeResourceId;
            OwnerStableIdentity = ownerStableIdentity;
            Position = position;
            LoadState = loadState;
            IsLocked = isLocked;
            FuelPercentage = fuelPercentage;
            Quality = quality;
            Container = container;
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public MapLayerPosition Position { get; }
        public MapEntityLoadState LoadState { get; }
        public bool? IsLocked { get; }
        public double? FuelPercentage { get; }
        public int? Quality { get; }
        public ContainerSummary? Container { get; }
    }

    public sealed class DroneSummary
    {
        public DroneSummary(
            string serverId,
            string stableIdentity,
            string? entityTypeResourceId,
            string? ownerStableIdentity,
            MapLayerPosition position,
            MapEntityLoadState loadState,
            bool? isLocked,
            int? quality,
            ContainerSummary? container)
        {
            WorldSummary.RequireText(serverId, nameof(serverId));
            WorldSummary.RequireText(stableIdentity, nameof(stableIdentity));
            WorldSummary.ValidateOptionalText(entityTypeResourceId, nameof(entityTypeResourceId));
            WorldSummary.ValidateOptionalText(ownerStableIdentity, nameof(ownerStableIdentity));
            ContainerSummary.RequireLoadState(loadState, nameof(loadState));
            if (quality.HasValue && quality.Value < 0) throw new ArgumentOutOfRangeException(nameof(quality));

            ServerId = serverId;
            StableIdentity = stableIdentity;
            EntityTypeResourceId = entityTypeResourceId;
            OwnerStableIdentity = ownerStableIdentity;
            Position = position;
            LoadState = loadState;
            IsLocked = isLocked;
            Quality = quality;
            Container = container;
        }

        public string ServerId { get; }
        public string StableIdentity { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public MapLayerPosition Position { get; }
        public MapEntityLoadState LoadState { get; }
        public bool? IsLocked { get; }
        public int? Quality { get; }
        public ContainerSummary? Container { get; }
    }

    public sealed class WorldCollectionSnapshot<T> where T : class
    {
        private WorldCollectionSnapshot(
            AvailabilityState sourceState,
            DateTimeOffset? observedAtUtc,
            IEnumerable<T>? items)
        {
            WorldSummary.RequireSourceState(sourceState, nameof(sourceState));
            if (observedAtUtc.HasValue)
                HistoryPlayerValidation.RequireUtc(observedAtUtc.Value, nameof(observedAtUtc));
            if (sourceState != AvailabilityState.Unavailable && !observedAtUtc.HasValue)
                throw new ArgumentException("An available collection requires an observation time.");

            SourceState = sourceState;
            ObservedAtUtc = observedAtUtc;
            Items = ContainerSummary.Copy(items ?? Enumerable.Empty<T>(), nameof(items));
        }

        public AvailabilityState SourceState { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<T> Items { get; }

        public static WorldCollectionSnapshot<T> Available(
            DateTimeOffset observedAtUtc,
            IEnumerable<T> items) =>
            new WorldCollectionSnapshot<T>(AvailabilityState.Available, observedAtUtc, items);

        public static WorldCollectionSnapshot<T> Stale(
            DateTimeOffset observedAtUtc,
            IEnumerable<T> items) =>
            new WorldCollectionSnapshot<T>(AvailabilityState.Stale, observedAtUtc, items);

        public static WorldCollectionSnapshot<T> Unavailable() =>
            new WorldCollectionSnapshot<T>(AvailabilityState.Unavailable, null, null);
    }

    public sealed class WorldSnapshot
    {
        public WorldSnapshot(
            WorldSummary world,
            WorldCollectionSnapshot<LandClaimSummary> landClaims,
            WorldCollectionSnapshot<VehicleSummary> vehicles,
            WorldCollectionSnapshot<DroneSummary> drones,
            WorldCollectionSnapshot<ContainerSummary> containers)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            LandClaims = landClaims ?? throw new ArgumentNullException(nameof(landClaims));
            Vehicles = vehicles ?? throw new ArgumentNullException(nameof(vehicles));
            Drones = drones ?? throw new ArgumentNullException(nameof(drones));
            Containers = containers ?? throw new ArgumentNullException(nameof(containers));
        }

        public WorldSummary World { get; }
        public WorldCollectionSnapshot<LandClaimSummary> LandClaims { get; }
        public WorldCollectionSnapshot<VehicleSummary> Vehicles { get; }
        public WorldCollectionSnapshot<DroneSummary> Drones { get; }
        public WorldCollectionSnapshot<ContainerSummary> Containers { get; }

        public static WorldSnapshot Unavailable() =>
            new WorldSnapshot(
                WorldSummary.Unavailable(),
                WorldCollectionSnapshot<LandClaimSummary>.Unavailable(),
                WorldCollectionSnapshot<VehicleSummary>.Unavailable(),
                WorldCollectionSnapshot<DroneSummary>.Unavailable(),
                WorldCollectionSnapshot<ContainerSummary>.Unavailable());
    }

    public sealed class WorldToolCatalogSnapshot
    {
        private WorldToolCatalogSnapshot(
            AvailabilityState sourceState,
            string? catalogVersion,
            DateTimeOffset? observedAtUtc,
            IEnumerable<string>? blockInternalNames,
            IEnumerable<string>? prefabResourceIds,
            IEnumerable<string>? entityTypeResourceIds)
        {
            WorldSummary.RequireSourceState(sourceState, nameof(sourceState));
            WorldSummary.ValidateOptionalText(catalogVersion, nameof(catalogVersion));
            if (observedAtUtc.HasValue)
                HistoryPlayerValidation.RequireUtc(observedAtUtc.Value, nameof(observedAtUtc));
            if (sourceState != AvailabilityState.Unavailable &&
                (catalogVersion == null || !observedAtUtc.HasValue))
            {
                throw new ArgumentException("An available catalog requires a version and observation time.");
            }

            SourceState = sourceState;
            CatalogVersion = catalogVersion;
            ObservedAtUtc = observedAtUtc;
            BlockInternalNames = CopyIdentifiers(blockInternalNames, nameof(blockInternalNames), opaque: false);
            PrefabResourceIds = CopyIdentifiers(prefabResourceIds, nameof(prefabResourceIds), opaque: true);
            EntityTypeResourceIds = CopyIdentifiers(entityTypeResourceIds, nameof(entityTypeResourceIds), opaque: true);
        }

        public AvailabilityState SourceState { get; }
        public string? CatalogVersion { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<string> BlockInternalNames { get; }
        public IReadOnlyList<string> PrefabResourceIds { get; }
        public IReadOnlyList<string> EntityTypeResourceIds { get; }

        public static WorldToolCatalogSnapshot Available(
            string catalogVersion,
            DateTimeOffset observedAtUtc,
            IEnumerable<string> blockInternalNames,
            IEnumerable<string> prefabResourceIds,
            IEnumerable<string> entityTypeResourceIds) =>
            new WorldToolCatalogSnapshot(
                AvailabilityState.Available,
                catalogVersion,
                observedAtUtc,
                blockInternalNames,
                prefabResourceIds,
                entityTypeResourceIds);

        public static WorldToolCatalogSnapshot Stale(WorldToolCatalogSnapshot previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            if (previous.CatalogVersion == null || !previous.ObservedAtUtc.HasValue)
                return Unavailable();
            return new WorldToolCatalogSnapshot(
                AvailabilityState.Stale,
                previous.CatalogVersion,
                previous.ObservedAtUtc,
                previous.BlockInternalNames,
                previous.PrefabResourceIds,
                previous.EntityTypeResourceIds);
        }

        public static WorldToolCatalogSnapshot Unavailable() =>
            new WorldToolCatalogSnapshot(
                AvailabilityState.Unavailable,
                null,
                null,
                null,
                null,
                null);

        private static IReadOnlyList<string> CopyIdentifiers(
            IEnumerable<string>? values,
            string parameterName,
            bool opaque)
        {
            var copy = (values ?? Enumerable.Empty<string>()).ToArray();
            if (copy.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Catalog identifiers cannot be blank.", parameterName);
            if (opaque && copy.Any(value => value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0))
                throw new ArgumentException("Opaque resource identifiers cannot contain path separators.", parameterName);
            return new ReadOnlyCollection<string>(copy);
        }
    }
}
