using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.MapTiles;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class LocalMapTileStoreTests
    {
        [Fact]
        public async Task Reads_only_the_controlled_native_tile_path_and_returns_stable_content_etag()
        {
            using var fixture = new TileFixture();
            var expected = new byte[] { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3 };
            fixture.WriteTile(4, -2, 3, ".png", expected);
            var store = fixture.CreateStore("published-generation-3");

            var first = await store.ReadAsync(
                new MapTileKey("world-guid", 4, -2, 3),
                CancellationToken.None);
            var second = await store.ReadAsync(
                new MapTileKey("world-guid", 4, -2, 3),
                CancellationToken.None);

            Assert.Equal(MapTileReadStatus.Available, first.Status);
            Assert.Equal(expected, first.Content);
            Assert.Equal("image/png", first.ContentType);
            Assert.Equal(first.ETag, second.ETag);
            Assert.StartsWith("\"", first.ETag, StringComparison.Ordinal);
            Assert.EndsWith("\"", first.ETag, StringComparison.Ordinal);
            Assert.Equal("published-generation-3", first.ResourceVersion);
            Assert.DoesNotContain(
                first.GetType().GetProperties(),
                property => property.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public async Task Content_change_changes_etag_without_using_a_fake_resource_version()
        {
            using var fixture = new TileFixture();
            fixture.WriteTile(4, 0, 0, ".png", new byte[] { 1, 2, 3 });
            var store = fixture.CreateStore(null);
            var key = new MapTileKey("world-guid", 4, 0, 0);

            var first = await store.ReadAsync(key, CancellationToken.None);
            fixture.WriteTile(4, 0, 0, ".png", new byte[] { 1, 2, 4 });
            var second = await store.ReadAsync(key, CancellationToken.None);

            Assert.NotEqual(first.ETag, second.ETag);
            Assert.Null(first.ResourceVersion);
            Assert.Null(second.ResourceVersion);
        }

        [Fact]
        public async Task Approves_webp_but_does_not_serve_unapproved_extensions()
        {
            using var fixture = new TileFixture();
            fixture.WriteTile(3, 1, -1, ".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 });
            fixture.WriteTile(3, 2, -1, ".jpg", new byte[] { 0xff, 0xd8 });
            var store = fixture.CreateStore("generation-1");

            var webp = await store.ReadAsync(
                new MapTileKey("world-guid", 3, 1, -1),
                CancellationToken.None);
            var jpeg = await store.ReadAsync(
                new MapTileKey("world-guid", 3, 2, -1),
                CancellationToken.None);

            Assert.Equal(MapTileReadStatus.Available, webp.Status);
            Assert.Equal("image/webp", webp.ContentType);
            Assert.Equal(MapTileReadStatus.Missing, jpeg.Status);
        }

        [Fact]
        public async Task Missing_root_wrong_world_and_missing_tile_are_explicit_and_non_destructive()
        {
            using var fixture = new TileFixture();
            fixture.WriteTile(4, 0, 0, ".png", new byte[] { 1 });
            var existingPath = fixture.TilePath(4, 0, 0, ".png");
            var store = fixture.CreateStore("generation-1");

            var wrongWorld = await store.ReadAsync(
                new MapTileKey("other-world", 4, 0, 0),
                CancellationToken.None);
            var missing = await store.ReadAsync(
                new MapTileKey("world-guid", 4, 1, 0),
                CancellationToken.None);
            var unavailable = await new LocalMapTileStore(() => null).ReadAsync(
                new MapTileKey("world-guid", 4, 0, 0),
                CancellationToken.None);

            Assert.Equal(MapTileReadStatus.Unavailable, wrongWorld.Status);
            Assert.Equal(MapTileReadStatus.Missing, missing.Status);
            Assert.Equal(MapTileReadStatus.Unavailable, unavailable.Status);
            Assert.True(File.Exists(existingPath));
        }

        [Fact]
        public async Task File_read_runs_on_a_worker_and_honors_precancelled_requests()
        {
            using var fixture = new TileFixture();
            fixture.WriteTile(4, 0, 0, ".png", Enumerable.Repeat((byte)7, 1024).ToArray());
            var callerThread = Environment.CurrentManagedThreadId;
            var providerThread = callerThread;
            var store = new LocalMapTileStore(() =>
            {
                providerThread = Environment.CurrentManagedThreadId;
                return new LocalMapTileRoot("world-guid", fixture.RootPath, "generation-1");
            });

            var result = await store.ReadAsync(
                new MapTileKey("world-guid", 4, 0, 0),
                CancellationToken.None);

            Assert.Equal(MapTileReadStatus.Available, result.Status);
            Assert.NotEqual(callerThread, providerThread);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.ReadAsync(
                    new MapTileKey("world-guid", 4, 0, 0),
                    cancellation.Token));
        }

        private sealed class TileFixture : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-map-tile-tests",
                Guid.NewGuid().ToString("N"));

            public TileFixture() => Directory.CreateDirectory(directory);

            public string RootPath => directory;

            public LocalMapTileStore CreateStore(string? resourceVersion) =>
                new LocalMapTileStore(() => new LocalMapTileRoot(
                    "world-guid",
                    directory,
                    resourceVersion));

            public void WriteTile(int zoom, int x, int y, string extension, byte[] content)
            {
                var path = TilePath(zoom, x, y, extension);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
            }

            public string TilePath(int zoom, int x, int y, string extension) =>
                Path.Combine(directory, zoom.ToString(), x.ToString(), y + extension);

            public void Dispose()
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
