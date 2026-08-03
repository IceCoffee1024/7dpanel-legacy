using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysRemoveItemGatewayTests
    {
        private static readonly DateTimeOffset ObservedAtUtc =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Remove_runs_inside_the_dispatcher_and_forwards_cancellation()
        {
            var dispatched = false;
            CancellationToken forwarded = default;
            var expected = RemoveItemGatewayResult.Terminal(
                RemoveItemGatewayStatus.Rejected,
                "test-rejection");
            var gateway = new SevenDaysRemoveItemGateway(
                (name, action, timeout, cancellationToken) =>
                {
                    Assert.Equal("7DPanel.Players.RemoveItem", name);
                    Assert.True(timeout > TimeSpan.Zero);
                    dispatched = true;
                    forwarded = cancellationToken;
                    return Task.FromResult(action());
                },
                _ =>
                {
                    Assert.True(dispatched);
                    return expected;
                });
            using var cancellation = new CancellationTokenSource();

            var result = await gateway.RemoveAsync(Command(), cancellation.Token);

            Assert.Same(expected, result);
            Assert.Equal(cancellation.Token, forwarded);
        }

        [Theory]
        [InlineData("crossplatform")]
        [InlineData("entity")]
        [InlineData("observed")]
        [InlineData("world")]
        [InlineData("catalog-version")]
        [InlineData("resource")]
        [InlineData("internal-name")]
        [InlineData("kind")]
        public void Fixed_target_and_catalog_changes_reject_before_apply(string mismatch)
        {
            var target = CurrentTarget();
            var catalog = CurrentCatalog();
            if (mismatch == "crossplatform") target = target.With(crossplatformId: "EOS_replacement");
            if (mismatch == "entity") target = target.With(entityId: 8);
            if (mismatch == "observed") target = target.With(observedAtUtc: ObservedAtUtc.AddSeconds(1));
            if (mismatch == "world") target = target.With(worldId: "Pregen10k");
            if (mismatch == "catalog-version") catalog = catalog.With(catalogVersion: "catalog-8");
            if (mismatch == "resource") catalog = catalog.With(resourceId: "resource-clay");
            if (mismatch == "internal-name") catalog = catalog.With(internalName: "resourceClay");
            if (mismatch == "kind") catalog = catalog.With(itemKind: GameResourceKind.Block);
            var applyCalls = 0;

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(),
                target,
                catalog,
                Slots(Slot(0, 4)),
                _ => applyCalls++,
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Rejected, result.Status);
            Assert.Equal(0, applyCalls);
            Assert.Null(result.ActualQuantity);
        }

        [Fact]
        public void Exact_shortage_scans_every_slot_and_has_zero_side_effect()
        {
            var visited = new List<int>();
            var applyCalls = 0;
            var slots = Slots(
                Slot(9, 1, onRead: visited.Add),
                Slot(1, 1, onRead: visited.Add),
                Slot(5, 1, internalName: "resourceClay", onRead: visited.Add));

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 3),
                CurrentTarget(),
                CurrentCatalog(),
                slots,
                _ => applyCalls++,
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Rejected, result.Status);
            Assert.Equal("insufficient_inventory", result.FailureCode);
            Assert.Equal(new[] { 1, 5, 9 }, visited.OrderBy(value => value));
            Assert.Equal(0, applyCalls);
            Assert.Equal(new[] { 1, 1, 1 }, slots.Select(slot => slot.Count));
        }

        [Fact]
        public void Exact_applies_one_precomputed_change_set_in_stable_container_slot_order()
        {
            IReadOnlyList<SevenDaysRemoveItemGateway.RemoveItemSlotChange>? applied = null;
            var applyCalls = 0;
            var slots = Slots(Slot(7, 3), Slot(2, 2), Slot(4, 8, internalName: "resourceClay"));

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 4),
                CurrentTarget(),
                CurrentCatalog(),
                slots,
                changes =>
                {
                    applyCalls++;
                    applied = changes;
                },
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Succeeded, result.Status);
            Assert.Equal(4, result.ActualQuantity);
            Assert.Equal(1, applyCalls);
            Assert.Equal(new[] { 2, 7 }, applied!.Select(change => change.Slot));
            Assert.Equal(new[] { 0, 1 }, applied.Select(change => change.RemainingCount));
        }

        [Fact]
        public void Quality_and_mod_variants_are_selected_reproducibly_by_slot()
        {
            IReadOnlyList<SevenDaysRemoveItemGateway.RemoveItemSlotChange>? applied = null;
            var slots = Slots(
                Slot(8, 1, quality: 2, mods: new[] { "modB" }),
                Slot(1, 1, quality: 2, mods: new[] { "modA" }),
                Slot(0, 5, quality: 1, mods: new[] { "modC" }));

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 2, quality: 2),
                CurrentTarget(),
                CurrentCatalog(hasQuality: true),
                slots,
                changes => applied = changes,
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Succeeded, result.Status);
            var changes = Assert.IsAssignableFrom<
                IReadOnlyList<SevenDaysRemoveItemGateway.RemoveItemSlotChange>>(applied);
            Assert.Equal(new[] { 1, 8 }, changes.Select(change => change.Slot));
            Assert.All(changes, change => Assert.Equal(0, change.RemainingCount));
        }

        [Fact]
        public void Up_to_available_removes_only_the_scanned_available_quantity()
        {
            IReadOnlyList<SevenDaysRemoveItemGateway.RemoveItemSlotChange>? applied = null;

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 9, removalMode: PlayerItemRemovalMode.UpToAvailable),
                CurrentTarget(),
                CurrentCatalog(),
                Slots(Slot(6, 2), Slot(3, 1)),
                changes => applied = changes,
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Succeeded, result.Status);
            Assert.Equal(3, result.ActualQuantity);
            Assert.Equal(new[] { 3, 6 }, applied!.Select(change => change.Slot));
        }

        [Fact]
        public void Only_bag_slots_are_eligible_even_when_other_containers_have_more_items()
        {
            var applyCalls = 0;

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 2),
                CurrentTarget(),
                CurrentCatalog(),
                Slots(
                    Slot(0, 1, container: "bag"),
                    Slot(0, 9, container: "toolbelt"),
                    Slot(0, 9, container: "equipment")),
                _ => applyCalls++,
                Snapshot);

            Assert.Equal(RemoveItemGatewayStatus.Rejected, result.Status);
            Assert.Equal("insufficient_inventory", result.FailureCode);
            Assert.Equal(0, applyCalls);
        }

        [Fact]
        public void Success_captures_before_and_after_snapshots_around_the_single_apply()
        {
            var captureCount = 0;
            var applied = false;

            var result = SevenDaysRemoveItemGateway.RemoveFromBag(
                Command(quantity: 1),
                CurrentTarget(),
                CurrentCatalog(),
                Slots(Slot(0, 2)),
                _ => applied = true,
                () =>
                {
                    captureCount++;
                    return Snapshot(fingerprint: applied ? "after" : "before");
                });

            Assert.Equal(RemoveItemGatewayStatus.Succeeded, result.Status);
            Assert.Equal(2, captureCount);
            Assert.Equal("before", result.BeforeInventory!.Fingerprint);
            Assert.Equal("after", result.AfterInventory!.Fingerprint);
        }

        private static RemoveItemCommand Command(
            int quantity = 3,
            int? quality = null,
            PlayerItemRemovalMode removalMode = PlayerItemRemovalMode.Exact) =>
            new RemoveItemCommand(
                new PlayerTargetStamp("EOS_123", 7, ObservedAtUtc, "Navezgane"),
                "catalog-7",
                "resource-iron",
                "resourceIron",
                GameResourceKind.Item,
                quantity,
                quality,
                PlayerItemRemovalScope.BagOnly,
                removalMode);

        private static SevenDaysRemoveItemGateway.RemoveItemTargetState CurrentTarget() =>
            new SevenDaysRemoveItemGateway.RemoveItemTargetState(
                "EOS_123",
                7,
                ObservedAtUtc,
                "Navezgane");

        private static SevenDaysRemoveItemGateway.RemoveItemCatalogState CurrentCatalog(
            bool? hasQuality = false) =>
            new SevenDaysRemoveItemGateway.RemoveItemCatalogState(
                "catalog-7",
                "resource-iron",
                "resourceIron",
                GameResourceKind.Item,
                hasQuality);

        private static SevenDaysRemoveItemGateway.RemoveItemBagSlot Slot(
            int slot,
            int count,
            string container = "bag",
            string internalName = "resourceIron",
            int? quality = null,
            IEnumerable<string>? mods = null,
            Action<int>? onRead = null) =>
            new SevenDaysRemoveItemGateway.RemoveItemBagSlot(
                container,
                slot,
                internalName,
                count,
                quality,
                mods ?? Array.Empty<string>(),
                onRead);

        private static IReadOnlyList<SevenDaysRemoveItemGateway.RemoveItemBagSlot> Slots(
            params SevenDaysRemoveItemGateway.RemoveItemBagSlot[] slots) => slots;

        private static RemoveItemInventorySnapshot Snapshot() => Snapshot("snapshot");

        private static RemoveItemInventorySnapshot Snapshot(string fingerprint) =>
            new RemoveItemInventorySnapshot(
                ObservedAtUtc,
                "3.0.1-b4",
                "catalog-7",
                CatalogResolutionState.Resolved,
                fingerprint,
                Array.Empty<InventoryItemScalar>());
    }
}
