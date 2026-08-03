using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class WorldReadUseCaseTests
    {
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 27, 1, 2, 3, TimeSpan.Zero);

        [Fact]
        public void Query_world_returns_independently_available_sources_and_copied_fields()
        {
            var itemSources = new List<ApprovedWorldItemSummary>
            {
                new ApprovedWorldItemSummary("resource-1", 4, quality: 6)
            };
            var container = new ContainerSummary(
                "container-10",
                "vehicle:10:storage",
                "vehicle:10",
                new MapLayerPosition(10, 11, 12),
                MapEntityLoadState.Loaded,
                isLocked: true,
                slotCount: 12,
                usedSlotCount: 1,
                itemSources);
            var vehicleSources = new List<VehicleSummary>
            {
                new VehicleSummary(
                    "10",
                    "vehicle:10",
                    "entity-resource-4x4",
                    "EOS_owner",
                    new MapLayerPosition(10, 11, 12),
                    MapEntityLoadState.Loaded,
                    isLocked: true,
                    fuelPercentage: 75,
                    quality: 6,
                    container)
            };
            var snapshot = new WorldSnapshot(
                new WorldSummary(
                    AvailabilityState.Available,
                    "world-guid",
                    "world-guid:42",
                    "seed",
                    width: 8192,
                    height: 8192,
                    gameVersion: "V 3.0.1 b4",
                    mapResourceVersion: "map-v2",
                    new MapExtent(-4096, -4096, 4096, 4096),
                    ObservedAtUtc),
                WorldCollectionSnapshot<LandClaimSummary>.Unavailable(),
                WorldCollectionSnapshot<VehicleSummary>.Available(ObservedAtUtc, vehicleSources),
                WorldCollectionSnapshot<DroneSummary>.Available(ObservedAtUtc, Array.Empty<DroneSummary>()),
                WorldCollectionSnapshot<ContainerSummary>.Available(ObservedAtUtc, new[] { container }));

            var result = new QueryWorldUseCase(new StubProjection(snapshot)).Execute();
            vehicleSources.Clear();
            itemSources.Clear();

            Assert.Equal("world-guid:42", result.World.WorldVersion);
            Assert.Equal("seed", result.World.Seed);
            Assert.Equal(8192, result.World.Width);
            Assert.Equal("map-v2", result.World.MapResourceVersion);
            Assert.Equal(AvailabilityState.Unavailable, result.LandClaims.SourceState);
            Assert.Empty(result.LandClaims.Items);
            var vehicle = Assert.Single(result.Vehicles.Items);
            Assert.Equal("entity-resource-4x4", vehicle.EntityTypeResourceId);
            Assert.Equal(75, vehicle.FuelPercentage);
            Assert.Equal(12, vehicle.Container!.SlotCount);
            Assert.Equal("resource-1", Assert.Single(vehicle.Container.Items!).ResourceId);
        }

        [Fact]
        public void World_tool_catalog_exposes_only_approved_identifiers_and_copies_sources()
        {
            var blocks = new List<string> { "cntStorageGeneric" };
            var prefabs = new List<string> { "prefab-resource-1" };
            var entityTypes = new List<string> { "entity-resource-1" };
            var snapshot = WorldToolCatalogSnapshot.Available(
                "catalog-v1",
                ObservedAtUtc,
                blocks,
                prefabs,
                entityTypes);
            var result = new QueryWorldToolCatalogUseCase(new StubCatalog(snapshot)).Execute();

            blocks[0] = "changed";
            prefabs.Clear();
            entityTypes.Clear();

            Assert.Equal("catalog-v1", result.CatalogVersion);
            Assert.Equal("cntStorageGeneric", Assert.Single(result.BlockInternalNames));
            Assert.Equal("prefab-resource-1", Assert.Single(result.PrefabResourceIds));
            Assert.Equal("entity-resource-1", Assert.Single(result.EntityTypeResourceIds));
            Assert.DoesNotContain("\\", Assert.Single(result.PrefabResourceIds));
            Assert.DoesNotContain("/", Assert.Single(result.PrefabResourceIds));
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class StubProjection : IWorldSnapshotProjection
        {
            private readonly WorldSnapshot snapshot;

            public StubProjection(WorldSnapshot snapshot) => this.snapshot = snapshot;

            public WorldSnapshot Query() => snapshot;
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class StubCatalog : IWorldToolCatalog
        {
            private readonly WorldToolCatalogSnapshot snapshot;

            public StubCatalog(WorldToolCatalogSnapshot snapshot) => this.snapshot = snapshot;

            public WorldToolCatalogSnapshot Read() => snapshot;
        }
    }
}
