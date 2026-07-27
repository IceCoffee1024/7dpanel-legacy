using System;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class RegionOperationUseCaseTests
    {
        [Fact]
        public void Region_normalizes_coordinates_and_rejects_excessive_volume()
        {
            var region = new WorldRegion(
                new WorldCoordinate(5, 6, 7),
                new WorldCoordinate(1, 2, 3));

            Assert.Equal(1d, region.Minimum.X);
            Assert.Equal(2d, region.Minimum.Y);
            Assert.Equal(3d, region.Minimum.Z);
            Assert.Equal(5d, region.Maximum.X);
            Assert.Equal(6d, region.Maximum.Y);
            Assert.Equal(7d, region.Maximum.Z);
            Assert.Equal(125, region.Volume);
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldRegion(
                new WorldCoordinate(0, 0, 0),
                new WorldCoordinate(100, 100, 100)));
        }

        [Fact]
        public void Four_use_cases_enqueue_only_their_closed_region_targets()
        {
            var bridge = new RecordingBridge();
            var metadata = new RecordingMetadataStore(Source("world-1"));
            var region = Region();

            new CopyRegionUseCase(bridge).Execute(new CopyRegionRequest(
                "owner", "world-1", "world-v1", "map-v1", region,
                "copy-1", true, Utc()));
            AssertTarget(bridge.Intent, WorldOperationKind.CopyRegion, null, null, reversible: false);

            new FillRegionUseCase(bridge, new FixedCatalog()).Execute(new FillRegionRequest(
                "owner", "world-1", "world-v1", "map-v1", region,
                "catalog-v1", "steelBlock", "fill-1", true, true, Utc()));
            AssertTarget(
                bridge.Intent,
                WorldOperationKind.FillRegion,
                null,
                "steelBlock",
                reversible: true);

            new ClearRegionUseCase(bridge).Execute(new ClearRegionRequest(
                "owner", "world-1", "world-v1", "map-v1", region,
                "clear-1", true, true, Utc()));
            AssertTarget(bridge.Intent, WorldOperationKind.ClearRegion, null, null, reversible: true);

            new PasteRegionUseCase(bridge, metadata).Execute(new PasteRegionRequest(
                "owner", "world-1", "world-v1", "map-v1", region,
                "change-set-source", "paste-1", true, true, Utc()));
            AssertTarget(
                bridge.Intent,
                WorldOperationKind.PasteRegion,
                "change-set-source",
                null,
                reversible: true);
        }

        [Fact]
        public void Mutating_region_operations_require_strong_confirmation_and_paste_rejects_cross_world()
        {
            var bridge = new RecordingBridge();
            var region = Region();

            Assert.Throws<WorldOperationStrongConfirmationRequiredException>(() =>
                new ClearRegionUseCase(bridge).Execute(new ClearRegionRequest(
                    "owner", "world-1", "world-v1", null, region,
                    "clear-1", true, false, Utc())));
            Assert.Null(bridge.Intent);

            var useCase = new PasteRegionUseCase(
                bridge,
                new RecordingMetadataStore(Source("world-2")));
            Assert.Throws<WorldOperationConflictException>(() => useCase.Execute(
                new PasteRegionRequest(
                    "owner", "world-1", "world-v1", null, region,
                    "change-set-source", "paste-1", true, true, Utc())));
            Assert.Null(bridge.Intent);
        }

        private static void AssertTarget(
            WorldOperationIntent? intent,
            WorldOperationKind kind,
            string? sourceChangeSetId,
            string? blockInternalName,
            bool reversible)
        {
            Assert.NotNull(intent);
            Assert.Equal(kind, intent!.Kind);
            Assert.Equal(reversible, intent.IsReversible);
            var target = Assert.IsType<WorldRegionOperationTarget>(intent.Target);
            Assert.Equal(1, target.MinimumX);
            Assert.Equal(2, target.MinimumY);
            Assert.Equal(3, target.MinimumZ);
            Assert.Equal(2, target.MaximumX);
            Assert.Equal(3, target.MaximumY);
            Assert.Equal(4, target.MaximumZ);
            Assert.Equal(sourceChangeSetId, target.SourceChangeSetId);
            Assert.Equal(blockInternalName, target.BlockInternalName);
        }

        private static WorldRegion Region() =>
            new WorldRegion(
                new WorldCoordinate(2, 3, 4),
                new WorldCoordinate(1, 2, 3));

        private static WorldChangeSetDescriptor Source(string worldId) =>
            new WorldChangeSetDescriptor(
                "change-set-source",
                "copy-operation",
                worldId,
                "world-v1",
                Region(),
                new string('a', 64),
                new string('a', 64),
                "wcs-11111111111111111111111111111111",
                Utc(),
                Utc().AddDays(30));

        private static DateTimeOffset Utc() =>
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        private sealed class FixedCatalog : IWorldToolCatalog
        {
            public WorldToolCatalogSnapshot Read() =>
                WorldToolCatalogSnapshot.Available(
                    "catalog-v1",
                    Utc(),
                    new[] { "steelBlock" },
                    Array.Empty<string>(),
                    Array.Empty<string>());
        }

        private sealed class RecordingBridge : IWorldOperationJobBridge
        {
            public WorldOperationIntent? Intent { get; private set; }

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                Intent = intent;
                return new WorldOperationReceipt(
                    "operation-1",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId) => throw new NotSupportedException();
            public WorldOperationPage Query(WorldOperationQuery query) => throw new NotSupportedException();
            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }

        private sealed class RecordingMetadataStore : IWorldChangeSetMetadataStore
        {
            private readonly WorldChangeSetDescriptor source;

            public RecordingMetadataStore(WorldChangeSetDescriptor source) => this.source = source;

            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft) =>
                throw new NotSupportedException();

            public WorldChangeSetDescriptor Read(string changeSetId)
            {
                Assert.Equal(source.ChangeSetId, changeSetId);
                return source;
            }

            public void MarkApplied(string changeSetId, string afterHash) =>
                throw new NotSupportedException();
        }
    }
}
