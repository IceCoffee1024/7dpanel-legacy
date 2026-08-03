using System;
using System.IO;
using LSTY.SevenDPanel.Application.Mods;
using LSTY.SevenDPanel.Mods;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class LocalModCatalogTests
    {
        [Theory]
        [InlineData("../Other")]
        [InlineData("C:\\Mods\\Other")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("")]
        public void Rejects_non_child_directory_ids(string directoryId)
        {
            using var fixture = new Fixture();

            Assert.Equal(ModStateChangeStatus.InvalidDirectory,
                fixture.Catalog.SetEnabled(directoryId, false).Status);
        }

        [Fact]
        public void Lists_metadata_and_keeps_disabled_marker_separate_from_runtime_state()
        {
            using var fixture = new Fixture();
            fixture.Add("Enabled", "ModInfo.xml", "Enabled.Internal", "Enabled display");
            fixture.Add("Disabled", "_ModInfo.xml", "Disabled.Internal", "Disabled display");

            var entries = fixture.Catalog.List();

            Assert.Collection(entries,
                disabled =>
                {
                    Assert.Equal("Disabled", disabled.DirectoryId);
                    Assert.Equal("Disabled.Internal", disabled.Name);
                    Assert.False(disabled.IsEnabledNextStart);
                },
                enabled =>
                {
                    Assert.Equal("Enabled", enabled.DirectoryId);
                    Assert.Equal("Enabled display", enabled.DisplayName);
                    Assert.True(enabled.IsEnabledNextStart);
                });
        }

        [Fact]
        public void Skips_malformed_xml_and_entries_without_name()
        {
            using var fixture = new Fixture();
            fixture.AddRaw("Broken", "ModInfo.xml", "<xml");
            fixture.AddRaw("MissingName", "ModInfo.xml", "<xml><DisplayName value=\"No name\" /></xml>");

            Assert.Empty(fixture.Catalog.List());
        }

        [Fact]
        public void Moves_marker_without_overwriting_and_is_idempotent()
        {
            using var fixture = new Fixture();
            fixture.Add("Example", "ModInfo.xml", "Example", "Example");

            Assert.Equal(ModStateChangeStatus.Changed,
                fixture.Catalog.SetEnabled("Example", false).Status);
            Assert.True(File.Exists(Path.Combine(fixture.Root, "Example", "_ModInfo.xml")));
            Assert.Equal(ModStateChangeStatus.Unchanged,
                fixture.Catalog.SetEnabled("Example", false).Status);
        }

        [Fact]
        public void Protects_configured_mod_and_rejects_ambiguous_markers()
        {
            using var fixture = new Fixture(new[] { "Panel" });
            fixture.Add("Panel", "ModInfo.xml", "Panel", "Panel");
            fixture.Add("Conflict", "ModInfo.xml", "Conflict", "Conflict");
            fixture.Add("Conflict", "_ModInfo.xml", "Conflict", "Conflict");

            Assert.Equal(ModStateChangeStatus.Protected,
                fixture.Catalog.SetEnabled("Panel", false).Status);
            Assert.Equal(ModStateChangeStatus.Conflict,
                fixture.Catalog.SetEnabled("Conflict", false).Status);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class Fixture : IDisposable
        {
            public Fixture(string[]? protectedDirectories = null)
            {
                Root = Path.Combine(Path.GetTempPath(), "7dpanel-mods-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Root);
                Catalog = new LocalModCatalog(Root, protectedDirectories ?? Array.Empty<string>());
            }

            public string Root { get; }
            public LocalModCatalog Catalog { get; }

            public void Add(string directory, string fileName, string name, string displayName)
            {
                AddRaw(directory, fileName,
                    $"<xml><Name value=\"{name}\" /><DisplayName value=\"{displayName}\" />" +
                    "<Author value=\"Author\" /><Version value=\"1.2\" />" +
                    "<Website value=\"https://example.test\" /><Description value=\"Description\" /></xml>");
            }

            public void AddRaw(string directory, string fileName, string contents)
            {
                var path = Path.Combine(Root, directory);
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, fileName), contents);
            }

            public void Dispose()
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, true);
            }
        }
    }
}
