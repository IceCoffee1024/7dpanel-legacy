using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysGameResourceCatalogTests
    {
        [Fact]
        public void Initial_read_is_building()
        {
            var catalog = CreateCatalog(EmptyDraft());

            var read = catalog.Read();

            Assert.Equal(GameResourceCatalogReadStatus.Building, read.Status);
            Assert.Null(read.Snapshot);
        }

        [Fact]
        public async Task Successful_build_atomically_publishes_an_immutable_available_snapshot()
        {
            using var directory = TestDirectory.Create();
            var iconRoot = directory.CreateDirectory("icons");
            File.WriteAllBytes(Path.Combine(iconRoot, "resourceRock.png"), new byte[] { 1, 2, 3 });
            var observed = new DateTimeOffset(2026, 7, 26, 1, 2, 3, TimeSpan.Zero);
            var draft = Draft(observed, iconRoot);
            using var indexingStarted = new ManualResetEventSlim();
            using var releaseIndexing = new ManualResetEventSlim();
            var catalog = new SevenDaysGameResourceCatalog(
                _ => Task.FromResult(draft),
                (captured, token) =>
                {
                    indexingStarted.Set();
                    releaseIndexing.Wait(token);
                    return GameResourceIconIndex.Build(captured.Resources, captured.IconRoots, token);
                });

            var building = catalog.BuildAsync(TestContext.Current.CancellationToken);
            Assert.True(indexingStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(GameResourceCatalogReadStatus.Building, catalog.Read().Status);

            releaseIndexing.Set();
            await building;

            var read = catalog.Read();
            Assert.Equal(GameResourceCatalogReadStatus.Available, read.Status);
            var snapshot = Assert.IsType<GameResourceCatalogSnapshot>(read.Snapshot);
            var resource = Assert.Single(snapshot.Resources);
            Assert.Equal("V3.0", snapshot.GameVersion);
            Assert.Equal(observed, snapshot.ObservedAtUtc);
            Assert.Equal("resourceRock", resource.InternalName);
            Assert.Equal(GameResourceKind.Item, resource.Kind);
            Assert.Equal(GameResourceVisibility.Public, resource.Visibility);
            Assert.Equal(GameResourceIconStatus.Available, resource.IconStatus);
            Assert.NotEmpty(resource.ResourceId);
            Assert.False(snapshot.Resources is GameResourceCatalogEntry[]);
            Assert.DoesNotContain(
                typeof(GameResourceCatalogEntry).GetProperties().Select(property => property.Name),
                name => name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(
                typeof(GameResourceCatalogSnapshot).GetProperties(),
                property => property.PropertyType == typeof(byte[]));
        }

        [Fact]
        public async Task Build_is_single_and_failures_publish_unavailable_without_path_details()
        {
            var calls = 0;
            var logs = new List<string>();
            var gate = new TaskCompletionSource<GameResourceScalarDraft>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var catalog = new SevenDaysGameResourceCatalog(
                _ =>
                {
                    Interlocked.Increment(ref calls);
                    return gate.Task;
                },
                (draft, token) => GameResourceIconIndex.Build(draft.Resources, draft.IconRoots, token),
                logs.Add);

            var first = catalog.BuildAsync(TestContext.Current.CancellationToken);
            var second = catalog.BuildAsync(TestContext.Current.CancellationToken);
            Assert.Same(first, second);
            gate.SetException(new IOException("C:\\secret\\ItemIcons"));
            await first;

            Assert.Equal(1, calls);
            Assert.Equal(GameResourceCatalogReadStatus.Unavailable, catalog.Read().Status);
            Assert.All(logs, log => Assert.DoesNotContain("secret", log, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Cancellation_does_not_publish_a_partial_or_unavailable_snapshot()
        {
            using var cancellation = new CancellationTokenSource();
            var catalog = new SevenDaysGameResourceCatalog(
                async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return EmptyDraft();
                },
                (draft, token) => GameResourceIconIndex.Build(draft.Resources, draft.IconRoots, token));

            var build = catalog.BuildAsync(cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
            Assert.Equal(GameResourceCatalogReadStatus.Building, catalog.Read().Status);
        }

        [Fact]
        public async Task Icon_read_validates_version_and_id_then_returns_png_with_a_versioned_etag()
        {
            using var directory = TestDirectory.Create();
            var root = directory.CreateDirectory("icons");
            var expected = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(Path.Combine(root, "resourceRock.png"), expected);
            var catalog = CreateCatalog(Draft(DateTimeOffset.UtcNow, root));
            await catalog.BuildAsync(TestContext.Current.CancellationToken);
            var snapshot = catalog.Read().Snapshot!;
            var resource = Assert.Single(snapshot.Resources);

            var available = await catalog.ReadIconAsync(
                snapshot.CatalogVersion,
                resource.ResourceId,
                TestContext.Current.CancellationToken);
            var oldVersion = await catalog.ReadIconAsync(
                "old-version",
                resource.ResourceId,
                TestContext.Current.CancellationToken);
            var unknownId = await catalog.ReadIconAsync(
                snapshot.CatalogVersion,
                "unknown",
                TestContext.Current.CancellationToken);

            Assert.Equal(GameResourceIconReadStatus.Available, available.Status);
            Assert.Equal(expected, available.Content);
            Assert.Equal("image/png", available.ContentType);
            Assert.StartsWith("\"", available.ETag!);
            Assert.EndsWith("\"", available.ETag!);
            Assert.Equal(GameResourceIconReadStatus.Missing, oldVersion.Status);
            Assert.Equal(GameResourceIconReadStatus.Missing, unknownId.Status);
        }

        [Fact]
        public async Task Deleted_or_replaced_icon_is_missing_and_etags_change_across_catalog_identity()
        {
            using var directory = TestDirectory.Create();
            var root = directory.CreateDirectory("icons");
            var iconPath = Path.Combine(root, "resourceRock.png");
            File.WriteAllBytes(iconPath, new byte[] { 1, 2, 3 });
            var firstCatalog = CreateCatalog(Draft(DateTimeOffset.UtcNow, root));
            await firstCatalog.BuildAsync(TestContext.Current.CancellationToken);
            var firstSnapshot = firstCatalog.Read().Snapshot!;
            var firstResource = Assert.Single(firstSnapshot.Resources);
            var firstRead = await firstCatalog.ReadIconAsync(
                firstSnapshot.CatalogVersion,
                firstResource.ResourceId,
                TestContext.Current.CancellationToken);

            File.WriteAllBytes(iconPath, new byte[] { 9, 8, 7, 6, 5 });
            File.SetLastWriteTimeUtc(iconPath, DateTime.UtcNow.AddSeconds(2));
            var replaced = await firstCatalog.ReadIconAsync(
                firstSnapshot.CatalogVersion,
                firstResource.ResourceId,
                TestContext.Current.CancellationToken);
            var secondCatalog = CreateCatalog(Draft(DateTimeOffset.UtcNow, root));
            await secondCatalog.BuildAsync(TestContext.Current.CancellationToken);
            var secondSnapshot = secondCatalog.Read().Snapshot!;
            var secondResource = Assert.Single(secondSnapshot.Resources);
            var secondRead = await secondCatalog.ReadIconAsync(
                secondSnapshot.CatalogVersion,
                secondResource.ResourceId,
                TestContext.Current.CancellationToken);
            File.Delete(iconPath);
            var deleted = await secondCatalog.ReadIconAsync(
                secondSnapshot.CatalogVersion,
                secondResource.ResourceId,
                TestContext.Current.CancellationToken);

            Assert.Equal(GameResourceIconReadStatus.Available, firstRead.Status);
            Assert.Equal(GameResourceIconReadStatus.Missing, replaced.Status);
            Assert.Equal(GameResourceIconReadStatus.Available, secondRead.Status);
            Assert.NotEqual(firstRead.ETag, secondRead.ETag);
            Assert.Equal(GameResourceIconReadStatus.Missing, deleted.Status);
        }

        private static SevenDaysGameResourceCatalog CreateCatalog(GameResourceScalarDraft draft) =>
            new SevenDaysGameResourceCatalog(
                _ => Task.FromResult(draft),
                (captured, token) => GameResourceIconIndex.Build(
                    captured.Resources,
                    captured.IconRoots,
                    token));

        private static GameResourceScalarDraft Draft(DateTimeOffset observed, string iconRoot) =>
            new GameResourceScalarDraft(
                "V3.0",
                observed,
                new[]
                {
                    new GameResourceScalarEntry(
                        1,
                        "resourceRock",
                        false,
                        true,
                        500,
                        false,
                        "resourceRock",
                        "A0B0C0",
                        "石头",
                        "Rock")
                },
                new[] { new GameResourceIconRootDescriptor(0, "base", iconRoot) },
                new[] { "draft-warning" });

        private static GameResourceScalarDraft EmptyDraft() =>
            new GameResourceScalarDraft(
                null,
                DateTimeOffset.UtcNow,
                Array.Empty<GameResourceScalarEntry>(),
                Array.Empty<GameResourceIconRootDescriptor>(),
                Array.Empty<string>());

        [Trait("Capability", "Players")]

        [Trait("Boundary", "SevenDays")]

        private sealed class TestDirectory : IDisposable
        {
            private static readonly string TestRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "7dpanel-game-resource-catalog-tests"));

            private TestDirectory(string path) => Path = path;

            public string Path { get; }

            public static TestDirectory Create()
            {
                Directory.CreateDirectory(TestRoot);
                var path = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(path);
                return new TestDirectory(path);
            }

            public string CreateDirectory(string leaf)
            {
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, leaf));
                AssertInside(path, Path);
                Directory.CreateDirectory(path);
                return path;
            }

            public void Dispose()
            {
                var target = System.IO.Path.GetFullPath(Path);
                AssertInside(target, TestRoot);
                if (Directory.Exists(target)) Directory.Delete(target, true);
            }

            private static void AssertInside(string target, string root)
            {
                var normalizedRoot = System.IO.Path.GetFullPath(root)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) +
                    System.IO.Path.DirectorySeparatorChar;
                Assert.StartsWith(normalizedRoot, target, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
