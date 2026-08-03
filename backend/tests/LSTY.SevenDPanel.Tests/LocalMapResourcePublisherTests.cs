using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LSTY.SevenDPanel.Adapters.Local.MapTiles;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class LocalMapResourcePublisherTests
    {
        private static readonly byte[] OnePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

        [Fact]
        public void Safe_publication_switches_to_a_new_opaque_resource_version()
        {
            using var fixture = new PublisherFixture();
            var publisher = fixture.CreatePublisher();
            var firstStage = fixture.CreateStage("world-guid", "0/0/0.png", OnePixelPng);

            var first = publisher.Publish("world-guid", firstStage);
            var secondStage = fixture.CreateStage("world-guid", "0/1/0.png", OnePixelPng);
            var second = publisher.Publish("world-guid", secondStage);

            Assert.Equal("world-guid", second.WorldId);
            Assert.Equal(1, second.TileSize);
            Assert.StartsWith("map-", first.MapResourceVersion, StringComparison.Ordinal);
            Assert.StartsWith("map-", second.MapResourceVersion, StringComparison.Ordinal);
            Assert.NotEqual(first.MapResourceVersion, second.MapResourceVersion);
            Assert.DoesNotContain("world-guid", second.MapResourceVersion, StringComparison.Ordinal);
            Assert.Same(second, publisher.Current);
            Assert.True(Directory.Exists(first.RootPath));
            Assert.True(Directory.Exists(second.RootPath));
            Assert.False(Directory.Exists(firstStage));
            Assert.False(Directory.Exists(secondStage));
            Assert.True(File.Exists(Path.Combine(second.RootPath, "0", "1", "0.png")));
        }

        [Fact]
        public void Invalid_manifest_path_preserves_the_previous_publication()
        {
            using var fixture = new PublisherFixture();
            var publisher = fixture.CreatePublisher();
            var previous = publisher.Publish(
                "world-guid",
                fixture.CreateStage("world-guid", "0/0/0.png", OnePixelPng));
            var invalidStage = fixture.CreateStage(
                "world-guid",
                "../outside.png",
                OnePixelPng,
                writeTile: false);

            var exception = Assert.Throws<LocalMapResourcePublishException>(() =>
                publisher.Publish("world-guid", invalidStage));

            Assert.Equal(LocalMapResourcePublisher.PathInvalid, exception.ErrorCode);
            Assert.Same(previous, publisher.Current);
            Assert.True(Directory.Exists(previous.RootPath));
            Assert.True(Directory.Exists(invalidStage));
        }

        [Theory]
        [InlineData("0/0/0.jpg", true)]
        [InlineData("0/0/0.png", false)]
        public void Invalid_tile_extension_or_content_preserves_the_previous_publication(
            string relativePath,
            bool useValidPng)
        {
            using var fixture = new PublisherFixture();
            var publisher = fixture.CreatePublisher();
            var previous = publisher.Publish(
                "world-guid",
                fixture.CreateStage("world-guid", "0/0/0.png", OnePixelPng));
            var content = useValidPng ? OnePixelPng : new byte[] { 1, 2, 3, 4 };
            var invalidStage = fixture.CreateStage("world-guid", relativePath, content);

            var exception = Assert.Throws<LocalMapResourcePublishException>(() =>
                publisher.Publish("world-guid", invalidStage));

            Assert.Equal(LocalMapResourcePublisher.TileInvalid, exception.ErrorCode);
            Assert.Same(previous, publisher.Current);
            Assert.True(Directory.Exists(previous.RootPath));
            Assert.True(Directory.Exists(invalidStage));
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class PublisherFixture : IDisposable
        {
            private readonly string root = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-map-publisher-tests",
                Guid.NewGuid().ToString("N"));

            public PublisherFixture()
            {
                TemporaryRoot = Path.Combine(root, "temporary");
                PublishedRoot = Path.Combine(root, "published");
                Directory.CreateDirectory(TemporaryRoot);
                Directory.CreateDirectory(PublishedRoot);
            }

            public string TemporaryRoot { get; }

            public string PublishedRoot { get; }

            public LocalMapResourcePublisher CreatePublisher() =>
                new LocalMapResourcePublisher(TemporaryRoot, PublishedRoot);

            public string CreateStage(
                string worldId,
                string relativePath,
                byte[] content,
                bool writeTile = true)
            {
                var stage = Path.Combine(TemporaryRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                var pathSegments = relativePath.Split('/');
                var zoom = 0;
                var x = 0;
                var y = 0;
                var isCanonicalTilePath = pathSegments.Length == 3 &&
                    int.TryParse(pathSegments[0], NumberStyles.None, CultureInfo.InvariantCulture, out zoom) &&
                    int.TryParse(pathSegments[1], NumberStyles.None, CultureInfo.InvariantCulture, out x) &&
                    int.TryParse(Path.GetFileNameWithoutExtension(pathSegments[2]), NumberStyles.None, CultureInfo.InvariantCulture, out y);
                if (writeTile)
                {
                    var tilePath = Path.Combine(
                        stage,
                        relativePath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(tilePath)!);
                    File.WriteAllBytes(tilePath, content);
                }

                var manifest = "{" +
                    "\"schemaVersion\":1," +
                    "\"worldId\":\"" + worldId + "\"," +
                    "\"tileSize\":1," +
                    "\"tiles\":[{" +
                    "\"zoom\":" + (isCanonicalTilePath ? zoom : 0).ToString(CultureInfo.InvariantCulture) + "," +
                    "\"x\":" + (isCanonicalTilePath ? x : 0).ToString(CultureInfo.InvariantCulture) + "," +
                    "\"y\":" + (isCanonicalTilePath ? y : 0).ToString(CultureInfo.InvariantCulture) + "," +
                    "\"relativePath\":\"" + relativePath.Replace("\\", "\\\\") + "\"," +
                    "\"sizeBytes\":" + content.LongLength.ToString(CultureInfo.InvariantCulture) + "," +
                    "\"sha256\":\"" + Hash(content) + "\"}]}";
                File.WriteAllText(
                    Path.Combine(stage, LocalMapResourcePublisher.ManifestFileName),
                    manifest,
                    new UTF8Encoding(false));
                return stage;
            }

            public void Dispose()
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }

            private static string Hash(byte[] content)
            {
                using var algorithm = SHA256.Create();
                return string.Concat(algorithm.ComputeHash(content)
                    .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
    }
}
