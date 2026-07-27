using System;
using System.Linq;
using System.Reflection;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class WorldOperationContractTests
    {
        [Fact]
        public void World_operation_kinds_are_the_approved_closed_catalog()
        {
            Assert.Equal(
                new[]
                {
                    "DeleteLandClaim", "MoveOnlinePlayer", "MoveEntity",
                    "RefreshMapResources", "RenderExploredMap", "RenderFullMap",
                    "CopyRegion", "FillRegion", "ClearRegion", "PasteRegion",
                    "SetBlock", "PlacePrefab", "RemovePrefab",
                    "SpawnEntity", "DeleteEntity", "CleanupEntities",
                    "ReloadBlocks", "ReloadItems", "ReloadEntityClasses", "ReloadPrefabs",
                    "CollectGarbage", "UndoChangeSet"
                },
                Enum.GetNames(typeof(WorldOperationKind)));
        }

        [Fact]
        public void World_operation_statuses_are_the_approved_eight_states()
        {
            Assert.Equal(
                new[]
                {
                    "Queued", "Running", "Succeeded", "Failed", "Cancelled",
                    "Interrupted", "ResultUnknown", "RollbackFailed"
                },
                Enum.GetNames(typeof(WorldOperationStatus)));
        }

        [Fact]
        public void Job_bridge_has_only_the_fixed_typed_surface()
        {
            var methods = typeof(IWorldOperationJobBridge)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .Select(method =>
                    method.ReturnType.Name + " " + method.Name + "(" +
                    string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name)) + ")")
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "WorldOperationReceipt Enqueue(WorldOperationIntent)",
                    "WorldOperationRecord Get(String)",
                    "WorldOperationPage Query(WorldOperationQuery)",
                    "Boolean RequestCancellation(String,String)"
                },
                methods);
        }

        [Fact]
        public void Public_world_operation_contracts_expose_no_payload_path_or_arbitrary_type_name()
        {
            var unsafeTerms = new[] { "payload", "path", "filename", "typename" };
            var publicSurface = typeof(WorldOperationKind).Assembly.GetTypes()
                .Where(type => type.IsPublic &&
                    string.Equals(
                        type.Namespace,
                        "LSTY.SevenDPanel.Application.WorldOperations",
                        StringComparison.Ordinal))
                .SelectMany(type =>
                    type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Select(property => type.Name + "." + property.Name)
                        .Concat(type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                            .SelectMany(method => method.GetParameters())
                            .Select(parameter => type.Name + "." + parameter.Name)))
                .ToArray();

            Assert.DoesNotContain(
                publicSurface,
                name => unsafeTerms.Any(term =>
                    name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Fact]
        public void Intent_requires_canonical_utc_and_a_safe_confirmation_summary()
        {
            var target = new WorldMaintenanceOperationTarget(null);

            Assert.Throws<ArgumentException>(() => new WorldOperationIntent(
                "owner", WorldOperationKind.CollectGarbage, "world", "v1", null,
                "corr-offset", "Collect managed garbage", false, target,
                new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.FromHours(8))));
            Assert.Throws<ArgumentException>(() => new WorldOperationIntent(
                "owner", WorldOperationKind.CollectGarbage, "world", "v1", null,
                "corr-path", "Collect /server/world/save", false, target, Utc(0)));
            Assert.Throws<ArgumentException>(() => new WorldOperationIntent(
                "owner", WorldOperationKind.CollectGarbage, "world", "v1", null,
                "corr-payload", "payload_json contains details", false, target, Utc(0)));
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);
    }
}
