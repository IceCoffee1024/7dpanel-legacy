using System;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ConsoleCommandCatalogTests
    {
        [Fact]
        public void Entry_prefers_registered_primary_and_normalizes_aliases()
        {
            var entry = SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                () => new[] { " version ", "VER", "Version", "" },
                () => "VERSION",
                () => "Show version",
                () => "version help",
                commands => 7);

            Assert.NotNull(entry);
            Assert.Equal("version", entry!.Name);
            Assert.Equal(new[] { "VER" }, entry.Aliases);
            Assert.Equal("Show version", entry.Description);
            Assert.Equal("version help", entry.Help);
            Assert.Equal(7, entry.PermissionLevel);
        }

        [Fact]
        public void Optional_metadata_failures_are_isolated()
        {
            var entry = SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                () => new[] { "help", "h" },
                () => throw new InvalidOperationException("primary"),
                () => throw new InvalidOperationException("description"),
                () => throw new InvalidOperationException("help"),
                commands => throw new InvalidOperationException("permission"));

            Assert.NotNull(entry);
            Assert.Equal("help", entry!.Name);
            Assert.Equal(new[] { "h" }, entry.Aliases);
            Assert.Null(entry.Description);
            Assert.Null(entry.Help);
            Assert.Null(entry.PermissionLevel);
        }

        [Fact]
        public void Entry_without_any_valid_name_is_skipped()
        {
            var entry = SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                () => new[] { "", "   " },
                () => "missing",
                () => "description",
                () => "help",
                commands => 0);

            Assert.Null(entry);
        }

        [Fact]
        public void Catalog_sort_is_case_insensitive_with_ordinal_tie_breaker()
        {
            var entries = new[]
            {
                SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                    () => new[] { "zebra" }, () => "zebra", () => null, () => null, _ => 0)!,
                SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                    () => new[] { "Alpha" }, () => "Alpha", () => null, () => null, _ => 0)!,
                SevenDaysConsoleCommandCatalogQuery.TryReadEntry(
                    () => new[] { "alpha" }, () => "alpha", () => null, () => null, _ => 0)!
            };

            Assert.Equal(
                new[] { "Alpha", "alpha", "zebra" },
                SevenDaysConsoleCommandCatalogQuery.Sort(entries).Select(entry => entry.Name));
        }
    }
}
