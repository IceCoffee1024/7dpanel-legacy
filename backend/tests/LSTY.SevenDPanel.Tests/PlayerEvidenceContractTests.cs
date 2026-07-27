using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerEvidenceContractTests
    {
        [Fact]
        public void Public_enumeration_strings_are_fixed()
        {
            AssertEnum<PlayerProfileSectionState>("Available", "Partial", "Unavailable", "Forbidden");
            AssertEnum<CatalogResolutionState>("Resolved", "Unavailable");
            AssertEnum<SkillValueState>("Known", "UnsupportedByVersion", "NotLoaded", "Unknown");
            AssertEnum<InventoryDiffKind>("Added", "Removed", "QuantityChanged", "Moved", "AttributesChanged", "Uncomparable");
            AssertEnum<EvidenceLevel>("Confirmed", "ObservedChange");
            AssertEnum<PlayerActionStatus>("Pending", "Succeeded", "Rejected", "Failed", "Cancelled", "ResultUnknown");
            AssertEnum<PlayerItemRemovalMode>("Exact", "UpToAvailable");
            AssertEnum<PlayerItemRemovalScope>("BagOnly");
        }

        [Fact]
        public void Player_target_stamp_has_only_the_fixed_validated_fields()
        {
            Assert.Equal(
                new[] { "CrossplatformId", "EntityId", "OnlineObservedAtUtc", "WorldId" },
                PublicProperties<PlayerTargetStamp>());

            var stamp = new PlayerTargetStamp("EOS_1", 17, Utc(1), "world-1");

            Assert.Equal("EOS_1", stamp.CrossplatformId);
            Assert.Throws<ArgumentException>(() => new PlayerTargetStamp(" ", 17, Utc(1), "world-1"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerTargetStamp("EOS_1", -1, Utc(1), "world-1"));
            Assert.Throws<ArgumentException>(() => new PlayerTargetStamp("EOS_1", 17, Local(1), "world-1"));
            Assert.Throws<ArgumentException>(() => new PlayerTargetStamp("EOS_1", 17, Utc(1), " "));
        }

        [Fact]
        public void Inventory_item_scalar_has_only_the_fixed_fields_and_copies_mods()
        {
            Assert.Equal(
                new[] { "Container", "Slot", "InternalName", "Count", "Quality", "UseAmount", "ModInternalNames" },
                PublicProperties<InventoryItemScalar>());
            var mods = new List<string> { "modA" };

            var item = new InventoryItemScalar("Bag", 3, "resourceWood", 2, 4, 0.25m, mods);
            mods[0] = "changed";

            Assert.Equal("modA", Assert.Single(item.ModInternalNames));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)item.ModInternalNames).Add("modB"));
            Assert.Throws<ArgumentException>(() => new InventoryItemScalar(" ", 0, "resourceWood", 1, null, null, Array.Empty<string>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItemScalar("Bag", -1, "resourceWood", 1, null, null, Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => new InventoryItemScalar("Bag", 0, " ", 1, null, null, Array.Empty<string>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryItemScalar("Bag", 0, "resourceWood", 0, null, null, Array.Empty<string>()));
        }

        [Fact]
        public void Inventory_snapshot_requires_unique_locations_and_copies_items()
        {
            var items = new List<InventoryItemScalar>
            {
                Item("Bag", 0, "resourceWood", 1)
            };
            var snapshot = InventorySnapshot(1, Utc(1), items);
            items.Clear();

            Assert.Single(snapshot.Items);
            Assert.Throws<NotSupportedException>(() => ((IList<InventoryItemScalar>)snapshot.Items).Clear());
            Assert.Throws<ArgumentException>(() => InventorySnapshot(
                2,
                Utc(2),
                new[]
                {
                    Item("Bag", 0, "resourceWood", 1),
                    Item("Bag", 0, "ammo9mmBulletBall", 1)
                }));
        }

        [Fact]
        public void Skill_values_preserve_nullable_version_semantics_and_snapshots_copy_values()
        {
            var unknown = new PlayerSkillValue(
                "perkMiner69r",
                SkillValueState.Unknown,
                null,
                null,
                null,
                null,
                null);
            var values = new List<PlayerSkillValue> { unknown };
            var snapshot = new PlayerSkillSnapshot(
                9,
                "EOS_1",
                "local",
                "world-1",
                Utc(1),
                "v3.0.1-b4",
                null,
                null,
                values);
            values.Clear();

            Assert.Null(unknown.Value);
            Assert.Null(snapshot.Level);
            Assert.Null(snapshot.SkillPoints);
            Assert.Single(snapshot.Values);
            Assert.Throws<ArgumentException>(() => new PlayerSkillValue(
                "perkMiner69r",
                SkillValueState.Known,
                null,
                null,
                null,
                null,
                null));
            Assert.Throws<ArgumentException>(() => new PlayerSkillValue(
                "perkMiner69r",
                SkillValueState.NotLoaded,
                1,
                null,
                null,
                null,
                null));
        }

        [Fact]
        public void Profile_sections_keep_independent_state_value_time_and_gap_metadata()
        {
            var gapSource = new List<PlayerEvidenceGap> { Gap(1, Utc(1), Utc(2)) };
            var inventory = new PlayerProfileSection<PlayerInventorySnapshot>(
                PlayerProfileSectionState.Partial,
                Utc(3),
                InventorySnapshot(3, Utc(3), Array.Empty<InventoryItemScalar>()),
                gapSource);
            var skills = new PlayerProfileSection<PlayerSkillSnapshot>(
                PlayerProfileSectionState.Unavailable,
                null,
                null,
                Array.Empty<PlayerEvidenceGap>());
            gapSource.Clear();

            var profile = new PlayerProfile(
                "EOS_1",
                new PlayerProfileSection<HistoricalPlayerSummary>(PlayerProfileSectionState.Unavailable, null, null, Array.Empty<PlayerEvidenceGap>()),
                new PlayerProfileSection<IReadOnlyList<PlayerSession>>(PlayerProfileSectionState.Unavailable, null, null, Array.Empty<PlayerEvidenceGap>()),
                new PlayerProfileSection<IReadOnlyList<PlayerActivityEvent>>(PlayerProfileSectionState.Unavailable, null, null, Array.Empty<PlayerEvidenceGap>()),
                inventory,
                skills,
                new PlayerProfileSection<IReadOnlyList<PlayerDailyActivitySummary>>(PlayerProfileSectionState.Unavailable, null, null, Array.Empty<PlayerEvidenceGap>()));

            Assert.Equal(PlayerProfileSectionState.Partial, profile.Inventory.State);
            Assert.Equal(PlayerProfileSectionState.Unavailable, profile.Skills.State);
            Assert.Single(profile.Inventory.GapMetadata);
            Assert.Throws<NotSupportedException>(() => ((IList<PlayerEvidenceGap>)profile.Inventory.GapMetadata).Clear());
            Assert.Throws<ArgumentException>(() => new PlayerProfile(
                " ",
                profile.Summary,
                profile.Sessions,
                profile.Activity,
                profile.Inventory,
                profile.Skills,
                profile.DailyActivity));
        }

        [Fact]
        public void Evidence_models_reject_non_utc_times_and_empty_stable_identity()
        {
            Assert.Throws<ArgumentException>(() => InventorySnapshot(1, Local(1), Array.Empty<InventoryItemScalar>()));
            Assert.Throws<ArgumentException>(() => new PlayerSkillSnapshot(1, "EOS_1", "local", "world-1", Local(1), "v3.0.1-b4", 1, 0, Array.Empty<PlayerSkillValue>()));
            Assert.Throws<ArgumentException>(() => new PlayerSession(1, "EOS_1", "local", "world-1", Local(1), null, null, null, PlayerProfileSectionState.Partial));
            Assert.Throws<ArgumentException>(() => new PlayerActivityEvent(1, "EOS_1", "local", "world-1", "PlayerJoined", Local(1), null, PlayerProfileSectionState.Available));
            Assert.Throws<ArgumentException>(() => new PlayerEvidenceGap(1, "EOS_1", Local(1), Utc(2), "QueueFull", 1));
            Assert.Throws<ArgumentException>(() => InventorySnapshot(1, Utc(1), Array.Empty<InventoryItemScalar>(), " "));
        }

        [Fact]
        public void Evidence_cursor_orders_observation_time_then_id_descending()
        {
            var newest = new PlayerEvidenceCursor(Utc(3), 1);
            var sameTimeHigherId = new PlayerEvidenceCursor(Utc(2), 9);
            var sameTimeLowerId = new PlayerEvidenceCursor(Utc(2), 2);

            var ordered = new[] { sameTimeLowerId, newest, sameTimeHigherId }.OrderBy(cursor => cursor).ToArray();

            Assert.Equal(new[] { newest, sameTimeHigherId, sameTimeLowerId }, ordered);
            Assert.Throws<ArgumentException>(() => new PlayerEvidenceCursor(Local(1), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerEvidenceCursor(Utc(1), 0));
        }

        [Fact]
        public void Evidence_queries_validate_identity_page_size_ranges_and_cursor()
        {
            var cursor = new PlayerEvidenceCursor(Utc(2), 9);
            var snapshots = new PlayerInventorySnapshotsQuery("EOS_1", 25, cursor);
            var diffs = new PlayerInventoryDiffsQuery("EOS_1", 25, cursor);
            var skills = new PlayerSkillSnapshotsQuery("EOS_1", 25, cursor);

            Assert.Equal("EOS_1", snapshots.CrossplatformId);
            Assert.Same(cursor, snapshots.Cursor);
            Assert.Same(cursor, diffs.Cursor);
            Assert.Same(cursor, skills.Cursor);
            Assert.Throws<ArgumentException>(() => new PlayerInventorySnapshotsQuery(" ", 25, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInventoryDiffsQuery("EOS_1", 0, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerSkillSnapshotsQuery("EOS_1", PlayerSkillSnapshotsQuery.MaximumPageSize + 1, null));
        }

        [Fact]
        public void Public_action_operation_is_a_fixed_redacted_summary()
        {
            var operation = Operation("operation-1", PlayerActionStatus.Succeeded, 1, 2);
            var properties = typeof(PlayerActionOperation).GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.Equal("GrantItem", operation.OperationType);
            Assert.Equal("EOS_1", operation.Target.CrossplatformId);
            Assert.Equal(1, operation.BeforeInventorySnapshotId);
            Assert.Equal(2, operation.AfterInventorySnapshotId);
            Assert.DoesNotContain(properties, property => ContainsForbiddenSummaryTerm(property.Name));
            Assert.DoesNotContain(typeof(PlayerActionOperation).GetConstructors().SelectMany(constructor => constructor.GetParameters()), parameter =>
                ContainsForbiddenSummaryTerm(parameter.Name ?? string.Empty));
            Assert.Throws<ArgumentException>(() => new PlayerActionOperation(
                "operation-2",
                "arbitrary-action",
                "owner",
                new PlayerTargetStamp("EOS_1", 17, Utc(1), "world-1"),
                PlayerActionStatus.Pending,
                Utc(1),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }

        [Fact]
        public void Store_and_operation_query_are_typed_without_generic_entity_or_payload_methods()
        {
            var storeMethods = typeof(IPlayerEvidenceStore).GetMethods();
            var operationMethods = typeof(IPlayerActionOperationQuery).GetMethods();

            Assert.Contains(storeMethods, method => method.Name == "AppendInventorySnapshot" && method.GetParameters().Single().ParameterType == typeof(PlayerInventorySnapshot));
            Assert.Contains(storeMethods, method => method.Name == "GetInventorySnapshots" && method.ReturnType == typeof(PlayerInventorySnapshotsPage));
            Assert.Contains(storeMethods, method => method.Name == "Compact" && method.GetParameters().Single().ParameterType == typeof(PlayerEvidenceCompactionRequest));
            Assert.DoesNotContain(storeMethods, method => method.IsGenericMethod || ContainsForbiddenSummaryTerm(method.Name) || method.Name.Contains("Entity"));
            var get = Assert.Single(operationMethods);
            Assert.Equal("Get", get.Name);
            Assert.Equal(typeof(PlayerActionOperation), Nullable.GetUnderlyingType(get.ReturnType) ?? get.ReturnType);
            Assert.Equal(typeof(string), Assert.Single(get.GetParameters()).ParameterType);
        }

        private static PlayerInventorySnapshot InventorySnapshot(
            long id,
            DateTimeOffset observedAtUtc,
            IEnumerable<InventoryItemScalar> items,
            string crossplatformId = "EOS_1",
            CatalogResolutionState catalogResolution = CatalogResolutionState.Resolved) =>
            new PlayerInventorySnapshot(
                id,
                crossplatformId,
                "local",
                "world-1",
                observedAtUtc,
                "v3.0.1-b4",
                catalogResolution == CatalogResolutionState.Resolved ? "catalog-1" : null,
                catalogResolution,
                "fingerprint-" + id,
                false,
                items);

        private static InventoryItemScalar Item(string container, int slot, string name, int count) =>
            new InventoryItemScalar(container, slot, name, count, null, null, Array.Empty<string>());

        private static PlayerEvidenceGap Gap(long id, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc) =>
            new PlayerEvidenceGap(id, "EOS_1", startedAtUtc, endedAtUtc, "QueueFull", 1);

        private static PlayerActionOperation Operation(
            string operationId,
            PlayerActionStatus status,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId) =>
            new PlayerActionOperation(
                operationId,
                PlayerActionOperationTypes.GrantItem,
                "owner",
                new PlayerTargetStamp("EOS_1", 17, Utc(1), "world-1"),
                status,
                Utc(1),
                Utc(1),
                Utc(2),
                null,
                beforeInventorySnapshotId,
                afterInventorySnapshotId,
                null,
                null,
                "correlation-1");

        private static string[] PublicProperties<T>() =>
            typeof(T)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OrderBy(property => property.MetadataToken)
                .Select(property => property.Name)
                .ToArray();

        private static bool ContainsForbiddenSummaryTerm(string value) =>
            value.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Payload", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Json", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void AssertEnum<T>(params string[] names) where T : struct, Enum =>
            Assert.Equal(names, Enum.GetNames(typeof(T)));

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, minute, 0, TimeSpan.Zero);

        private static DateTimeOffset Local(int minute) =>
            new DateTimeOffset(2026, 7, 26, 9, minute, 0, TimeSpan.FromHours(8));
    }
}
