using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class GameResourceIconIndexTests
    {
        [Fact]
        public void Indexes_only_ordinary_top_level_png_files()
        {
            using var directory = TestDirectory.Create();
            var root = directory.CreateDirectory("icons");
            File.WriteAllBytes(Path.Combine(root, "resourceRock.PNG"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(root, "resourceWood.jpg"), "not png");
            Directory.CreateDirectory(Path.Combine(root, "resourceDirectory.png"));
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            File.WriteAllBytes(Path.Combine(nested, "resourceNested.png"), new byte[] { 4 });

            var index = GameResourceIconIndex.Build(
                new[]
                {
                    Resource("resourceRock", "resourceRock"),
                    Resource("resourceWood", "resourceWood"),
                    Resource("resourceDirectory", "resourceDirectory"),
                    Resource("resourceNested", "resourceNested")
                },
                new[] { Root(0, "base", root) },
                CancellationToken.None);

            Assert.Equal(GameResourceIconStatus.Available, index.Resources[0].IconStatus);
            Assert.Equal(GameResourceIconStatus.Missing, index.Resources[1].IconStatus);
            Assert.Equal(GameResourceIconStatus.Missing, index.Resources[2].IconStatus);
            Assert.Equal(GameResourceIconStatus.Missing, index.Resources[3].IconStatus);
        }

        [Theory]
        [InlineData("folder/icon")]
        [InlineData("folder\\icon")]
        [InlineData("icon..backup")]
        [InlineData("C:icon")]
        [InlineData("icon\u0001")]
        public void Rejects_an_illegal_icon_leaf_name(string iconName)
        {
            using var directory = TestDirectory.Create();
            var root = directory.CreateDirectory("icons");

            var index = GameResourceIconIndex.Build(
                new[] { Resource("resource", iconName) },
                new[] { Root(0, "base", root) },
                CancellationToken.None);

            Assert.Equal(GameResourceIconStatus.Invalid, Assert.Single(index.Resources).IconStatus);
            Assert.False(index.TryGetIcon(index.Resources[0].ResourceId, out _));
        }

        [Fact]
        public void Rejects_an_icon_leaf_name_longer_than_128_characters()
        {
            using var directory = TestDirectory.Create();
            var index = GameResourceIconIndex.Build(
                new[] { Resource("resource", new string('a', 129)) },
                new[] { Root(0, "base", directory.CreateDirectory("icons")) },
                CancellationToken.None);

            Assert.Equal(GameResourceIconStatus.Invalid, Assert.Single(index.Resources).IconStatus);
        }

        [Fact]
        public void Later_explicit_root_precedence_overrides_base_regardless_of_input_order()
        {
            using var directory = TestDirectory.Create();
            var baseRoot = directory.CreateDirectory("base-icons");
            var modRoot = directory.CreateDirectory("mod-icons");
            File.WriteAllBytes(Path.Combine(baseRoot, "resourceRock.png"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(modRoot, "resourceRock.png"), new byte[] { 2 });

            var index = GameResourceIconIndex.Build(
                new[] { Resource("resourceRock", "resourceRock") },
                new[] { Root(8, "mod", modRoot), Root(0, "base", baseRoot) },
                CancellationToken.None);

            var resource = Assert.Single(index.Resources);
            Assert.True(index.TryGetIcon(resource.ResourceId, out var icon));
            Assert.Equal(Path.GetFullPath(Path.Combine(modRoot, "resourceRock.png")), icon.CanonicalPath);
        }

        [Fact]
        public void Missing_roots_are_skipped_without_exposing_the_path_in_warnings()
        {
            using var directory = TestDirectory.Create();
            var missingRoot = Path.Combine(directory.Path, "secret", "missing");

            var index = GameResourceIconIndex.Build(
                new[] { Resource("resourceRock", "resourceRock") },
                new[] { Root(0, "base", missingRoot) },
                CancellationToken.None);

            Assert.Equal(GameResourceIconStatus.Missing, Assert.Single(index.Resources).IconStatus);
            Assert.NotEmpty(index.Warnings);
            Assert.All(index.Warnings, warning => Assert.DoesNotContain(directory.Path, warning));
        }

        [Fact]
        public void Resource_ids_are_unpredictable_url_safe_and_do_not_expose_paths()
        {
            using var directory = TestDirectory.Create();
            var root = directory.CreateDirectory("icons");
            File.WriteAllBytes(Path.Combine(root, "resourceRock.png"), new byte[] { 1 });
            var resources = new[] { Resource("resourceRock", "resourceRock") };
            var roots = new[] { Root(0, "base", root) };

            var first = GameResourceIconIndex.Build(resources, roots, CancellationToken.None);
            var second = GameResourceIconIndex.Build(resources, roots, CancellationToken.None);

            var firstId = Assert.Single(first.Resources).ResourceId;
            var secondId = Assert.Single(second.Resources).ResourceId;
            Assert.NotEqual(firstId, secondId);
            Assert.Matches(new Regex("^[A-Za-z0-9_-]+$"), firstId);
            Assert.DoesNotContain("=", firstId);
            Assert.DoesNotContain(directory.Path, firstId);
            Assert.DoesNotContain(
                first.Resources.GetType().GetProperties().Select(property => property.Name),
                name => name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Duplicate_root_precedence_is_rejected_instead_of_using_enumeration_order()
        {
            using var directory = TestDirectory.Create();
            var first = directory.CreateDirectory("first");
            var second = directory.CreateDirectory("second");

            Assert.Throws<InvalidOperationException>(() => GameResourceIconIndex.Build(
                new[] { Resource("resourceRock", "resourceRock") },
                new[] { Root(0, "first", first), Root(0, "second", second) },
                CancellationToken.None));
        }

        [Fact]
        public void Reparse_icon_root_is_rejected_and_cannot_escape_to_another_directory()
        {
            if (Path.DirectorySeparatorChar != '\\') return;

            using var directory = TestDirectory.Create();
            var approved = directory.CreateDirectory("approved");
            var outside = directory.CreateDirectory("outside");
            File.WriteAllBytes(Path.Combine(outside, "resourceRock.png"), new byte[] { 9 });
            var link = Path.Combine(approved, "linked-icons");
            CreateJunction(link, outside);
            try
            {
                var index = GameResourceIconIndex.Build(
                    new[] { Resource("resourceRock", "resourceRock") },
                    new[] { Root(0, "base", link) },
                    CancellationToken.None);

                Assert.Equal(GameResourceIconStatus.Missing, Assert.Single(index.Resources).IconStatus);
                Assert.Contains("icon-root-rejected", index.Warnings);
            }
            finally
            {
                if (Directory.Exists(link)) Directory.Delete(link);
            }
        }

        private static void CreateJunction(string link, string target)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c mklink /J \"" + link + "\" \"" + target + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Assert.NotNull(process);
            Assert.True(process!.WaitForExit(5000));
            Assert.Equal(0, process.ExitCode);
        }

        private static GameResourceScalarEntry Resource(string name, string? iconName) =>
            new GameResourceScalarEntry(
                1,
                name,
                false,
                true,
                500,
                false,
                iconName,
                null,
                null,
                null);

        private static GameResourceIconRootDescriptor Root(
            int precedence,
            string source,
            string path) =>
            new GameResourceIconRootDescriptor(precedence, source, path);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "SevenDays")]

        private sealed class TestDirectory : IDisposable
        {
            private static readonly string TestRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "7dpanel-game-resource-tests"));

            private TestDirectory(string path)
            {
                Path = path;
            }

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
