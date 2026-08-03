using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Domain")]
    public sealed class PlayerInventoryDiffServiceTests
    {
        private readonly PlayerInventoryDiffService service = new PlayerInventoryDiffService();

        [Fact]
        public void Added_item_is_reported_as_an_observed_change()
        {
            var result = Compare(
                Snapshot(1, Utc(1)),
                Snapshot(2, Utc(2), Item("Bag", 0, "resourceWood", 2)));

            var change = Assert.Single(result.Changes);
            Assert.Equal(InventoryDiffKind.Added, change.Kind);
            Assert.Null(change.PreviousItem);
            Assert.Equal("resourceWood", change.CurrentItem?.InternalName);
            Assert.Equal(EvidenceLevel.ObservedChange, change.EvidenceLevel);
            Assert.True(result.IsComplete);
        }

        [Fact]
        public void Removed_item_is_reported_as_an_observed_change()
        {
            var result = Compare(
                Snapshot(1, Utc(1), Item("Bag", 0, "resourceWood", 2)),
                Snapshot(2, Utc(2)));

            var change = Assert.Single(result.Changes);
            Assert.Equal(InventoryDiffKind.Removed, change.Kind);
            Assert.Equal("resourceWood", change.PreviousItem?.InternalName);
            Assert.Null(change.CurrentItem);
        }

        [Fact]
        public void Quantity_change_preserves_both_observations()
        {
            var result = Compare(
                Snapshot(1, Utc(1), Item("Bag", 0, "resourceWood", 2)),
                Snapshot(2, Utc(2), Item("Bag", 0, "resourceWood", 5)));

            var change = Assert.Single(result.Changes);
            Assert.Equal(InventoryDiffKind.QuantityChanged, change.Kind);
            Assert.Equal(2, change.PreviousItem?.Count);
            Assert.Equal(5, change.CurrentItem?.Count);
        }

        [Fact]
        public void The_same_item_fingerprint_in_another_location_is_moved()
        {
            var result = Compare(
                Snapshot(1, Utc(1), Item("Bag", 0, "resourceWood", 2, 4, 0.5m, "modA")),
                Snapshot(2, Utc(2), Item("Toolbelt", 3, "resourceWood", 2, 4, 0.5m, "modA")));

            var change = Assert.Single(result.Changes);
            Assert.Equal(InventoryDiffKind.Moved, change.Kind);
            Assert.Equal("Bag", change.PreviousItem?.Container);
            Assert.Equal("Toolbelt", change.CurrentItem?.Container);
        }

        [Theory]
        [InlineData("quality")]
        [InlineData("use")]
        [InlineData("mods")]
        public void Quality_use_amount_or_mod_change_is_an_attribute_change(string changedAttribute)
        {
            var previous = Item("Bag", 0, "resourceWood", 2, 4, 0.5m, "modA");
            var current = changedAttribute switch
            {
                "quality" => Item("Bag", 0, "resourceWood", 2, 5, 0.5m, "modA"),
                "use" => Item("Bag", 0, "resourceWood", 2, 4, 0.75m, "modA"),
                _ => Item("Bag", 0, "resourceWood", 2, 4, 0.5m, "modB")
            };

            var result = Compare(Snapshot(1, Utc(1), previous), Snapshot(2, Utc(2), current));

            Assert.Equal(InventoryDiffKind.AttributesChanged, Assert.Single(result.Changes).Kind);
        }

        [Fact]
        public void Catalog_unavailability_makes_the_comparison_uncomparable()
        {
            var previous = Snapshot(1, Utc(1));
            var current = Snapshot(2, Utc(2), CatalogResolutionState.Unavailable);

            var result = Compare(previous, current);

            AssertUncomparable(result);
        }

        [Fact]
        public void Missing_adjacent_snapshot_makes_the_comparison_uncomparable()
        {
            var result = service.Compare(
                null,
                Snapshot(2, Utc(2)),
                Array.Empty<PlayerEvidenceGap>(),
                Array.Empty<PlayerActionOperation>());

            AssertUncomparable(result);
            Assert.Null(result.PreviousSnapshotId);
        }

        [Fact]
        public void Gap_intersecting_the_closed_comparison_interval_is_uncomparable()
        {
            var gaps = new[]
            {
                new PlayerEvidenceGap(1, "EOS_1", Utc(1), Utc(2), "QueueFull", 1)
            };

            var result = Compare(Snapshot(1, Utc(1)), Snapshot(2, Utc(2)), gaps);

            AssertUncomparable(result);
        }

        [Fact]
        public void Gap_outside_the_comparison_interval_does_not_hide_observed_changes()
        {
            var gaps = new[]
            {
                new PlayerEvidenceGap(1, "EOS_1", Utc(3), Utc(4), "QueueFull", 1)
            };

            var result = Compare(
                Snapshot(1, Utc(1)),
                Snapshot(2, Utc(2), Item("Bag", 0, "resourceWood", 1)),
                gaps);

            Assert.True(result.IsComplete);
            Assert.Equal(InventoryDiffKind.Added, Assert.Single(result.Changes).Kind);
        }

        [Fact]
        public void Succeeded_panel_operation_with_exact_snapshot_links_confirms_the_change()
        {
            var operation = Operation("operation-1", PlayerActionStatus.Succeeded, 1, 2);

            var result = Compare(
                Snapshot(1, Utc(1)),
                Snapshot(2, Utc(2), Item("Bag", 0, "resourceWood", 1)),
                confirmedOperations: new[] { operation });

            var change = Assert.Single(result.Changes);
            Assert.Equal(EvidenceLevel.Confirmed, change.EvidenceLevel);
            Assert.Equal("operation-1", Assert.Single(change.SourceOperationIds));
        }

        [Fact]
        public void Non_success_or_inexact_snapshot_links_remain_observed_changes()
        {
            var operations = new[]
            {
                Operation("failed", PlayerActionStatus.Failed, 1, 2),
                Operation("wrong-before", PlayerActionStatus.Succeeded, 9, 2),
                Operation("wrong-after", PlayerActionStatus.Succeeded, 1, 9),
                Operation("other-player", PlayerActionStatus.Succeeded, 1, 2, "EOS_2")
            };

            var result = Compare(
                Snapshot(1, Utc(1)),
                Snapshot(2, Utc(2), Item("Bag", 0, "resourceWood", 1)),
                confirmedOperations: operations);

            var change = Assert.Single(result.Changes);
            Assert.Equal(EvidenceLevel.ObservedChange, change.EvidenceLevel);
            Assert.Empty(change.SourceOperationIds);
        }

        private PlayerInventoryDiff Compare(
            PlayerInventorySnapshot previous,
            PlayerInventorySnapshot current,
            IEnumerable<PlayerEvidenceGap>? gaps = null,
            IEnumerable<PlayerActionOperation>? confirmedOperations = null) =>
            service.Compare(
                previous,
                current,
                gaps ?? Array.Empty<PlayerEvidenceGap>(),
                confirmedOperations ?? Array.Empty<PlayerActionOperation>());

        private static void AssertUncomparable(PlayerInventoryDiff result)
        {
            Assert.False(result.IsComplete);
            var change = Assert.Single(result.Changes);
            Assert.Equal(InventoryDiffKind.Uncomparable, change.Kind);
            Assert.Equal(EvidenceLevel.ObservedChange, change.EvidenceLevel);
            Assert.Empty(change.SourceOperationIds);
        }

        private static PlayerInventorySnapshot Snapshot(
            long id,
            DateTimeOffset observedAtUtc,
            params InventoryItemScalar[] items) =>
            Snapshot(id, observedAtUtc, CatalogResolutionState.Resolved, items);

        private static PlayerInventorySnapshot Snapshot(
            long id,
            DateTimeOffset observedAtUtc,
            CatalogResolutionState catalogResolution,
            params InventoryItemScalar[] items) =>
            new PlayerInventorySnapshot(
                id,
                "EOS_1",
                "local",
                "world-1",
                observedAtUtc,
                "v3.0.1-b4",
                catalogResolution == CatalogResolutionState.Resolved ? "catalog-1" : null,
                catalogResolution,
                "fingerprint-" + id,
                false,
                items);

        private static InventoryItemScalar Item(
            string container,
            int slot,
            string internalName,
            int count,
            int? quality = null,
            decimal? useAmount = null,
            params string[] mods) =>
            new InventoryItemScalar(container, slot, internalName, count, quality, useAmount, mods);

        private static PlayerActionOperation Operation(
            string id,
            PlayerActionStatus status,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            string crossplatformId = "EOS_1") =>
            new PlayerActionOperation(
                id,
                PlayerActionOperationTypes.GrantItem,
                "owner",
                new PlayerTargetStamp(crossplatformId, 17, Utc(1), "world-1"),
                status,
                Utc(1),
                Utc(1),
                Utc(2),
                status == PlayerActionStatus.Succeeded ? null : "failed",
                beforeInventorySnapshotId,
                afterInventorySnapshotId,
                null,
                null,
                "correlation-1");

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, minute, 0, TimeSpan.Zero);
    }
}
