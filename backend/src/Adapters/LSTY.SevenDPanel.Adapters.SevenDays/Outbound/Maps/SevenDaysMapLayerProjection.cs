using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
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
                LayerState.Available(observedAtUtc, sample.Traders.Select(ToFeature)),
                LayerState.Available(observedAtUtc, sample.LandClaims.Select(ToFeature)),
                LayerState.Available(observedAtUtc, sample.Vehicles.Select(ToFeature)),
                LayerState.Available(observedAtUtc, sample.Drones.Select(ToFeature)));
            lock (sync) published = next;
        }

        public void Publish(SevenDaysWorldScalarSnapshot sample, DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            lock (sync)
            {
                if (!sample.WorldAvailable)
                {
                    published = null;
                    return;
                }

                var previous = published ?? PublishedSnapshot.Empty;
                published = new PublishedSnapshot(
                    previous.Traders,
                    Source(sample.LandClaimsCaptureFailed, observedAtUtc, sample.LandClaims.Select(ToFeature)),
                    Source(sample.VehiclesCaptureFailed, observedAtUtc, sample.Vehicles.Select(ToFeature)),
                    Source(sample.DronesCaptureFailed, observedAtUtc, sample.Drones.Select(ToFeature)));
            }
        }

        public MapLayerProjectionSnapshot Query(MapLayerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            lock (sync)
            {
                if (published == null)
                    return MapLayerProjectionSnapshot.Unavailable(query.Layer);

                var source = published.Get(query.Layer);
                if (source.Availability == AvailabilityState.Unavailable || !source.ObservedAtUtc.HasValue)
                    return MapLayerProjectionSnapshot.Unavailable(query.Layer);

                var isZoomSufficient = query.Zoom >= MapLayerQuery.MinimumZoom(query.Layer);
                var matches = isZoomSufficient
                    ? source.Features
                        .Where(feature => Contains(query.Extent, feature.Position))
                        .Take(query.Limit + 1)
                        .ToArray()
                    : Array.Empty<MapLayerFeature>();
                if (matches.Length > query.Limit)
                    throw new MapLayerLimitExceededException();

                return source.Availability == AvailabilityState.Stale
                    ? MapLayerProjectionSnapshot.Stale(
                        query.Layer,
                        source.ObservedAtUtc.Value,
                        matches,
                        isZoomSufficient)
                    : MapLayerProjectionSnapshot.Available(
                        query.Layer,
                        source.ObservedAtUtc.Value,
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

        private static LayerState Source(
            bool captureFailed,
            DateTimeOffset observedAtUtc,
            IEnumerable<MapLayerFeature> features) =>
            captureFailed
                ? LayerState.Unavailable()
                : LayerState.Available(observedAtUtc, features);

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
                sample.StableIdentity,
                Position(sample),
                sample.OwnerCrossplatformId,
                sample.ProtectionRadius,
                sample.IsValid,
                sample.OwnerLastLoginUtc);

        private static VehicleMapFeature ToFeature(SevenDaysVehicleMapSample sample) =>
            new VehicleMapFeature(
                sample.Id,
                sample.StableIdentity,
                Position(sample),
                sample.VehicleType,
                sample.EntityTypeResourceId,
                sample.OwnerCrossplatformId,
                sample.LoadState,
                sample.FuelPercentage,
                sample.Quality,
                sample.IsLocked,
                sample.StorageItemCount,
                sample.Container);

        private static DroneMapFeature ToFeature(SevenDaysDroneMapSample sample) =>
            new DroneMapFeature(
                sample.Id,
                sample.StableIdentity,
                Position(sample),
                sample.EntityTypeResourceId,
                sample.OwnerCrossplatformId,
                sample.LoadState,
                sample.IsLocked,
                sample.Quality,
                sample.Container);

        private static LandClaimMapFeature ToFeature(LandClaimSummary sample) =>
            new LandClaimMapFeature(
                sample.ServerId,
                sample.StableIdentity,
                sample.Position,
                sample.OwnerStableIdentity,
                sample.ProtectionRadius,
                sample.IsValid,
                sample.OwnerLastLoginUtc);

        private static VehicleMapFeature ToFeature(VehicleSummary sample) =>
            new VehicleMapFeature(
                sample.ServerId,
                sample.StableIdentity,
                sample.Position,
                null,
                sample.EntityTypeResourceId,
                sample.OwnerStableIdentity,
                sample.LoadState,
                sample.FuelPercentage,
                sample.Quality,
                sample.IsLocked,
                sample.Container?.UsedSlotCount,
                sample.Container);

        private static DroneMapFeature ToFeature(DroneSummary sample) =>
            new DroneMapFeature(
                sample.ServerId,
                sample.StableIdentity,
                sample.Position,
                sample.EntityTypeResourceId,
                sample.OwnerStableIdentity,
                sample.LoadState,
                sample.IsLocked,
                sample.Quality,
                sample.Container);

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
            public static PublishedSnapshot Empty { get; } = new PublishedSnapshot(
                LayerState.Unavailable(),
                LayerState.Unavailable(),
                LayerState.Unavailable(),
                LayerState.Unavailable());

            public PublishedSnapshot(
                LayerState traders,
                LayerState landClaims,
                LayerState vehicles,
                LayerState drones)
            {
                Traders = traders;
                LandClaims = landClaims;
                Vehicles = vehicles;
                Drones = drones;
            }

            public LayerState Traders { get; }
            public LayerState LandClaims { get; }
            public LayerState Vehicles { get; }
            public LayerState Drones { get; }

            public LayerState Get(MapLayerKind layer)
            {
                switch (layer)
                {
                    case MapLayerKind.Traders: return Traders;
                    case MapLayerKind.LandClaims: return LandClaims;
                    case MapLayerKind.Vehicles: return Vehicles;
                    case MapLayerKind.Drones: return Drones;
                    default: throw new ArgumentOutOfRangeException(nameof(layer));
                }
            }

            public PublishedSnapshot AsStale() =>
                new PublishedSnapshot(
                    Traders.AsStale(),
                    LandClaims.AsStale(),
                    Vehicles.AsStale(),
                    Drones.AsStale());
        }

        private sealed class LayerState
        {
            private LayerState(
                AvailabilityState availability,
                DateTimeOffset? observedAtUtc,
                MapLayerFeature[] features)
            {
                Availability = availability;
                ObservedAtUtc = observedAtUtc;
                Features = features;
            }

            public AvailabilityState Availability { get; }
            public DateTimeOffset? ObservedAtUtc { get; }
            public MapLayerFeature[] Features { get; }

            public static LayerState Available(
                DateTimeOffset observedAtUtc,
                IEnumerable<MapLayerFeature> features) =>
                new LayerState(AvailabilityState.Available, observedAtUtc, features.ToArray());

            public static LayerState Unavailable() =>
                new LayerState(AvailabilityState.Unavailable, null, Array.Empty<MapLayerFeature>());

            public LayerState AsStale() =>
                Availability == AvailabilityState.Unavailable || !ObservedAtUtc.HasValue
                    ? this
                    : new LayerState(AvailabilityState.Stale, ObservedAtUtc, Features);
        }
    }
}
