using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapLayerProjection : IMapLayerProjection
    {
        private readonly object sync = new object();
        private PublishedSnapshot? published;

        public void Publish(SevenDaysMapLayerSample sample, DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));

            var next = new PublishedSnapshot(
                observedAtUtc,
                sample.Traders.Select(ToFeature).ToArray(),
                sample.LandClaims.Select(ToFeature).ToArray(),
                sample.Vehicles.Select(ToFeature).ToArray(),
                sample.Drones.Select(ToFeature).ToArray(),
                isStale: false);
            lock (sync) published = next;
        }

        public MapLayerProjectionSnapshot Query(MapLayerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            lock (sync)
            {
                if (published == null)
                    return MapLayerProjectionSnapshot.Unavailable(query.Layer);

                var isZoomSufficient = query.Zoom >= MapLayerQuery.MinimumZoom(query.Layer);
                var matches = isZoomSufficient
                    ? Features(query.Layer)
                        .Where(feature => Contains(query.Extent, feature.Position))
                        .Take(query.Limit + 1)
                        .ToArray()
                    : Array.Empty<MapLayerFeature>();
                if (matches.Length > query.Limit)
                    throw new MapLayerLimitExceededException();

                return published.IsStale
                    ? MapLayerProjectionSnapshot.Stale(
                        query.Layer,
                        published.ObservedAtUtc,
                        matches,
                        isZoomSufficient)
                    : MapLayerProjectionSnapshot.Available(
                        query.Layer,
                        published.ObservedAtUtc,
                        matches,
                        isZoomSufficient);
            }
        }

        public void MarkCaptureFailed()
        {
            lock (sync)
            {
                if (published != null) published = published.AsStale();
            }
        }

        public void Clear()
        {
            lock (sync) published = null;
        }

        private IReadOnlyList<MapLayerFeature> Features(MapLayerKind layer)
        {
            switch (layer)
            {
                case MapLayerKind.Traders: return published!.Traders;
                case MapLayerKind.LandClaims: return published!.LandClaims;
                case MapLayerKind.Vehicles: return published!.Vehicles;
                case MapLayerKind.Drones: return published!.Drones;
                default: throw new ArgumentOutOfRangeException(nameof(layer));
            }
        }

        private static TraderMapFeature ToFeature(SevenDaysTraderMapSample sample) =>
            new TraderMapFeature(
                sample.Id,
                Position(sample),
                sample.Name,
                sample.IsOpen,
                sample.PrefabBounds,
                sample.ProtectionRadius);

        private static LandClaimMapFeature ToFeature(SevenDaysLandClaimMapSample sample) =>
            new LandClaimMapFeature(
                sample.Id,
                Position(sample),
                sample.OwnerCrossplatformId,
                sample.ProtectionRadius,
                sample.IsValid,
                sample.OwnerLastLoginUtc);

        private static VehicleMapFeature ToFeature(SevenDaysVehicleMapSample sample) =>
            new VehicleMapFeature(
                sample.Id,
                Position(sample),
                sample.VehicleType,
                sample.OwnerCrossplatformId,
                sample.LoadState,
                sample.FuelPercentage,
                sample.Quality,
                sample.IsLocked,
                sample.StorageItemCount);

        private static DroneMapFeature ToFeature(SevenDaysDroneMapSample sample) =>
            new DroneMapFeature(
                sample.Id,
                Position(sample),
                sample.OwnerCrossplatformId,
                sample.LoadState);

        private static MapLayerPosition Position(SevenDaysMapFeatureSample sample) =>
            new MapLayerPosition(sample.X, sample.Y, sample.Z);

        private static bool Contains(MapExtent extent, MapLayerPosition position) =>
            position.X >= extent.MinimumX &&
            position.X <= extent.MaximumX &&
            position.Z >= extent.MinimumZ &&
            position.Z <= extent.MaximumZ;

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private sealed class PublishedSnapshot
        {
            public PublishedSnapshot(
                DateTimeOffset observedAtUtc,
                TraderMapFeature[] traders,
                LandClaimMapFeature[] landClaims,
                VehicleMapFeature[] vehicles,
                DroneMapFeature[] drones,
                bool isStale)
            {
                ObservedAtUtc = observedAtUtc;
                Traders = traders;
                LandClaims = landClaims;
                Vehicles = vehicles;
                Drones = drones;
                IsStale = isStale;
            }

            public DateTimeOffset ObservedAtUtc { get; }
            public TraderMapFeature[] Traders { get; }
            public LandClaimMapFeature[] LandClaims { get; }
            public VehicleMapFeature[] Vehicles { get; }
            public DroneMapFeature[] Drones { get; }
            public bool IsStale { get; }

            public PublishedSnapshot AsStale() =>
                IsStale
                    ? this
                    : new PublishedSnapshot(
                        ObservedAtUtc,
                        Traders,
                        LandClaims,
                        Vehicles,
                        Drones,
                        isStale: true);
        }
    }
}
