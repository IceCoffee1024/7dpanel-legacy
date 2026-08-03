using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysGameResourceDraftReaderTests
    {
        [Fact]
        public async Task Read_dispatches_the_only_capture_action_with_the_bounded_game_thread_contract()
        {
            var expected = EmptyDraft();
            string? operation = null;
            TimeSpan timeout = default;
            CancellationToken observedToken = default;
            Func<GameResourceScalarDraft>? observedCapture = null;
            using var cancellation = new CancellationTokenSource();
            var reader = new SevenDaysGameResourceDraftReader(
                (name, capture, startTimeout, token) =>
                {
                    operation = name;
                    observedCapture = capture;
                    timeout = startTimeout;
                    observedToken = token;
                    return Task.FromResult(capture());
                },
                () => expected);

            var actual = await reader.ReadAsync(cancellation.Token);

            Assert.Same(expected, actual);
            Assert.Equal("7dpanel.game-resources.capture", operation);
            Assert.Equal(TimeSpan.FromSeconds(5), timeout);
            Assert.Equal(cancellation.Token, observedToken);
            Assert.NotNull(observedCapture);
        }

        [Fact]
        public void Normalize_skips_empty_slots_and_entries_without_an_internal_name()
        {
            var resources = new GameResourceCapturedEntry?[]
            {
                null,
                Entry(1, "  "),
                Entry(2, "resourceRock")
            };

            var draft = SevenDaysGameResourceDraftReader.Normalize(
                "V3.0",
                new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
                resources,
                null,
                Array.Empty<GameResourceIconRootDescriptor>());

            var resource = Assert.Single(draft.Resources);
            Assert.Equal(2, resource.NumericId);
            Assert.Equal("resourceRock", resource.InternalName);
            Assert.Contains(draft.Warnings, warning => warning == "resource-name-invalid");
        }

        [Fact]
        public void Normalize_reads_localization_by_metadata_column_name_and_allows_one_column_to_be_missing()
        {
            var localization = new GameResourceLocalizationCapture(
                new[] { "german", "english", "schinese" },
                new Dictionary<string, string[]>
                {
                    ["resourceRock"] = new[] { "Stein", "Rock", "石头" },
                    ["resourceClay"] = new[] { "Lehm", "Clay" }
                });

            var draft = SevenDaysGameResourceDraftReader.Normalize(
                null,
                DateTimeOffset.UtcNow,
                new[] { Entry(1, "resourceRock"), Entry(2, "resourceClay") },
                localization,
                Array.Empty<GameResourceIconRootDescriptor>());

            Assert.Equal("Rock", draft.Resources[0].EnglishName);
            Assert.Equal("石头", draft.Resources[0].SimplifiedChineseName);
            Assert.Equal("Clay", draft.Resources[1].EnglishName);
            Assert.Null(draft.Resources[1].SimplifiedChineseName);

            var englishOnly = SevenDaysGameResourceDraftReader.Normalize(
                null,
                DateTimeOffset.UtcNow,
                new[] { Entry(3, "resourceWood") },
                new GameResourceLocalizationCapture(
                    new[] { "english" },
                    new Dictionary<string, string[]> { ["resourceWood"] = new[] { "Wood" } }),
                Array.Empty<GameResourceIconRootDescriptor>());

            Assert.Equal("Wood", englishOnly.Resources[0].EnglishName);
            Assert.Null(englishOnly.Resources[0].SimplifiedChineseName);
        }

        [Fact]
        public void Normalize_keeps_the_runtime_proven_final_definition_for_a_duplicate_name()
        {
            var first = Entry(1, "resourceRock", isFinalDefinition: false);
            var final = Entry(9, "resourceRock", isFinalDefinition: true);

            var draft = SevenDaysGameResourceDraftReader.Normalize(
                null,
                DateTimeOffset.UtcNow,
                new[] { first, final },
                null,
                Array.Empty<GameResourceIconRootDescriptor>());

            Assert.Equal(9, Assert.Single(draft.Resources).NumericId);
            Assert.Contains("resource-duplicate-resolved", draft.Warnings);
        }

        [Fact]
        public void Normalize_rejects_a_duplicate_name_without_one_proven_final_definition()
        {
            var entries = new[]
            {
                Entry(1, "resourceRock", isFinalDefinition: false),
                Entry(9, "resourceRock", isFinalDefinition: false)
            };

            Assert.Throws<GameResourceCatalogAmbiguousException>(() =>
                SevenDaysGameResourceDraftReader.Normalize(
                    null,
                    DateTimeOffset.UtcNow,
                    entries,
                    null,
                    Array.Empty<GameResourceIconRootDescriptor>()));
        }

        [Fact]
        public void Normalize_hides_unknown_creative_modes_and_nulls_invalid_optional_values()
        {
            var captured = new GameResourceCapturedEntry(
                7,
                "resourceBad",
                false,
                999,
                0,
                null,
                "resourceBad",
                new GameResourceCapturedTint(float.NaN, 0f, 0f, 1f),
                true);

            var draft = SevenDaysGameResourceDraftReader.Normalize(
                null,
                DateTimeOffset.UtcNow,
                new[] { captured },
                null,
                Array.Empty<GameResourceIconRootDescriptor>());

            var resource = Assert.Single(draft.Resources);
            Assert.False(resource.IsPublic);
            Assert.Null(resource.MaxStack);
            Assert.Null(resource.HasQuality);
            Assert.Null(resource.IconTintHex);
            Assert.Contains("resource-creative-mode-unknown", draft.Warnings);
            Assert.Contains("resource-max-stack-invalid", draft.Warnings);
            Assert.Contains("resource-quality-unavailable", draft.Warnings);
            Assert.Contains("resource-icon-tint-invalid", draft.Warnings);
        }

        [Fact]
        public void Normalize_omits_white_tint_and_copies_all_input_collections()
        {
            var entries = new[]
            {
                new GameResourceCapturedEntry(
                    1,
                    "resourceRock",
                    false,
                    GameResourceCreativeMode.All,
                    500,
                    true,
                    "resourceRock",
                    new GameResourceCapturedTint(1f, 1f, 1f, 1f),
                    true)
            };
            var roots = new[] { new GameResourceIconRootDescriptor(0, "base", "C:\\game\\Data\\ItemIcons") };

            var draft = SevenDaysGameResourceDraftReader.Normalize(
                "V3.0",
                DateTimeOffset.UtcNow,
                entries,
                null,
                roots);
            entries[0] = Entry(2, "changed");
            roots[0] = new GameResourceIconRootDescriptor(1, "changed", "C:\\outside");

            Assert.Equal("resourceRock", draft.Resources[0].InternalName);
            Assert.Null(draft.Resources[0].IconTintHex);
            Assert.Equal("base", draft.IconRoots[0].SourceName);
            Assert.IsAssignableFrom<IReadOnlyList<GameResourceScalarEntry>>(draft.Resources);
            Assert.False(draft.Resources is GameResourceScalarEntry[]);
        }

        private static GameResourceCapturedEntry Entry(
            int id,
            string? name,
            bool isFinalDefinition = true) =>
            new GameResourceCapturedEntry(
                id,
                name,
                false,
                GameResourceCreativeMode.Player,
                500,
                false,
                name,
                null,
                isFinalDefinition);

        private static GameResourceScalarDraft EmptyDraft() =>
            new GameResourceScalarDraft(
                null,
                DateTimeOffset.UtcNow,
                Array.Empty<GameResourceScalarEntry>(),
                Array.Empty<GameResourceIconRootDescriptor>(),
                Array.Empty<string>());
    }
}
