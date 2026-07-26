using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public abstract class SevenDaysMapFeatureSample
    {
        protected SevenDaysMapFeatureSample(string id, float x, float y, float z)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("A map feature identifier is required.", nameof(id));
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(z, nameof(z));
            Id = id;
            X = x;
            Y = y;
            Z = z;
        }

        public string Id { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        protected static void ValidateOptionalText(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Optional text cannot be blank.", parameterName);
        }

        protected static void ValidateOwnerCrossplatformId(string? value, string parameterName)
        {
            ValidateOptionalText(value, parameterName);
            if (value != null && value.Length > 256)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        protected static void ValidateOptionalNonNegativeFinite(double? value, string parameterName)
        {
            if (value.HasValue &&
                (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public sealed class SevenDaysTraderMapSample : SevenDaysMapFeatureSample
    {
        public SevenDaysTraderMapSample(
            string id,
            string? name,
            float x,
            float y,
            float z,
            bool isOpen,
            MapLayerBounds? prefabBounds,
            double? protectionRadius)
            : base(id, x, y, z)
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
    }

    public sealed class SevenDaysLandClaimMapSample : SevenDaysMapFeatureSample
    {
        public SevenDaysLandClaimMapSample(
            string id,
            float x,
            float y,
            float z,
            string? ownerCrossplatformId,
            double? protectionRadius,
            bool? isValid,
            DateTimeOffset? ownerLastLoginUtc)
            : base(id, x, y, z)
        {
            ValidateOwnerCrossplatformId(ownerCrossplatformId, nameof(ownerCrossplatformId));
            ValidateOptionalNonNegativeFinite(protectionRadius, nameof(protectionRadius));
            if (ownerLastLoginUtc.HasValue && ownerLastLoginUtc.Value.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ownerLastLoginUtc));
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

    public sealed class SevenDaysVehicleMapSample : SevenDaysMapFeatureSample
    {
        public SevenDaysVehicleMapSample(
            string id,
            float x,
            float y,
            float z,
            string? vehicleType,
            string? ownerCrossplatformId,
            MapEntityLoadState loadState,
            double? fuelPercentage,
            int? quality,
            bool? isLocked,
            int? storageItemCount)
            : base(id, x, y, z)
        {
            ValidateOptionalText(vehicleType, nameof(vehicleType));
            ValidateOwnerCrossplatformId(ownerCrossplatformId, nameof(ownerCrossplatformId));
            if (!Enum.IsDefined(typeof(MapEntityLoadState), loadState))
                throw new ArgumentOutOfRangeException(nameof(loadState));
            if (fuelPercentage.HasValue &&
                (double.IsNaN(fuelPercentage.Value) ||
                 double.IsInfinity(fuelPercentage.Value) ||
                 fuelPercentage.Value < 0 ||
                 fuelPercentage.Value > 100))
            {
                throw new ArgumentOutOfRangeException(nameof(fuelPercentage));
            }
            if (quality.HasValue && quality.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(quality));
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

    public sealed class SevenDaysDroneMapSample : SevenDaysMapFeatureSample
    {
        public SevenDaysDroneMapSample(
            string id,
            float x,
            float y,
            float z,
            string? ownerCrossplatformId,
            MapEntityLoadState loadState)
            : base(id, x, y, z)
        {
            ValidateOwnerCrossplatformId(ownerCrossplatformId, nameof(ownerCrossplatformId));
            if (!Enum.IsDefined(typeof(MapEntityLoadState), loadState))
                throw new ArgumentOutOfRangeException(nameof(loadState));
            OwnerCrossplatformId = ownerCrossplatformId;
            LoadState = loadState;
        }

        public string? OwnerCrossplatformId { get; }
        public MapEntityLoadState LoadState { get; }
    }
}
