using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class GameResourceUseCaseTests
    {
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Query_accepts_only_the_approved_language_and_bounded_normalized_values()
        {
            var minimum = new GameResourceQuery(" x ", null, false, "zh-CN", 1, 1);
            var maximum = new GameResourceQuery(
                new string('x', 100),
                GameResourceKind.Block,
                true,
                "en",
                100_000,
                100);

            Assert.Equal("x", minimum.Search);
            Assert.Equal("zh-CN", minimum.Language);
            Assert.Equal(100, maximum.Search!.Length);
            Assert.Equal(100_000, maximum.Page);
            Assert.Equal(100, maximum.PageSize);
            Assert.Throws<ArgumentException>(() =>
                new GameResourceQuery(" ", null, false, "en", 1, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameResourceQuery(new string('x', 101), null, false, "en", 1, 50));
            Assert.Throws<ArgumentException>(() =>
                new GameResourceQuery(null, null, false, "EN", 1, 50));
            Assert.Throws<ArgumentException>(() =>
                new GameResourceQuery(null, null, false, "schinese", 1, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameResourceQuery(null, null, false, "en", 0, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameResourceQuery(null, null, false, "en", 100_001, 50));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameResourceQuery(null, null, false, "en", 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameResourceQuery(null, null, false, "en", 1, 101));
        }

        [Fact]
        public void Selected_language_is_projected_without_filling_missing_localization()
        {
            var resource = Entry(
                "iron",
                1,
                "resourceIron",
                localizedNameZhCn: "铁锭",
                localizedNameEn: "Iron Ingot");
            var useCase = CreateUseCase(Available(resource));

            var chinese = useCase.Execute(
                new GameResourceQuery(null, null, false, "zh-CN", 1, 50),
                GameResourceAccess.Standard);
            var english = useCase.Execute(
                new GameResourceQuery(null, null, false, "en", 1, 50),
                GameResourceAccess.Standard);
            var missing = CreateUseCase(Available(Entry(
                "clay",
                2,
                "resourceClay",
                localizedNameZhCn: null,
                localizedNameEn: "Clay"))).Execute(
                    new GameResourceQuery(null, null, false, "zh-CN", 1, 50),
                    GameResourceAccess.Standard);

            Assert.Equal("铁锭", Assert.Single(chinese.Items).LocalizedName);
            Assert.Equal("Iron Ingot", Assert.Single(english.Items).LocalizedName);
            Assert.Null(Assert.Single(missing.Items).LocalizedName);
        }

        [Fact]
        public void Search_uses_internal_name_and_only_the_selected_localization_ordinal_ignore_case()
        {
            var useCase = CreateUseCase(Available(Entry(
                "iron",
                1,
                "resourceIron",
                localizedNameZhCn: "铁锭",
                localizedNameEn: "Iron Ingot")));

            var englishLocalization = useCase.Execute(
                new GameResourceQuery("  INGOT  ", null, false, "en", 1, 50),
                GameResourceAccess.Standard);
            var chineseDoesNotSearchEnglish = useCase.Execute(
                new GameResourceQuery("ingot", null, false, "zh-CN", 1, 50),
                GameResourceAccess.Standard);
            var internalName = useCase.Execute(
                new GameResourceQuery("RESOURCEIRON", null, false, "zh-CN", 1, 50),
                GameResourceAccess.Standard);

            Assert.Single(englishLocalization.Items);
            Assert.Empty(chineseDoesNotSearchEnglish.Items);
            Assert.Single(internalName.Items);
        }

        [Fact]
        public void Kind_and_visibility_filters_run_before_results_are_returned()
        {
            var unknownVisibility = Entry(
                "unknown-visibility",
                4,
                "unknownVisibility",
                visibility: (GameResourceVisibility)999);
            var useCase = CreateUseCase(Available(
                Entry("public-item", 1, "publicItem"),
                Entry("public-block", 2, "publicBlock", kind: GameResourceKind.Block),
                Entry("hidden-item", 3, "hiddenItem", visibility: GameResourceVisibility.Hidden),
                unknownVisibility));

            var standardBlocks = useCase.Execute(
                new GameResourceQuery(null, GameResourceKind.Block, false, "en", 1, 50),
                GameResourceAccess.Standard);
            var ownerPublic = useCase.Execute(
                new GameResourceQuery(null, null, false, "en", 1, 50),
                GameResourceAccess.Owner);
            var ownerAll = useCase.Execute(
                new GameResourceQuery(null, null, true, "en", 1, 50),
                GameResourceAccess.Owner);

            Assert.Equal(GameResourceVisibility.Hidden, unknownVisibility.Visibility);
            Assert.Equal("public-block", Assert.Single(standardBlocks.Items).ResourceId);
            Assert.Equal(2, ownerPublic.Total);
            Assert.Equal(4, ownerAll.Total);
        }

        [Fact]
        public void Standard_access_cannot_request_hidden_resources()
        {
            var catalog = new StubCatalog(Available(Entry("public", 1, "public")));
            var useCase = new QueryGameResourcesUseCase(catalog);

            var exception = Assert.Throws<GameResourceHiddenForbiddenException>(() =>
                useCase.Execute(
                    new GameResourceQuery(null, null, true, "en", 1, 50),
                    GameResourceAccess.Standard));

            Assert.Equal(
                "Including hidden game resources requires owner access.",
                exception.Message);
            Assert.Equal(0, catalog.ReadCount);
        }

        [Fact]
        public void Results_are_stably_sorted_then_paged_with_the_real_total()
        {
            var useCase = CreateUseCase(Available(
                Entry("zulu", 20, "Zulu", localizedNameEn: "Alpha"),
                Entry("same-two", 2, "same", localizedNameEn: "beta"),
                Entry("same-one", 1, "SAME", localizedNameEn: "Beta"),
                Entry("gamma", 4, "Gamma", localizedNameEn: null)));

            var secondPage = useCase.Execute(
                new GameResourceQuery(null, null, false, "en", 2, 2),
                GameResourceAccess.Standard);
            var beyondLastPage = useCase.Execute(
                new GameResourceQuery(null, null, false, "en", 100_000, 100),
                GameResourceAccess.Standard);

            Assert.Equal(4, secondPage.Total);
            Assert.Equal(2, secondPage.Page);
            Assert.Equal(2, secondPage.PageSize);
            Assert.Equal(new[] { "same-two", "gamma" },
                new[] { secondPage.Items[0].ResourceId, secondPage.Items[1].ResourceId });
            Assert.Equal(4, beyondLastPage.Total);
            Assert.Empty(beyondLastPage.Items);
        }

        [Theory]
        [InlineData(GameResourceCatalogReadStatus.Building)]
        [InlineData(GameResourceCatalogReadStatus.Unavailable)]
        public void Non_available_catalog_states_remain_typed(
            GameResourceCatalogReadStatus status)
        {
            var read = status == GameResourceCatalogReadStatus.Building
                ? GameResourceCatalogReadResult.Building()
                : GameResourceCatalogReadResult.Unavailable();

            var result = new QueryGameResourcesUseCase(new StubCatalog(read)).Execute(
                new GameResourceQuery(null, null, false, "en", 3, 25),
                GameResourceAccess.Standard);

            Assert.Equal(status, result.Status);
            Assert.Null(result.CatalogVersion);
            Assert.Null(result.GameVersion);
            Assert.Null(result.ObservedAtUtc);
            Assert.Empty(result.Items);
            Assert.Empty(result.Warnings);
            Assert.Equal(0, result.Total);
            Assert.Equal(3, result.Page);
            Assert.Equal(25, result.PageSize);
        }

        [Fact]
        public async Task Icon_lookup_hides_hidden_and_unknown_identifiers_from_standard_access()
        {
            var catalog = new StubCatalog(
                Available(
                    Entry("public", 1, "public"),
                    Entry("hidden", 2, "hidden", visibility: GameResourceVisibility.Hidden)),
                GameResourceIconReadResult.Available(new byte[] { 1 }, "\"etag\""));
            var useCase = new GetGameResourceIconUseCase(catalog);

            var hidden = await useCase.ExecuteAsync(
                "hidden",
                GameResourceAccess.Standard,
                TestContext.Current.CancellationToken);
            var missing = await useCase.ExecuteAsync(
                "does-not-exist",
                GameResourceAccess.Standard,
                TestContext.Current.CancellationToken);

            Assert.Equal(GameResourceIconReadStatus.Missing, hidden.Status);
            Assert.Equal(GameResourceIconReadStatus.Missing, missing.Status);
            Assert.Equal(0, catalog.IconReadCount);
        }

        [Fact]
        public async Task Icon_lookup_passes_the_same_snapshot_version_for_an_allowed_resource()
        {
            var expected = GameResourceIconReadResult.Available(
                new byte[] { 1, 2, 3 },
                "\"etag-1\"");
            var catalog = new StubCatalog(Available(Entry("public", 1, "public")), expected);

            var result = await new GetGameResourceIconUseCase(catalog).ExecuteAsync(
                "public",
                GameResourceAccess.Standard,
                TestContext.Current.CancellationToken);

            Assert.Same(expected, result);
            Assert.Equal(1, catalog.IconReadCount);
            Assert.Equal("catalog-7", catalog.LastCatalogVersion);
            Assert.Equal("public", catalog.LastResourceId);
        }

        [Fact]
        public async Task Icon_lookup_returns_missing_without_port_access_for_a_known_bad_icon()
        {
            var catalog = new StubCatalog(Available(Entry(
                "invalid-icon",
                1,
                "invalidIcon",
                iconStatus: GameResourceIconStatus.Invalid)));

            var result = await new GetGameResourceIconUseCase(catalog).ExecuteAsync(
                "invalid-icon",
                GameResourceAccess.Owner,
                TestContext.Current.CancellationToken);

            Assert.Equal(GameResourceIconReadStatus.Missing, result.Status);
            Assert.Equal(0, catalog.IconReadCount);
        }

        [Theory]
        [InlineData(GameResourceCatalogReadStatus.Building)]
        [InlineData(GameResourceCatalogReadStatus.Unavailable)]
        public async Task Icon_lookup_maps_non_available_catalog_states_to_typed_unavailable(
            GameResourceCatalogReadStatus status)
        {
            var read = status == GameResourceCatalogReadStatus.Building
                ? GameResourceCatalogReadResult.Building()
                : GameResourceCatalogReadResult.Unavailable();
            var catalog = new StubCatalog(read);

            var result = await new GetGameResourceIconUseCase(catalog).ExecuteAsync(
                "public",
                GameResourceAccess.Standard,
                TestContext.Current.CancellationToken);

            Assert.Equal(GameResourceIconReadStatus.Unavailable, result.Status);
            Assert.Equal(0, catalog.IconReadCount);
        }

        [Fact]
        public void Snapshots_and_results_defensively_copy_and_read_only_wrap_collections()
        {
            var original = Entry("original", 1, "original");
            var resources = new[] { original };
            var warnings = new[] { "missing-localization-en" };
            var snapshot = new GameResourceCatalogSnapshot(
                "catalog-7",
                "3.0.1-b4",
                ObservedAtUtc,
                resources,
                warnings);
            resources[0] = Entry("replacement", 2, "replacement");
            warnings[0] = "replacement-warning";

            var result = CreateUseCase(GameResourceCatalogReadResult.Available(snapshot)).Execute(
                new GameResourceQuery(null, null, false, "en", 1, 50),
                GameResourceAccess.Standard);

            Assert.Same(original, Assert.Single(snapshot.Resources));
            Assert.Equal("missing-localization-en", Assert.Single(snapshot.Warnings));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GameResourceCatalogEntry>)snapshot.Resources).Add(original));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)snapshot.Warnings).Add("another-warning"));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GameResourceQueryItem>)result.Items).Add(result.Items[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<string>)result.Warnings).Add("another-warning"));
        }

        [Fact]
        public void Resource_and_icon_payload_contracts_enforce_normalized_scalar_values()
        {
            var resource = Entry(
                "iron",
                1,
                "resourceIron",
                localizedNameZhCn: " ",
                localizedNameEn: "Iron",
                iconTintHex: "12ABEF");
            var bytes = new byte[] { 1, 2, 3 };
            var icon = GameResourceIconReadResult.Available(bytes, "\"etag-1\"");
            bytes[0] = 9;
            var exposed = icon.Content!;
            exposed[0] = 8;

            Assert.Null(resource.LocalizedNameZhCn);
            Assert.Equal("12ABEF", resource.IconTintHex);
            Assert.Equal("image/png", icon.ContentType);
            Assert.Equal(1, icon.Content![0]);
            Assert.Throws<ArgumentException>(() => Entry(
                "lowercase-tint", 2, "lowercaseTint", iconTintHex: "12abef"));
            Assert.Throws<ArgumentException>(() => Entry(
                "short-tint", 3, "shortTint", iconTintHex: "ABCDE"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Entry(
                "bad-stack", 4, "badStack", maxStack: 0));
        }

        private static QueryGameResourcesUseCase CreateUseCase(
            GameResourceCatalogReadResult read) =>
            new QueryGameResourcesUseCase(new StubCatalog(read));

        private static GameResourceCatalogReadResult Available(
            params GameResourceCatalogEntry[] resources) =>
            GameResourceCatalogReadResult.Available(new GameResourceCatalogSnapshot(
                "catalog-7",
                "3.0.1-b4",
                ObservedAtUtc,
                resources,
                new[] { "missing-localization-en" }));

        private static GameResourceCatalogEntry Entry(
            string resourceId,
            int numericId,
            string internalName,
            string? localizedNameZhCn = "中文名",
            string? localizedNameEn = "English name",
            GameResourceKind kind = GameResourceKind.Item,
            GameResourceVisibility visibility = GameResourceVisibility.Public,
            int? maxStack = 100,
            bool? hasQuality = false,
            GameResourceIconStatus iconStatus = GameResourceIconStatus.Available,
            string? iconTintHex = null) =>
            new GameResourceCatalogEntry(
                resourceId,
                numericId,
                internalName,
                localizedNameZhCn,
                localizedNameEn,
                kind,
                visibility,
                maxStack,
                hasQuality,
                iconStatus,
                iconTintHex);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "SevenDays")]

        private sealed class StubCatalog : IGameResourceCatalog
        {
            private readonly GameResourceCatalogReadResult read;
            private readonly GameResourceIconReadResult iconRead;

            public StubCatalog(
                GameResourceCatalogReadResult read,
                GameResourceIconReadResult? iconRead = null)
            {
                this.read = read;
                this.iconRead = iconRead ?? GameResourceIconReadResult.Missing();
            }

            public int ReadCount { get; private set; }

            public int IconReadCount { get; private set; }

            public string? LastCatalogVersion { get; private set; }

            public string? LastResourceId { get; private set; }

            public GameResourceCatalogReadResult Read()
            {
                ReadCount++;
                return read;
            }

            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken)
            {
                IconReadCount++;
                LastCatalogVersion = catalogVersion;
                LastResourceId = resourceId;
                return Task.FromResult(iconRead);
            }
        }
    }
}
