using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public enum SevenDaysRuntimeMaintenanceOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class SevenDaysRuntimeMaintenanceResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string MapResourceVersionChanged = "map_resource_version_changed";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string DispatchCancelled = "game_thread_dispatch_cancelled";
        public const string ResultUnknown = "result_unknown";

        private SevenDaysRuntimeMaintenanceResult(
            SevenDaysRuntimeMaintenanceOutcome outcome,
            string? errorCode)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
        }

        public SevenDaysRuntimeMaintenanceOutcome Outcome { get; }
        public string? ErrorCode { get; }

        internal static SevenDaysRuntimeMaintenanceResult Succeeded() =>
            new SevenDaysRuntimeMaintenanceResult(
                SevenDaysRuntimeMaintenanceOutcome.Succeeded,
                null);

        internal static SevenDaysRuntimeMaintenanceResult Rejected(string errorCode) =>
            new SevenDaysRuntimeMaintenanceResult(
                SevenDaysRuntimeMaintenanceOutcome.Rejected,
                RequireErrorCode(errorCode));

        internal static SevenDaysRuntimeMaintenanceResult Failed(string errorCode) =>
            new SevenDaysRuntimeMaintenanceResult(
                SevenDaysRuntimeMaintenanceOutcome.Failed,
                RequireErrorCode(errorCode));

        internal static SevenDaysRuntimeMaintenanceResult Unknown() =>
            new SevenDaysRuntimeMaintenanceResult(
                SevenDaysRuntimeMaintenanceOutcome.ResultUnknown,
                ResultUnknown);

        private static string RequireErrorCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An error code is required.", nameof(value));
            return value;
        }
    }

    public sealed class SevenDaysRuntimeMaintenanceHandler
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<
            string,
            Func<SevenDaysRuntimeMaintenanceResult>,
            TimeSpan,
            CancellationToken,
            Task<SevenDaysRuntimeMaintenanceResult>> dispatcher;
        private readonly Func<WorldOperationIntent, SevenDaysRuntimeMaintenanceContext?> captureContext;

        public SevenDaysRuntimeMaintenanceHandler()
            : this(() => null)
        {
        }

        public SevenDaysRuntimeMaintenanceHandler(Func<string?> currentMapResourceVersion)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CreateNativeContextCapture(currentMapResourceVersion))
        {
        }

        internal SevenDaysRuntimeMaintenanceHandler(
            Func<
                string,
                Func<SevenDaysRuntimeMaintenanceResult>,
                TimeSpan,
                CancellationToken,
                Task<SevenDaysRuntimeMaintenanceResult>> dispatcher,
            Func<WorldOperationIntent, SevenDaysRuntimeMaintenanceContext?> captureContext)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
        }

        public async Task<SevenDaysRuntimeMaintenanceResult> HandleAsync(
            WorldOperationIntent intent,
            CancellationToken cancellationToken)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));

            var shapeError = ValidateIntentShape(intent);
            if (shapeError != null)
                return SevenDaysRuntimeMaintenanceResult.Rejected(shapeError);

            var sideEffectStarted = 0;
            try
            {
                return await dispatcher(
                        "7DPanel.World.RuntimeMaintenance",
                        () => ExecuteOnGameThread(
                            intent,
                            cancellationToken,
                            ref sideEffectStarted),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (intent.Kind == WorldOperationKind.CollectGarbage)
                    return SevenDaysRuntimeMaintenanceResult.Unknown();
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysRuntimeMaintenanceResult.Failed(
                        SevenDaysRuntimeMaintenanceResult.DispatchFailed)
                    : SevenDaysRuntimeMaintenanceResult.Unknown();
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysRuntimeMaintenanceResult.Failed(
                        SevenDaysRuntimeMaintenanceResult.DispatchCancelled)
                    : SevenDaysRuntimeMaintenanceResult.Unknown();
            }
            catch
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysRuntimeMaintenanceResult.Failed(
                        SevenDaysRuntimeMaintenanceResult.DispatchFailed)
                    : SevenDaysRuntimeMaintenanceResult.Unknown();
            }
        }

        private SevenDaysRuntimeMaintenanceResult ExecuteOnGameThread(
            WorldOperationIntent intent,
            CancellationToken cancellationToken,
            ref int sideEffectStarted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = captureContext(intent);
            if (context == null || !context.WorldAvailable)
            {
                return SevenDaysRuntimeMaintenanceResult.Rejected(
                    SevenDaysRuntimeMaintenanceResult.WorldUnavailable);
            }

            var validationError = ValidateCurrentContext(intent, context);
            if (validationError != null)
                return SevenDaysRuntimeMaintenanceResult.Rejected(validationError);

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref sideEffectStarted, 1);

            bool completed;
            switch (intent.Kind)
            {
                case WorldOperationKind.ReloadBlocks:
                    completed = context.ReloadBlocks();
                    break;
                case WorldOperationKind.ReloadItems:
                    completed = context.ReloadItems();
                    break;
                case WorldOperationKind.ReloadEntityClasses:
                    completed = context.ReloadEntityClasses();
                    break;
                case WorldOperationKind.ReloadPrefabs:
                    completed = context.ReloadPrefabs();
                    break;
                case WorldOperationKind.CollectGarbage:
                    completed = context.CollectGarbage();
                    break;
                default:
                    return SevenDaysRuntimeMaintenanceResult.Rejected(
                        SevenDaysRuntimeMaintenanceResult.OperationKindNotSupported);
            }

            return completed
                ? SevenDaysRuntimeMaintenanceResult.Succeeded()
                : SevenDaysRuntimeMaintenanceResult.Unknown();
        }

        private static string? ValidateIntentShape(WorldOperationIntent intent)
        {
            switch (intent.Kind)
            {
                case WorldOperationKind.ReloadBlocks:
                case WorldOperationKind.ReloadItems:
                case WorldOperationKind.ReloadEntityClasses:
                case WorldOperationKind.ReloadPrefabs:
                case WorldOperationKind.CollectGarbage:
                    return intent.Target is WorldMaintenanceOperationTarget target &&
                           target.EntityTypeResourceId == null
                        ? null
                        : SevenDaysRuntimeMaintenanceResult.TargetInvalid;
                default:
                    return SevenDaysRuntimeMaintenanceResult.OperationKindNotSupported;
            }
        }

        private static string? ValidateCurrentContext(
            WorldOperationIntent intent,
            SevenDaysRuntimeMaintenanceContext context)
        {
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysRuntimeMaintenanceResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysRuntimeMaintenanceResult.WorldVersionChanged;
            return string.Equals(
                    context.MapResourceVersion,
                    intent.MapResourceVersion,
                    StringComparison.Ordinal)
                ? null
                : SevenDaysRuntimeMaintenanceResult.MapResourceVersionChanged;
        }

        private static Func<WorldOperationIntent, SevenDaysRuntimeMaintenanceContext?>
            CreateNativeContextCapture(Func<string?> currentMapResourceVersion)
        {
            if (currentMapResourceVersion == null)
                throw new ArgumentNullException(nameof(currentMapResourceVersion));
            return intent => CaptureNativeContext(currentMapResourceVersion);
        }

        private static SevenDaysRuntimeMaintenanceContext CaptureNativeContext(
            Func<string?> currentMapResourceVersion)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null || string.IsNullOrWhiteSpace(world.Guid))
                return SevenDaysRuntimeMaintenanceContext.Unavailable();

            var worldId = world.Guid;
            var worldVersion = worldId + ":" +
                world.worldTime.ToString(CultureInfo.InvariantCulture);
            return SevenDaysRuntimeMaintenanceContext.Available(
                worldId,
                worldVersion,
                currentMapResourceVersion(),
                ReloadBlocks,
                ReloadItems,
                ReloadEntityClasses,
                () => ReloadPrefabs(world),
                CollectGarbage);
        }

        private static bool ReloadBlocks()
        {
            global::WorldStaticData.Reset("blocks");
            return true;
        }

        private static bool ReloadItems()
        {
            global::WorldStaticData.Reset("items");
            return true;
        }

        private static bool ReloadEntityClasses()
        {
            global::WorldStaticData.Reset("entityclasses");
            global::WorldStaticData.Reset("entitybandits");
            return true;
        }

        private static bool ReloadPrefabs(global::World world)
        {
            var instances = new List<global::PrefabInstance>();
            var decorator = world.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
            decorator?.GetAllPrefabs(instances);
            foreach (var prefab in instances
                         .Select(instance => instance?.prefab)
                         .Where(prefab => prefab != null)
                         .Distinct())
            {
                if (!prefab!.LoadXMLData(prefab.location)) return false;
            }
            return true;
        }

        private static bool CollectGarbage()
        {
            GC.Collect();
            return true;
        }
    }

    internal sealed class SevenDaysRuntimeMaintenanceContext
    {
        private SevenDaysRuntimeMaintenanceContext(
            bool worldAvailable,
            string? worldId,
            string? worldVersion,
            string? mapResourceVersion,
            Func<bool> reloadBlocks,
            Func<bool> reloadItems,
            Func<bool> reloadEntityClasses,
            Func<bool> reloadPrefabs,
            Func<bool> collectGarbage)
        {
            WorldAvailable = worldAvailable;
            WorldId = worldId;
            WorldVersion = worldVersion;
            MapResourceVersion = mapResourceVersion;
            ReloadBlocks = reloadBlocks ?? throw new ArgumentNullException(nameof(reloadBlocks));
            ReloadItems = reloadItems ?? throw new ArgumentNullException(nameof(reloadItems));
            ReloadEntityClasses = reloadEntityClasses ??
                throw new ArgumentNullException(nameof(reloadEntityClasses));
            ReloadPrefabs = reloadPrefabs ?? throw new ArgumentNullException(nameof(reloadPrefabs));
            CollectGarbage = collectGarbage ?? throw new ArgumentNullException(nameof(collectGarbage));
        }

        public bool WorldAvailable { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public Func<bool> ReloadBlocks { get; }
        public Func<bool> ReloadItems { get; }
        public Func<bool> ReloadEntityClasses { get; }
        public Func<bool> ReloadPrefabs { get; }
        public Func<bool> CollectGarbage { get; }

        public static SevenDaysRuntimeMaintenanceContext Unavailable() =>
            new SevenDaysRuntimeMaintenanceContext(
                false, null, null, null,
                () => false,
                () => false,
                () => false,
                () => false,
                () => false);

        public static SevenDaysRuntimeMaintenanceContext Available(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            Func<bool> reloadBlocks,
            Func<bool> reloadItems,
            Func<bool> reloadEntityClasses,
            Func<bool> reloadPrefabs,
            Func<bool> collectGarbage) =>
            new SevenDaysRuntimeMaintenanceContext(
                true,
                worldId,
                worldVersion,
                mapResourceVersion,
                reloadBlocks,
                reloadItems,
                reloadEntityClasses,
                reloadPrefabs,
                collectGarbage);
    }
}
