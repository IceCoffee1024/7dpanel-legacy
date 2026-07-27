using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysGrantItemGateway : IGrantItemGateway
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly IGameResourceCatalog catalog;
        private readonly Func<
            string,
            Func<GrantItemGatewayResult>,
            TimeSpan,
            CancellationToken,
            Task<GrantItemGatewayResult>> dispatcher;
        private readonly Func<GrantItemCommand, GrantItemRuntimeContext?> captureContext;
        private readonly Func<
            GrantItemSnapshotCommand,
            CancellationToken,
            Task<GrantItemInventorySnapshot>> captureSnapshot;
        private readonly Func<DateTimeOffset> utcClock;

        public SevenDaysGrantItemGateway(
            IGameResourceCatalog catalog,
            Func<int, string, DateTimeOffset?> onlineObservedAtUtc,
            Func<
                GrantItemSnapshotCommand,
                CancellationToken,
                Task<GrantItemInventorySnapshot>> captureSnapshot)
            : this(
                catalog,
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                command => CaptureNativeContext(command, onlineObservedAtUtc),
                captureSnapshot,
                () => DateTimeOffset.UtcNow)
        {
            if (onlineObservedAtUtc == null)
                throw new ArgumentNullException(nameof(onlineObservedAtUtc));
        }

        internal SevenDaysGrantItemGateway(
            IGameResourceCatalog catalog,
            Func<
                string,
                Func<GrantItemGatewayResult>,
                TimeSpan,
                CancellationToken,
                Task<GrantItemGatewayResult>> dispatcher,
            Func<GrantItemCommand, GrantItemRuntimeContext?> captureContext,
            Func<
                GrantItemSnapshotCommand,
                CancellationToken,
                Task<GrantItemInventorySnapshot>> captureSnapshot,
            Func<DateTimeOffset> utcClock)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
            this.captureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public async Task<GrantItemInventorySnapshot> CaptureInventorySnapshotAsync(
            GrantItemSnapshotCommand command,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var snapshot = await captureSnapshot(command, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot == null)
            {
                throw new InvalidOperationException(
                    "The inventory snapshot capture did not return scalar evidence.");
            }
            return snapshot;
        }

        public async Task<GrantItemGatewayResult> GrantAsync(
            GrantItemCommand command,
            Func<DateTimeOffset, bool> tryStart,
            CancellationToken cancellationToken)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (tryStart == null) throw new ArgumentNullException(nameof(tryStart));

            var started = 0;
            try
            {
                return await dispatcher(
                        "7DPanel.Players.GrantItem",
                        () => ExecuteOnGameThread(
                            command,
                            tryStart,
                            cancellationToken,
                            ref started),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref started) == 0
                    ? GrantItemGatewayResult.Cancelled()
                    : GrantItemGatewayResult.ResultUnknown(
                        GrantItemFailureCodes.ResultUnknown);
            }
            catch
            {
                return Volatile.Read(ref started) == 0
                    ? GrantItemGatewayResult.Failed(GrantItemFailureCodes.GatewayFailure)
                    : GrantItemGatewayResult.ResultUnknown(
                        GrantItemFailureCodes.ResultUnknown);
            }
        }

        private GrantItemGatewayResult ExecuteOnGameThread(
            GrantItemCommand command,
            Func<DateTimeOffset, bool> tryStart,
            CancellationToken cancellationToken,
            ref int started)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var context = captureContext(command);
            if (context == null)
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.PlayerNotOnline);
            }
            if (context.Target != command.Target)
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.TargetChanged);
            }

            var read = catalog.Read();
            if (!CatalogStillMatches(read, command))
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.CatalogChanged);
            }
            if (!context.VersionSupported)
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.VersionUnsupported);
            }
            if (context.ApprovedBagCapacity < command.Quantity)
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.InsufficientSpace);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var startedAtUtc = utcClock();
            if (startedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("The grant start time must be UTC.");
            if (!tryStart(startedAtUtc))
            {
                return GrantItemGatewayResult.Rejected(
                    GrantItemFailureCodes.OperationStartConflict);
            }

            Volatile.Write(ref started, 1);
            var actualQuantity = context.CommitApprovedBag();
            return actualQuantity == command.Quantity
                ? GrantItemGatewayResult.Succeeded(actualQuantity)
                : GrantItemGatewayResult.ResultUnknown(
                    GrantItemFailureCodes.ResultUnknown);
        }

        private static bool CatalogStillMatches(
            GameResourceCatalogReadResult read,
            GrantItemCommand command)
        {
            if (read == null ||
                read.Status != GameResourceCatalogReadStatus.Available ||
                read.Snapshot == null)
            {
                return false;
            }

            var snapshot = read.Snapshot;
            if (!string.Equals(
                    snapshot.CatalogVersion,
                    command.CatalogVersion,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    snapshot.GameVersion,
                    command.GameVersion,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var entries = snapshot.Resources
                .Where(entry => string.Equals(
                    entry.ResourceId,
                    command.ResourceId,
                    StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (entries.Length != 1) return false;

            var entry = entries[0];
            return entry.NumericId == command.NumericId &&
                   string.Equals(
                       entry.InternalName,
                       command.InternalName,
                       StringComparison.Ordinal) &&
                   entry.Kind == command.ItemKind &&
                   entry.Kind == GameResourceKind.Item &&
                   entry.Visibility == command.Visibility &&
                   (entry.Visibility != GameResourceVisibility.Hidden ||
                    command.HiddenItemConfirmed) &&
                   entry.MaxStack == command.MaxStack &&
                   entry.HasQuality == command.HasQuality;
        }

        private static GrantItemRuntimeContext? CaptureNativeContext(
            GrantItemCommand command,
            Func<int, string, DateTimeOffset?> onlineObservedAtUtc)
        {
            var clients = global::ConnectionManager.Instance?.Clients;
            var client = clients?.ForEntityId(command.Target.EntityId);
            var combinedId = client?.CrossplatformId?.CombinedString;
            if (client == null || string.IsNullOrWhiteSpace(combinedId)) return null;

            var world = global::GameManager.Instance?.World;
            var player = world?.GetEntity(command.Target.EntityId) as global::EntityPlayer;
            var bag = player?.bag;
            var worldId = global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);
            var observedAtUtc = onlineObservedAtUtc(client.entityId, combinedId!);
            if (player == null || bag == null || string.IsNullOrWhiteSpace(worldId) ||
                !observedAtUtc.HasValue || observedAtUtc.Value.Offset != TimeSpan.Zero)
            {
                return null;
            }

            var currentTarget = new PlayerTargetStamp(
                combinedId!,
                client.entityId,
                observedAtUtc.Value,
                worldId);
            var versionSupported = string.Equals(
                global::Constants.cVersionInformation.ToString(),
                command.GameVersion,
                StringComparison.Ordinal);

            global::ItemStack[]? plannedSlots = null;
            try
            {
                var itemClass = global::ItemClass.GetItemClass(command.InternalName, false);
                var maxStack = itemClass?.Stacknumber?.Value;
                versionSupported = versionSupported &&
                    itemClass != null &&
                    itemClass.Id == command.NumericId &&
                    string.Equals(
                        itemClass.GetItemName(),
                        command.InternalName,
                        StringComparison.Ordinal) &&
                    maxStack == command.MaxStack &&
                    itemClass.HasQuality == command.HasQuality;
                if (versionSupported)
                {
                    var itemValue = command.Quality.HasValue
                        ? global::ItemClass.CreateItemValue(
                            command.InternalName,
                            command.Quality.Value,
                            false)
                        : global::ItemClass.GetItem(command.InternalName, false);
                    if (itemValue != null && !itemValue.IsEmpty())
                    {
                        plannedSlots = TryPlanNativeBag(
                            bag.GetSlots(),
                            itemValue,
                            command.Quantity,
                            command.MaxStack);
                    }
                    else
                    {
                        versionSupported = false;
                    }
                }
            }
            catch
            {
                versionSupported = false;
                plannedSlots = null;
            }

            return new GrantItemRuntimeContext(
                currentTarget,
                versionSupported,
                plannedSlots == null ? 0 : command.Quantity,
                () =>
                {
                    if (plannedSlots == null)
                        throw new InvalidOperationException("An approved bag plan is unavailable.");
                    bag.SetSlots(plannedSlots);
                    return command.Quantity;
                });
        }

        private static global::ItemStack[]? TryPlanNativeBag(
            global::ItemStack[]? sourceSlots,
            global::ItemValue itemValue,
            int quantity,
            int maxStack)
        {
            if (sourceSlots == null || sourceSlots.Length == 0) return null;

            var planned = global::ItemStack.Clone(sourceSlots);
            var remaining = quantity;
            for (var index = 0; index < planned.Length && remaining > 0; index++)
            {
                var slot = planned[index];
                if (slot == null || slot.IsEmpty()) continue;

                var candidate = new global::ItemStack(itemValue.Clone(), remaining);
                if (!slot.CanStackPartlyWith(candidate, out var moved)) continue;
                moved = Math.Min(moved, remaining);
                slot.count += moved;
                remaining -= moved;
            }
            for (var index = 0; index < planned.Length && remaining > 0; index++)
            {
                var slot = planned[index];
                if (slot != null && !slot.IsEmpty()) continue;

                var moved = Math.Min(maxStack, remaining);
                planned[index] = new global::ItemStack(itemValue.Clone(), moved);
                remaining -= moved;
            }

            return remaining == 0 ? planned : null;
        }
    }

    internal sealed class GrantItemRuntimeContext
    {
        public GrantItemRuntimeContext(
            PlayerTargetStamp target,
            bool versionSupported,
            int approvedBagCapacity,
            Func<int> commitApprovedBag)
        {
            if (approvedBagCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(approvedBagCapacity));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            VersionSupported = versionSupported;
            ApprovedBagCapacity = approvedBagCapacity;
            CommitApprovedBag = commitApprovedBag ??
                throw new ArgumentNullException(nameof(commitApprovedBag));
        }

        public PlayerTargetStamp Target { get; }
        public bool VersionSupported { get; }
        public int ApprovedBagCapacity { get; }
        public Func<int> CommitApprovedBag { get; }
    }

}
