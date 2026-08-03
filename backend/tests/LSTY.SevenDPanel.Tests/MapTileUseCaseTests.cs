using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class MapTileUseCaseTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("../world")]
        [InlineData("world/name")]
        [InlineData("world\\name")]
        [InlineData("world.name")]
        [InlineData("世界")]
        public void Tile_key_rejects_empty_or_path_capable_world_identifiers(string worldId)
        {
            Assert.Throws<ArgumentException>(() => new MapTileKey(worldId, 4, 0, 0));
        }

        [Fact]
        public void Tile_contract_contains_coordinates_but_no_browser_supplied_path()
        {
            var key = new MapTileKey("world-guid", 4, -2, 3);

            Assert.Equal("world-guid", key.WorldId);
            Assert.Equal(4, key.Zoom);
            Assert.Equal(-2, key.X);
            Assert.Equal(3, key.Y);
            Assert.DoesNotContain(
                typeof(MapTileKey).GetProperties(),
                property => property.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new MapTileKey("world-guid", -1, 0, 0));
        }

        [Fact]
        public async Task Use_case_accepts_only_tiles_intersecting_the_metadata_grid()
        {
            var store = new RecordingTileStore(MapTileReadResult.Missing());
            var useCase = CreateUseCase(store);

            await useCase.ExecuteAsync(new MapTileKey("world-guid", 4, -4, -4), CancellationToken.None);
            await useCase.ExecuteAsync(new MapTileKey("world-guid", 4, 3, 3), CancellationToken.None);
            await useCase.ExecuteAsync(new MapTileKey("world-guid", 2, -1, 0), CancellationToken.None);

            Assert.Equal(3, store.ReadCount);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                useCase.ExecuteAsync(new MapTileKey("other-world", 4, 0, 0), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                useCase.ExecuteAsync(new MapTileKey("world-guid", 5, 0, 0), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                useCase.ExecuteAsync(new MapTileKey("world-guid", 4, -5, 0), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                useCase.ExecuteAsync(new MapTileKey("world-guid", 4, 4, 0), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                useCase.ExecuteAsync(new MapTileKey("world-guid", 2, 0, 1), CancellationToken.None));
            Assert.Equal(3, store.ReadCount);
        }

        [Fact]
        public async Task Unavailable_metadata_returns_unavailable_without_touching_storage()
        {
            var store = new RecordingTileStore(MapTileReadResult.Missing());
            var useCase = new GetMapTileUseCase(
                new MetadataQuery(MapMetadataProjectionSnapshot.Unavailable()),
                store);

            var result = await useCase.ExecuteAsync(
                new MapTileKey("world-guid", 4, 0, 0),
                CancellationToken.None);

            Assert.Equal(MapTileReadStatus.Unavailable, result.Status);
            Assert.Equal(0, store.ReadCount);
        }

        [Fact]
        public async Task Use_case_preserves_typed_content_etag_and_real_resource_version()
        {
            var expected = MapTileReadResult.Available(
                new byte[] { 1, 2, 3 },
                "image/png",
                "\"etag-1\"",
                "map-generation-7");
            var useCase = CreateUseCase(new RecordingTileStore(expected));

            var result = await useCase.ExecuteAsync(
                new MapTileKey("world-guid", 4, 0, 0),
                CancellationToken.None);

            Assert.Same(expected, result);
            Assert.Equal(MapTileReadStatus.Available, result.Status);
            Assert.Equal(new byte[] { 1, 2, 3 }, result.Content);
            Assert.Equal("image/png", result.ContentType);
            Assert.Equal("\"etag-1\"", result.ETag);
            Assert.Equal("map-generation-7", result.ResourceVersion);
        }

        private static GetMapTileUseCase CreateUseCase(IMapTileStore store) =>
            new GetMapTileUseCase(
                new MetadataQuery(MapMetadataProjectionSnapshot.Available(
                    "world-guid",
                    new MapMetadata(
                        "Navezgane",
                        new MapExtent(-512, -512, 512, 512),
                        new MapAxisConvention("east", "north"),
                        new[] { 2, 3, 4 },
                        128,
                        null),
                    new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero))),
                store);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class MetadataQuery : IMapMetadataQuery
        {
            private readonly MapMetadataProjectionSnapshot snapshot;

            public MetadataQuery(MapMetadataProjectionSnapshot snapshot) => this.snapshot = snapshot;

            public MapMetadataProjectionSnapshot Query() => snapshot;
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingTileStore : IMapTileStore
        {
            private readonly MapTileReadResult result;

            public RecordingTileStore(MapTileReadResult result) => this.result = result;

            public int ReadCount { get; private set; }

            public Task<MapTileReadResult> ReadAsync(MapTileKey key, CancellationToken cancellationToken)
            {
                ReadCount++;
                return Task.FromResult(result);
            }
        }
    }
}
