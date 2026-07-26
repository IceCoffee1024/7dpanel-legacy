using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum MapLayerKind
    {
        Traders,
        LandClaims,
        Vehicles,
        Drones
    }

    public enum MapEntityLoadState
    {
        Loaded,
        Unloaded
    }

    public readonly struct MapLayerPosition
    {
        public MapLayerPosition(double x, double y, double z)
        {
            if (!IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
            if (!IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        internal static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct MapLayerBounds
    {
        public MapLayerBounds(double minimumX, double minimumZ, double maximumX, double maximumZ)
        {
            if (!MapLayerPosition.IsFinite(minimumX)) throw new ArgumentOutOfRangeException(nameof(minimumX));
            if (!MapLayerPosition.IsFinite(minimumZ)) throw new ArgumentOutOfRangeException(nameof(minimumZ));
            if (!MapLayerPosition.IsFinite(maximumX) || maximumX <= minimumX)
                throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (!MapLayerPosition.IsFinite(maximumZ) || maximumZ <= minimumZ)
                throw new ArgumentOutOfRangeException(nameof(maximumZ));
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
        }

        public double MinimumX { get; }
        public double MinimumZ { get; }
        public double MaximumX { get; }
        public double MaximumZ { get; }
    }

    public abstract class MapLayerFeature
    {
        protected MapLayerFeature(string id, MapLayerPosition position)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A map feature identifier is required.", nameof(id));
            Id = id;
            Position = position;
        }

        public string Id { get; }
        public MapLayerPosition Position { get; }
    }

    public sealed class TraderMapFeature : MapLayerFeature
    {
        public TraderMapFeature(
            string id,
            MapLayerPosition position,
            string? name,
            bool isOpen,
            MapLayerBounds? prefabBounds,
            double? protectionRadius)
            : base(id, position)
        {
            ValidateOptionalText(name, nameof(name));
            ValidateOptionalNonNegativeFinite(protectionRadius, nameof(protectionRadius));
            Name = name;
            IsOpen = isOpen;
            PrefabBounds = prefabBounds;
            ProtectionRadius = protectionRadius;
        }

        public string? Name { get; }
        public bool IsOpen { get; }
        public MapLayerBounds? PrefabBounds { get; }
        public double? ProtectionRadius { get; }

        internal static void ValidateOptionalText(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Optional text cannot be blank.", parameterName);
        }

        internal static void ValidateOptionalNonNegativeFinite(double? value, string parameterName)
        {
            if (value.HasValue && (!MapLayerPosition.IsFinite(value.Value) || value.Value < 0))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public sealed class LandClaimMapFeature : MapLayerFeature
    {
        public LandClaimMapFeature(
            string id,
            MapLayerPosition position,
            string? ownerCrossplatformId,
            double? protectionRadius,
            bool? isValid,
            DateTimeOffset? ownerLastLoginUtc)
            : base(id, position)
        {
            TraderMapFeature.ValidateOptionalText(ownerCrossplatformId, nameof(ownerCrossplatformId));
            TraderMapFeature.ValidateOptionalNonNegativeFinite(protectionRadius, nameof(protectionRadius));
            if (ownerLastLoginUtc.HasValue)
                HistoryPlayerValidation.RequireUtc(ownerLastLoginUtc.Value, nameof(ownerLastLoginUtc));
            OwnerCrossplatformId = ownerCrossplatformId;
            ProtectionRadius = protectionRadius;
            IsValid = isValid;
            OwnerLastLoginUtc = ownerLastLoginUtc;
        }

        public string? OwnerCrossplatformId { get; }
        public double? ProtectionRadius { get; }
        public bool? IsValid { get; }
        public DateTimeOffset? OwnerLastLoginUtc { get; }
    }

    public sealed class VehicleMapFeature : MapLayerFeature
    {
        public VehicleMapFeature(
            string id,
            MapLayerPosition position,
            string? vehicleType,
            string? ownerCrossplatformId,
            MapEntityLoadState loadState,
            double? fuelPercentage,
            int? quality,
            bool? isLocked,
            int? storageItemCount)
            : base(id, position)
        {
            TraderMapFeature.ValidateOptionalText(vehicleType, nameof(vehicleType));
            TraderMapFeature.ValidateOptionalText(ownerCrossplatformId, nameof(ownerCrossplatformId));
            if (fuelPercentage.HasValue &&
                (!MapLayerPosition.IsFinite(fuelPercentage.Value) || fuelPercentage.Value < 0 || fuelPercentage.Value > 100))
                throw new ArgumentOutOfRangeException(nameof(fuelPercentage));
            if (quality.HasValue && quality.Value < 0) throw new ArgumentOutOfRangeException(nameof(quality));
            if (storageItemCount.HasValue && storageItemCount.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(storageItemCount));
            VehicleType = vehicleType;
            OwnerCrossplatformId = ownerCrossplatformId;
            LoadState = loadState;
            FuelPercentage = fuelPercentage;
            Quality = quality;
            IsLocked = isLocked;
            StorageItemCount = storageItemCount;
        }

        public string? VehicleType { get; }
        public string? OwnerCrossplatformId { get; }
        public MapEntityLoadState LoadState { get; }
        public double? FuelPercentage { get; }
        public int? Quality { get; }
        public bool? IsLocked { get; }
        public int? StorageItemCount { get; }
    }

    public sealed class DroneMapFeature : MapLayerFeature
    {
        public DroneMapFeature(
            string id,
            MapLayerPosition position,
            string? ownerCrossplatformId,
            MapEntityLoadState loadState)
            : base(id, position)
        {
            TraderMapFeature.ValidateOptionalText(ownerCrossplatformId, nameof(ownerCrossplatformId));
            OwnerCrossplatformId = ownerCrossplatformId;
            LoadState = loadState;
        }

        public string? OwnerCrossplatformId { get; }
        public MapEntityLoadState LoadState { get; }
    }

    public sealed class MapLayerLimitExceededException : InvalidOperationException
    {
        public const string StableMessage = "The map layer result limit was exceeded.";

        public MapLayerLimitExceededException() : base(StableMessage) { }
    }

    public sealed class MapLayerQuery
    {
        public const int MaximumResultLimit = 500;

        public MapLayerQuery(MapLayerKind layer, MapExtent extent, int zoom, int limit)
        {
            if (!Enum.IsDefined(typeof(MapLayerKind), layer))
                throw new ArgumentOutOfRangeException(nameof(layer));
            if (zoom < 0) throw new ArgumentOutOfRangeException(nameof(zoom));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            if (limit > MaximumResultLimit) throw new MapLayerLimitExceededException();
            Layer = layer;
            Extent = extent;
            Zoom = zoom;
            Limit = limit;
        }

        public MapLayerKind Layer { get; }
        public MapExtent Extent { get; }
        public int Zoom { get; }
        public int Limit { get; }

        public static int MinimumZoom(MapLayerKind layer)
        {
            switch (layer)
            {
                case MapLayerKind.Traders: return 1;
                case MapLayerKind.LandClaims: return 2;
                case MapLayerKind.Vehicles:
                case MapLayerKind.Drones: return 3;
                default: throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }
    }

    public sealed class MapLayerProjectionSnapshot
    {
        private MapLayerProjectionSnapshot(
            AvailabilityState availability,
            MapLayerKind layer,
            bool isZoomSufficient,
            DateTimeOffset? observedAtUtc,
            IEnumerable<MapLayerFeature>? features)
        {
            Availability = availability;
            Layer = layer;
            IsZoomSufficient = isZoomSufficient;
            ObservedAtUtc = observedAtUtc;
            Features = new ReadOnlyCollection<MapLayerFeature>(
                (features ?? Enumerable.Empty<MapLayerFeature>()).ToArray());
        }

        public AvailabilityState Availability { get; }
        public MapLayerKind Layer { get; }
        public bool IsZoomSufficient { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<MapLayerFeature> Features { get; }

        public static MapLayerProjectionSnapshot Available(
            MapLayerKind layer,
            DateTimeOffset observedAtUtc,
            IEnumerable<MapLayerFeature> features,
            bool isZoomSufficient = true) =>
            Create(AvailabilityState.Available, layer, observedAtUtc, features, isZoomSufficient);

        public static MapLayerProjectionSnapshot Stale(
            MapLayerKind layer,
            DateTimeOffset observedAtUtc,
            IEnumerable<MapLayerFeature> features,
            bool isZoomSufficient = true) =>
            Create(AvailabilityState.Stale, layer, observedAtUtc, features, isZoomSufficient);

        public static MapLayerProjectionSnapshot Unavailable(MapLayerKind layer) =>
            new MapLayerProjectionSnapshot(
                AvailabilityState.Unavailable,
                layer,
                false,
                null,
                null);

        private static MapLayerProjectionSnapshot Create(
            AvailabilityState availability,
            MapLayerKind layer,
            DateTimeOffset observedAtUtc,
            IEnumerable<MapLayerFeature> features,
            bool isZoomSufficient)
        {
            if (features == null) throw new ArgumentNullException(nameof(features));
            return new MapLayerProjectionSnapshot(
                availability,
                layer,
                isZoomSufficient,
                HistoryPlayerValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc)),
                isZoomSufficient ? features : Enumerable.Empty<MapLayerFeature>());
        }
    }
}
