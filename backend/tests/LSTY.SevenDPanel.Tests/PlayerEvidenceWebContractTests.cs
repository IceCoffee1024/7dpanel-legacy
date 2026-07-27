using System;
using System.Linq;
using System.Reflection;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerEvidenceWebContractTests
    {
        [Fact]
        public void Evidence_controller_exposes_only_the_fixed_owner_routes()
        {
            Assert.Equal("api/v1/players", typeof(PlayerEvidenceController)
                .GetCustomAttributes(typeof(RoutePrefixAttribute), true)
                .Cast<RoutePrefixAttribute>()
                .Single().Prefix);
            Assert.Contains(
                typeof(PlayerEvidenceController).GetCustomAttributes(true),
                attribute => attribute.GetType().Name == "OwnerAuthorizeAttribute");
            AssertRoute(nameof(PlayerEvidenceController.GetProfile), "{crossplatformId}/profile");
            AssertRoute(
                nameof(PlayerEvidenceController.GetInventorySnapshots),
                "{crossplatformId}/inventory-snapshots");
            AssertRoute(
                nameof(PlayerEvidenceController.GetInventoryDiffs),
                "{crossplatformId}/inventory-diffs");
            AssertRoute(nameof(PlayerEvidenceController.GetSkills), "{crossplatformId}/skills");
        }

        [Fact]
        public void Evidence_cursor_is_url_safe_round_trips_ties_and_is_bound_to_the_player()
        {
            var cursor = new PlayerEvidenceCursor(
                new DateTimeOffset(2026, 7, 26, 8, 30, 0, TimeSpan.Zero),
                41);

            var encoded = PlayerEvidenceCursorCodec.Encode("EOS_player/one", cursor);

            Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
            Assert.DoesNotContain("EOS_player", encoded, StringComparison.Ordinal);
            Assert.True(PlayerEvidenceCursorCodec.TryDecode(
                encoded,
                "EOS_player/one",
                out var decoded));
            Assert.Equal(cursor.ObservedAtUtc, decoded!.ObservedAtUtc);
            Assert.Equal(cursor.Id, decoded.Id);
            Assert.False(PlayerEvidenceCursorCodec.TryDecode(
                encoded,
                "EOS_player/two",
                out _));
            Assert.False(PlayerEvidenceCursorCodec.TryDecode("not+a+cursor", "EOS_player/one", out _));
        }

        [Fact]
        public void Paged_evidence_responses_have_an_opaque_cursor_and_gap_metadata()
        {
            AssertResponseShape<PlayerInventorySnapshotsPageHttpResponse>("Snapshots");
            AssertResponseShape<PlayerInventoryDiffsPageHttpResponse>("Diffs");
            AssertResponseShape<PlayerSkillsPageHttpResponse>("Snapshots");
        }

        private static void AssertResponseShape<T>(string itemProperty)
        {
            var names = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();
            Assert.Contains(itemProperty, names);
            Assert.Contains("NextCursor", names);
            Assert.Contains("GapMetadata", names);
            Assert.DoesNotContain("Object", names);
            Assert.DoesNotContain("Path", names);
            Assert.DoesNotContain("Token", names);
            Assert.DoesNotContain("Command", names);
        }

        private static void AssertRoute(string methodName, string expected)
        {
            var method = typeof(PlayerEvidenceController).GetMethod(methodName);
            Assert.NotNull(method);
            var route = method!.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .Single();
            Assert.Equal(expected, route.Template);
        }
    }
}
