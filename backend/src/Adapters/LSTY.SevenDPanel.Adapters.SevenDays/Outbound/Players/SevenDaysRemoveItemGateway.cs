using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysRemoveItemGateway : IRemoveItemGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<string, Func<RemoveItemGatewayResult>, TimeSpan, CancellationToken, Task<RemoveItemGatewayResult>> dispatcher;
        private readonly Func<RemoveItemCommand, RemoveItemGatewayResult> remove;

        public SevenDaysRemoveItemGateway(
            IOnlinePlayerQuery onlinePlayers,
            IGameResourceCatalog catalog)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                command => CaptureAndRemove(
                    command,
                    onlinePlayers ?? throw new ArgumentNullException(nameof(onlinePlayers)),
                    catalog ?? throw new ArgumentNullException(nameof(catalog))))
        {
        }

        internal SevenDaysRemoveItemGateway(
            Func<string, Func<RemoveItemGatewayResult>, TimeSpan, CancellationToken, Task<RemoveItemGatewayResult>> dispatcher,
            Func<RemoveItemCommand, RemoveItemGatewayResult> remove)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.remove = remove ?? throw new ArgumentNullException(nameof(remove));
        }

        public Task<RemoveItemGatewayResult> RemoveAsync(
            RemoveItemCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            return dispatcher(
                "7DPanel.Players.RemoveItem",
                () => remove(command),
                DispatchTimeout,
                cancellationToken);
        }

        private static RemoveItemGatewayResult CaptureAndRemove(
            RemoveItemCommand command,
            IOnlinePlayerQuery onlinePlayers,
            IGameResourceCatalog catalog)
        {
            if (!ThreadManager.IsMainThread())
                throw new InvalidOperationException("Item removal must run on the game thread.");

            var currentTarget = CaptureTarget(command, onlinePlayers);
            var currentCatalog = CaptureCatalog(command, catalog);
            var entity = CaptureEntity(command.Target.EntityId);
            if (entity == null || entity.bag == null)
            {
                return RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Rejected,
                    "player_not_online");
            }

            var nativeSlots = global::ItemStack.Clone(entity.bag.GetSlots());
            var scalarSlots = new List<RemoveItemBagSlot>();
            for (var slot = 0; slot < nativeSlots.Length; slot++)
            {
                var stack = nativeSlots[slot];
                if (stack == null || stack.IsEmpty()) continue;
                var internalName = Normalize(stack.itemValue?.ItemClass?.GetItemName());
                if (internalName == null) continue;
                scalarSlots.Add(new RemoveItemBagSlot(
                    "bag",
                    slot,
                    internalName,
                    stack.count,
                    NormalizeQuality(stack.itemValue),
                    CopyMods(stack.itemValue)));
            }

            return RemoveFromBag(
                command,
                currentTarget,
                currentCatalog,
                scalarSlots,
                changes =>
                {
                    foreach (var change in changes)
                    {
                        nativeSlots[change.Slot] = change.RemainingCount == 0
                            ? global::ItemStack.Empty
                            : new global::ItemStack(
                                nativeSlots[change.Slot].itemValue.Clone(),
                                change.RemainingCount);
                    }
                    entity.bag.SetSlots(nativeSlots);
                },
                () => CaptureInventory(entity, currentCatalog));
        }

        internal static RemoveItemGatewayResult RemoveFromBag(
            RemoveItemCommand command,
            RemoveItemTargetState? currentTarget,
            RemoveItemCatalogState? currentCatalog,
            IReadOnlyList<RemoveItemBagSlot> slots,
            Action<IReadOnlyList<RemoveItemSlotChange>> apply,
            Func<RemoveItemInventorySnapshot> captureSnapshot)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            if (captureSnapshot == null) throw new ArgumentNullException(nameof(captureSnapshot));
            if (slots.Any(slot => slot == null))
                throw new ArgumentException("Removal slots cannot contain null.", nameof(slots));

            var targetFailure = ValidateTarget(command.Target, currentTarget);
            if (targetFailure != null)
                return RemoveItemGatewayResult.Terminal(RemoveItemGatewayStatus.Rejected, targetFailure);
            var catalogFailure = ValidateCatalog(command, currentCatalog);
            if (catalogFailure != null)
                return RemoveItemGatewayResult.Terminal(RemoveItemGatewayStatus.Rejected, catalogFailure);
            if (command.RemovalScope != PlayerItemRemovalScope.BagOnly)
            {
                return RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Rejected,
                    "unsupported_removal_scope");
            }

            var scanned = slots
                .OrderBy(slot => slot.Container, StringComparer.Ordinal)
                .ThenBy(slot => slot.Slot)
                .Select(slot => new ScannedSlot(slot, slot.ReadCount()))
                .ToArray();
            var candidates = scanned
                .Where(slot =>
                    string.Equals(slot.Source.Container, "bag", StringComparison.Ordinal) &&
                    string.Equals(
                        slot.Source.InternalName,
                        command.InternalName,
                        StringComparison.Ordinal) &&
                    (!command.Quality.HasValue || slot.Source.Quality == command.Quality))
                .ToArray();
            var available = 0L;
            foreach (var candidate in candidates)
                available += candidate.Count;

            if (command.RemovalMode == PlayerItemRemovalMode.Exact && available < command.Quantity)
            {
                return RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Rejected,
                    "insufficient_inventory");
            }

            var actualQuantity = command.RemovalMode == PlayerItemRemovalMode.Exact
                ? command.Quantity
                : (int)Math.Min(command.Quantity, available);
            var remainingToRemove = actualQuantity;
            var changes = new List<RemoveItemSlotChange>();
            foreach (var candidate in candidates)
            {
                if (remainingToRemove == 0) break;
                var removed = Math.Min(candidate.Count, remainingToRemove);
                changes.Add(new RemoveItemSlotChange(
                    candidate.Source.Container,
                    candidate.Source.Slot,
                    removed,
                    candidate.Count - removed));
                remainingToRemove -= removed;
            }

            RemoveItemInventorySnapshot before;
            try { before = captureSnapshot(); }
            catch
            {
                return RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.Failed,
                    "before_inventory_snapshot_unavailable");
            }

            if (changes.Count != 0)
            {
                try { apply(Array.AsReadOnly(changes.ToArray())); }
                catch
                {
                    return RemoveItemGatewayResult.Terminal(
                        RemoveItemGatewayStatus.ResultUnknown,
                        "remove_item_result_unknown");
                }
            }

            RemoveItemInventorySnapshot after;
            try { after = captureSnapshot(); }
            catch
            {
                return RemoveItemGatewayResult.Terminal(
                    RemoveItemGatewayStatus.ResultUnknown,
                    "after_inventory_snapshot_unavailable");
            }

            return RemoveItemGatewayResult.Succeeded(actualQuantity, before, after);
        }

        private static RemoveItemTargetState? CaptureTarget(
            RemoveItemCommand command,
            IOnlinePlayerQuery onlinePlayers)
        {
            OnlinePlayersSnapshot snapshot;
            try
            {
                snapshot = onlinePlayers.GetOnlineAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                return null;
            }

            var observed = snapshot.Players
                .Where(player => player.EntityId == command.Target.EntityId)
                .ToArray();
            if (observed.Length != 1) return null;

            var client = global::ConnectionManager.Instance?.Clients?.ForEntityId(
                command.Target.EntityId);
            if (client == null) return null;
            var combinedId = Normalize(client.CrossplatformId?.CombinedString);
            if (combinedId == null) return null;
            var projectionId = observed[0].CrossplatformIdentity?.CombinedId;
            if (!string.Equals(projectionId, combinedId, StringComparison.Ordinal)) return null;

            return new RemoveItemTargetState(
                combinedId,
                client.entityId,
                observed[0].ObservedAtUtc,
                global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld));
        }

        private static RemoveItemCatalogState? CaptureCatalog(
            RemoveItemCommand command,
            IGameResourceCatalog catalog)
        {
            GameResourceCatalogReadResult read;
            try { read = catalog.Read(); }
            catch { return null; }
            if (read.Status != GameResourceCatalogReadStatus.Available || read.Snapshot == null)
                return null;
            var matches = read.Snapshot.Resources
                .Where(resource => string.Equals(
                    resource.ResourceId,
                    command.ResourceId,
                    StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1) return null;
            return new RemoveItemCatalogState(
                read.Snapshot.CatalogVersion,
                matches[0].ResourceId,
                matches[0].InternalName,
                matches[0].Kind,
                matches[0].HasQuality);
        }

        private static global::EntityPlayer? CaptureEntity(int entityId)
        {
            var players = global::GameManager.Instance?.World?.Players?.dict;
            if (players == null) return null;
            return players.TryGetValue(entityId, out var entity) ? entity : null;
        }

        private static RemoveItemInventorySnapshot CaptureInventory(
            global::EntityPlayer entity,
            RemoveItemCatalogState? catalog)
        {
            var items = new List<InventoryItemScalar>();
            CopyStacks("bag", entity.bag?.GetSlots(), items);
            if (entity.inventory != null)
            {
                for (var slot = 0; slot < entity.inventory.PUBLIC_SLOTS; slot++)
                    CopyStack("toolbelt", slot, entity.inventory.GetItemStack(slot), items);
            }
            var equipment = entity.equipment?.GetItems();
            if (equipment != null)
            {
                for (var slot = 0; slot < equipment.Length; slot++)
                {
                    var value = equipment[slot];
                    if (value == null || value.IsEmpty()) continue;
                    CopyItem("equipment", slot, value, 1, items);
                }
            }

            return new RemoveItemInventorySnapshot(
                DateTimeOffset.UtcNow,
                global::Constants.cVersionInformation.ToString(),
                catalog?.CatalogVersion,
                catalog == null
                    ? CatalogResolutionState.Unavailable
                    : CatalogResolutionState.Resolved,
                Fingerprint(items),
                items);
        }

        private static void CopyStacks(
            string container,
            global::ItemStack[]? stacks,
            ICollection<InventoryItemScalar> target)
        {
            if (stacks == null) return;
            for (var slot = 0; slot < stacks.Length; slot++)
                CopyStack(container, slot, stacks[slot], target);
        }

        private static void CopyStack(
            string container,
            int slot,
            global::ItemStack? stack,
            ICollection<InventoryItemScalar> target)
        {
            if (stack == null || stack.IsEmpty()) return;
            CopyItem(container, slot, stack.itemValue, stack.count, target);
        }

        private static void CopyItem(
            string container,
            int slot,
            global::ItemValue? value,
            int count,
            ICollection<InventoryItemScalar> target)
        {
            if (value == null || value.IsEmpty() || count <= 0) return;
            var internalName = Normalize(value.ItemClass?.GetItemName());
            if (internalName == null) return;
            decimal? useAmount = null;
            if (!float.IsNaN(value.UseTimes) && !float.IsInfinity(value.UseTimes) &&
                value.UseTimes >= 0)
            {
                useAmount = (decimal)value.UseTimes;
            }
            target.Add(new InventoryItemScalar(
                container,
                slot,
                internalName,
                count,
                NormalizeQuality(value),
                useAmount,
                CopyMods(value)));
        }

        private static int? NormalizeQuality(global::ItemValue? value) =>
            value == null || value.Quality == 0 ? null : (int?)value.Quality;

        private static IReadOnlyList<string> CopyMods(global::ItemValue? value) =>
            (value?.Modifications ?? Array.Empty<global::ItemValue>())
                .Where(mod => mod != null && !mod.IsEmpty() &&
                              mod.ItemClass is global::ItemClassModifier)
                .Select(mod => Normalize(mod.ItemClass?.GetItemName()))
                .Where(name => name != null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        private static string Fingerprint(IEnumerable<InventoryItemScalar> items)
        {
            var canonical = new StringBuilder();
            foreach (var item in items.OrderBy(value => value.Container, StringComparer.Ordinal)
                         .ThenBy(value => value.Slot))
            {
                canonical.Append(item.Container).Append('\u001f')
                    .Append(item.Slot.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                    .Append(item.InternalName).Append('\u001f')
                    .Append(item.Count.ToString(CultureInfo.InvariantCulture)).Append('\u001f')
                    .Append(item.Quality?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\u001f')
                    .Append(item.UseAmount?.ToString(CultureInfo.InvariantCulture) ?? "null").Append('\u001f')
                    .Append(string.Join("\u001e", item.ModInternalNames)).Append('\n');
            }
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(
                        Encoding.UTF8.GetBytes(canonical.ToString())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
            }
        }

        private static string? ValidateTarget(
            PlayerTargetStamp expected,
            RemoveItemTargetState? current)
        {
            if (current == null) return "player_not_online";
            return string.Equals(
                       current.CrossplatformId,
                       expected.CrossplatformId,
                       StringComparison.Ordinal) &&
                   current.EntityId == expected.EntityId &&
                   current.ObservedAtUtc == expected.OnlineObservedAtUtc &&
                   string.Equals(current.WorldId, expected.WorldId, StringComparison.Ordinal)
                ? null
                : "player_target_changed";
        }

        private static string? ValidateCatalog(
            RemoveItemCommand command,
            RemoveItemCatalogState? current)
        {
            if (current == null) return "catalog_unavailable";
            if (!string.Equals(current.CatalogVersion, command.CatalogVersion, StringComparison.Ordinal) ||
                !string.Equals(current.ResourceId, command.ResourceId, StringComparison.Ordinal) ||
                !string.Equals(current.InternalName, command.InternalName, StringComparison.Ordinal) ||
                current.ItemKind != command.ItemKind)
            {
                return "catalog_changed";
            }
            return command.Quality.HasValue && current.HasQuality == false
                ? "quality_not_supported"
                : null;
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        private sealed class ScannedSlot
        {
            public ScannedSlot(RemoveItemBagSlot source, int count)
            {
                Source = source;
                Count = count;
            }

            public RemoveItemBagSlot Source { get; }
            public int Count { get; }
        }

        internal sealed class RemoveItemTargetState
        {
            public RemoveItemTargetState(
                string crossplatformId,
                int entityId,
                DateTimeOffset observedAtUtc,
                string worldId)
            {
                CrossplatformId = crossplatformId;
                EntityId = entityId;
                ObservedAtUtc = observedAtUtc;
                WorldId = worldId;
            }

            public string CrossplatformId { get; }
            public int EntityId { get; }
            public DateTimeOffset ObservedAtUtc { get; }
            public string WorldId { get; }

            public RemoveItemTargetState With(
                string? crossplatformId = null,
                int? entityId = null,
                DateTimeOffset? observedAtUtc = null,
                string? worldId = null) =>
                new RemoveItemTargetState(
                    crossplatformId ?? CrossplatformId,
                    entityId ?? EntityId,
                    observedAtUtc ?? ObservedAtUtc,
                    worldId ?? WorldId);
        }

        internal sealed class RemoveItemCatalogState
        {
            public RemoveItemCatalogState(
                string catalogVersion,
                string resourceId,
                string internalName,
                GameResourceKind itemKind,
                bool? hasQuality)
            {
                CatalogVersion = catalogVersion;
                ResourceId = resourceId;
                InternalName = internalName;
                ItemKind = itemKind;
                HasQuality = hasQuality;
            }

            public string CatalogVersion { get; }
            public string ResourceId { get; }
            public string InternalName { get; }
            public GameResourceKind ItemKind { get; }
            public bool? HasQuality { get; }

            public RemoveItemCatalogState With(
                string? catalogVersion = null,
                string? resourceId = null,
                string? internalName = null,
                GameResourceKind? itemKind = null) =>
                new RemoveItemCatalogState(
                    catalogVersion ?? CatalogVersion,
                    resourceId ?? ResourceId,
                    internalName ?? InternalName,
                    itemKind ?? ItemKind,
                    HasQuality);
        }

        internal sealed class RemoveItemBagSlot
        {
            private readonly Action<int>? onRead;

            public RemoveItemBagSlot(
                string container,
                int slot,
                string internalName,
                int count,
                int? quality,
                IEnumerable<string> mods,
                Action<int>? onRead = null)
            {
                Container = container;
                Slot = slot;
                InternalName = internalName;
                Count = count;
                Quality = quality;
                Mods = Array.AsReadOnly(mods.ToArray());
                this.onRead = onRead;
            }

            public string Container { get; }
            public int Slot { get; }
            public string InternalName { get; }
            public int Count { get; }
            public int? Quality { get; }
            public IReadOnlyList<string> Mods { get; }

            internal int ReadCount()
            {
                onRead?.Invoke(Slot);
                return Count;
            }
        }

        internal sealed class RemoveItemSlotChange
        {
            public RemoveItemSlotChange(
                string container,
                int slot,
                int removedCount,
                int remainingCount)
            {
                Container = container;
                Slot = slot;
                RemovedCount = removedCount;
                RemainingCount = remainingCount;
            }

            public string Container { get; }
            public int Slot { get; }
            public int RemovedCount { get; }
            public int RemainingCount { get; }
        }
    }
}
