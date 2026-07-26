using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysMapLayerProjectionTests
    {
        [Fact]
        public void Publish_copies_all_approved_fields_with_one_observation_time()
        {
            var observedAtUtc = Utc(1);
            var projection = new SevenDaysMapLayerProjection();
            projection.Publish(new SevenDaysMapLayerSample(
                new[]
                {
                    new SevenDaysTraderMapSample(
                        "trader-1", "Trader Jen", 10, 11, 12, isOpen: true,
                        new MapLayerBounds(0, 2, 20, 22), protectionRadius: 15)
                },
                new[]
                {
                    new SevenDaysLandClaimMapSample(
                        "claim-1", 20, 21, 22, "EOS_owner", protectionRadius: 41,
                        isValid: true, ownerLastLoginUtc: Utc(0))
                },
                new[]
                {
                    new SevenDaysVehicleMapSample(
                        "vehicle-1", 30, 31, 32, "4x4", "EOS_owner",
                        MapEntityLoadState.Unloaded, fuelPercentage: null, quality: 6,
                        isLocked: null, storageItemCount: 7)
                },
                new[]
                {
                    new SevenDaysDroneMapSample(
                        "drone-1", 40, 41, 42, "EOS_owner", MapEntityLoadState.Loaded)
                }), observedAtUtc);

            var trader = Assert.IsType<TraderMapFeature>(Single(projection, MapLayerKind.Traders));
            Assert.True(trader.IsOpen);
            Assert.Equal("Trader Jen", trader.Name);
            Assert.Equal(15, trader.ProtectionRadius);
            Assert.Equal(new MapLayerBounds(0, 2, 20, 22), trader.PrefabBounds);

            var claim = Assert.IsType<LandClaimMapFeature>(Single(projection, MapLayerKind.LandClaims));
            Assert.Equal("EOS_owner", claim.OwnerCrossplatformId);
            Assert.True(claim.IsValid);
            Assert.Equal(Utc(0), claim.OwnerLastLoginUtc);

            var vehicle = Assert.IsType<VehicleMapFeature>(Single(projection, MapLayerKind.Vehicles));
            Assert.Equal(MapEntityLoadState.Unloaded, vehicle.LoadState);
            Assert.Null(vehicle.FuelPercentage);
            Assert.Null(vehicle.IsLocked);
            Assert.Equal(7, vehicle.StorageItemCount);
            Assert.Equal("EOS_owner", vehicle.OwnerCrossplatformId);

            var drone = Assert.IsType<DroneMapFeature>(Single(projection, MapLayerKind.Drones));
            Assert.Equal(MapEntityLoadState.Loaded, drone.LoadState);
            Assert.Equal("EOS_owner", drone.OwnerCrossplatformId);

            foreach (var layer in Enum.GetValues(typeof(MapLayerKind)).Cast<MapLayerKind>())
                Assert.Equal(observedAtUtc, Query(projection, layer).ObservedAtUtc);
        }

        [Fact]
        public void Publish_and_query_do_not_retain_mutable_sources_and_are_extent_bounded()
        {
            var vehicles = new List<SevenDaysVehicleMapSample>
            {
                new SevenDaysVehicleMapSample(
                    "inside", 1, 2, 3, null, null, MapEntityLoadState.Loaded,
                    null, null, null, null),
                new SevenDaysVehicleMapSample(
                    "outside", 200, 2, 200, null, null, MapEntityLoadState.Unloaded,
                    null, null, null, null)
            };
            var sample = new SevenDaysMapLayerSample(
                Array.Empty<SevenDaysTraderMapSample>(),
                Array.Empty<SevenDaysLandClaimMapSample>(),
                vehicles,
                Array.Empty<SevenDaysDroneMapSample>());
            var projection = new SevenDaysMapLayerProjection();
            projection.Publish(sample, Utc(1));

            vehicles.Clear();
            var result = Query(projection, MapLayerKind.Vehicles, new MapExtent(-10, -10, 10, 10));

            Assert.Equal("inside", Assert.Single(result.Features).Id);
        }

        [Fact]
        public void Query_reports_zoom_threshold_and_rejects_matching_results_above_limit()
        {
            var projection = new SevenDaysMapLayerProjection();
            projection.Publish(new SevenDaysMapLayerSample(
                Array.Empty<SevenDaysTraderMapSample>(),
                Array.Empty<SevenDaysLandClaimMapSample>(),
                new[]
                {
                    new SevenDaysVehicleMapSample("a", 1, 2, 3, null, null, MapEntityLoadState.Loaded, null, null, null, null),
                    new SevenDaysVehicleMapSample("b", 4, 5, 6, null, null, MapEntityLoadState.Unloaded, null, null, null, null)
                },
                Array.Empty<SevenDaysDroneMapSample>()), Utc(1));

            var zoomedOut = Query(projection, MapLayerKind.Vehicles, zoom: 0, limit: 10);
            Assert.False(zoomedOut.IsZoomSufficient);
            Assert.Empty(zoomedOut.Features);

            Assert.Throws<MapLayerLimitExceededException>(() =>
                Query(projection, MapLayerKind.Vehicles, zoom: 3, limit: 1));
        }

        [Fact]
        public void Invalid_coordinates_and_non_utc_observation_times_are_rejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MapLayerPosition(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SevenDaysDroneMapSample(
                    "drone", float.PositiveInfinity, 0, 0, null, MapEntityLoadState.Loaded));

            var projection = new SevenDaysMapLayerProjection();
            Assert.Throws<ArgumentOutOfRangeException>(() => projection.Publish(
                SevenDaysMapLayerSample.Empty,
                new DateTimeOffset(2026, 7, 26, 1, 0, 0, TimeSpan.FromHours(8))));
        }

        [Fact]
        public void Capture_failure_keeps_only_an_immutable_stale_snapshot_and_clear_removes_it()
        {
            var projection = new SevenDaysMapLayerProjection();
            projection.Publish(new SevenDaysMapLayerSample(
                Array.Empty<SevenDaysTraderMapSample>(),
                Array.Empty<SevenDaysLandClaimMapSample>(),
                new[]
                {
                    new SevenDaysVehicleMapSample("vehicle", 1, 2, 3, null, null, MapEntityLoadState.Loaded, null, null, null, null)
                },
                Array.Empty<SevenDaysDroneMapSample>()), Utc(1));

            projection.MarkCaptureFailed();
            var stale = Query(projection, MapLayerKind.Vehicles);
            Assert.Equal(AvailabilityState.Stale, stale.Availability);
            Assert.Equal("vehicle", Assert.Single(stale.Features).Id);

            projection.Clear();
            var unavailable = Query(projection, MapLayerKind.Vehicles);
            Assert.Equal(AvailabilityState.Unavailable, unavailable.Availability);
            Assert.Empty(unavailable.Features);
        }

        private static MapLayerFeature Single(
            SevenDaysMapLayerProjection projection,
            MapLayerKind layer) =>
            Assert.Single(Query(projection, layer).Features);

        private static MapLayerProjectionSnapshot Query(
            SevenDaysMapLayerProjection projection,
            MapLayerKind layer,
            MapExtent? extent = null,
            int zoom = 3,
            int limit = 10) =>
            projection.Query(new MapLayerQuery(
                layer,
                extent ?? new MapExtent(-100, -100, 100, 100),
                zoom,
                limit));

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, minute, 0, TimeSpan.Zero);
    }
}
