using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysWorldSnapshotProjectionTests
    {
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 27, 2, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Capture_reads_only_inside_dispatch_and_publishes_product_scalars()
        {
            var insideDispatch = false;
            var projection = new SevenDaysWorldSnapshotProjection(
                (_, action, _, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    insideDispatch = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatch = false; }
                },
                () =>
                {
                    Assert.True(insideDispatch);
                    return CreateScalarSnapshot();
                });

            var scalar = await projection.CaptureAsync(TestContext.Current.CancellationToken);
            projection.Publish(scalar, ObservedAtUtc);
            var result = projection.Query();

            Assert.Equal(AvailabilityState.Available, result.World.SourceState);
            Assert.Equal("world-guid:42", result.World.WorldVersion);
            Assert.Equal(ObservedAtUtc, result.World.ObservedAtUtc);
            Assert.Equal("EOS_owner", Assert.Single(result.LandClaims.Items).OwnerStableIdentity);
            Assert.Equal(7, Assert.Single(result.Vehicles.Items).Container!.UsedSlotCount);
            Assert.Equal("EOS_owner", Assert.Single(result.Drones.Items).OwnerStableIdentity);
        }

        [Fact]
        public void A_failed_source_is_unavailable_without_hiding_other_collections()
        {
            var projection = new SevenDaysWorldSnapshotProjection(
                (_, action, _, _) => Task.FromResult(action()),
                () => CreateScalarSnapshot());
            var scalar = CreateScalarSnapshot(landClaimsCaptureFailed: true);

            projection.Publish(scalar, ObservedAtUtc);
            var result = projection.Query();

            Assert.Equal(AvailabilityState.Unavailable, result.LandClaims.SourceState);
            Assert.Empty(result.LandClaims.Items);
            Assert.Equal(AvailabilityState.Available, result.Vehicles.SourceState);
            Assert.Single(result.Vehicles.Items);
            Assert.Equal(AvailabilityState.Available, result.Drones.SourceState);
            Assert.Single(result.Containers.Items);
        }

        [Fact]
        public void Tool_catalog_publishes_only_block_names_and_opaque_resource_ids()
        {
            var catalog = new SevenDaysWorldToolCatalog();
            catalog.Publish(CreateScalarSnapshot(), ObservedAtUtc);

            var result = catalog.Read();

            Assert.Equal(AvailabilityState.Available, result.SourceState);
            Assert.Equal("cntStorageGeneric", Assert.Single(result.BlockInternalNames));
            Assert.Equal("prefab-resource-1", Assert.Single(result.PrefabResourceIds));
            Assert.Equal("entity-resource-1", Assert.Single(result.EntityTypeResourceIds));
        }

        private static SevenDaysWorldScalarSnapshot CreateScalarSnapshot(
            bool landClaimsCaptureFailed = false)
        {
            var container = new ContainerSummary(
                "container-10",
                "vehicle:10:storage",
                "vehicle:10",
                new MapLayerPosition(10, 11, 12),
                MapEntityLoadState.Loaded,
                isLocked: true,
                slotCount: 12,
                usedSlotCount: 7,
                new[] { new ApprovedWorldItemSummary("resource-1", 4, 6) });
            return new SevenDaysWorldScalarSnapshot(
                new SevenDaysMapSample(
                    new SevenDaysMapMetadataSample(
                        "Navezgane", "world-guid", -4096, -4096, 4096, 4096, 128, 5),
                    new SevenDaysMapGameTimeSample(4, 13, 27)),
                new SevenDaysWorldScalar(
                    "world-guid",
                    "world-guid:42",
                    "seed",
                    8192,
                    8192,
                    "V 3.0.1 b4",
                    "map-v2",
                    new MapExtent(-4096, -4096, 4096, 4096)),
                new[]
                {
                    new LandClaimSummary(
                        "claim-1", "claim:EOS_owner:1:2:3", new MapLayerPosition(1, 2, 3),
                        "EOS_owner", 41, isValid: true, ownerLastLoginUtc: ObservedAtUtc.AddDays(-1))
                },
                new[]
                {
                    new VehicleSummary(
                        "10", "vehicle:10", "entity-resource-1", "EOS_owner",
                        new MapLayerPosition(10, 11, 12), MapEntityLoadState.Loaded,
                        isLocked: true, fuelPercentage: 75, quality: 6, container)
                },
                new[]
                {
                    new DroneSummary(
                        "20", "drone:20", "entity-resource-1", "EOS_owner",
                        new MapLayerPosition(20, 21, 22), MapEntityLoadState.Loaded,
                        isLocked: false, quality: 5, container: null)
                },
                new[] { container },
                new[] { "cntStorageGeneric" },
                new[] { "prefab-resource-1" },
                new[] { "entity-resource-1" },
                landClaimsCaptureFailed: landClaimsCaptureFailed);
        }
    }
}
