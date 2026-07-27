using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public enum SevenDaysMapWorldOperationOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class SevenDaysMapWorldOperationResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string MapResourceVersionChanged = "map_resource_version_changed";
        public const string TargetMissing = "target_missing";
        public const string TargetIdentityChanged = "target_identity_changed";
        public const string TargetTypeChanged = "target_type_changed";
        public const string TargetPositionChanged = "target_position_changed";
        public const string DestinationInvalid = "destination_invalid";
        public const string MapBoundsInvalid = "map_bounds_invalid";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string DispatchCancelled = "game_thread_dispatch_cancelled";
        public const string MapOutputMissing = "map_output_missing";
        public const string ResultUnknown = "result_unknown";

        private SevenDaysMapWorldOperationResult(
            SevenDaysMapWorldOperationOutcome outcome,
            string? errorCode,
            string? stagedMapRoot)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
            StagedMapRoot = stagedMapRoot;
        }

        public SevenDaysMapWorldOperationOutcome Outcome { get; }

        public string? ErrorCode { get; }

        public string? StagedMapRoot { get; }

        internal static SevenDaysMapWorldOperationResult Succeeded(string? stagedMapRoot) =>
            new SevenDaysMapWorldOperationResult(
                SevenDaysMapWorldOperationOutcome.Succeeded,
                null,
                stagedMapRoot);

        internal static SevenDaysMapWorldOperationResult Rejected(string errorCode) =>
            new SevenDaysMapWorldOperationResult(
                SevenDaysMapWorldOperationOutcome.Rejected,
                RequireErrorCode(errorCode),
                null);

        internal static SevenDaysMapWorldOperationResult Failed(string errorCode) =>
            new SevenDaysMapWorldOperationResult(
                SevenDaysMapWorldOperationOutcome.Failed,
                RequireErrorCode(errorCode),
                null);

        internal static SevenDaysMapWorldOperationResult Unknown() =>
            new SevenDaysMapWorldOperationResult(
                SevenDaysMapWorldOperationOutcome.ResultUnknown,
                ResultUnknown,
                null);

        private static string RequireErrorCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An error code is required.", nameof(value));
            return value;
        }
    }

    public sealed class SevenDaysMapWorldOperationHandler
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private const double PositionTolerance = 0.001d;

        private readonly Func<
            string,
            Func<SevenDaysMapWorldOperationResult>,
            TimeSpan,
            CancellationToken,
            Task<SevenDaysMapWorldOperationResult>> dispatcher;
        private readonly Func<WorldOperationIntent, SevenDaysMapWorldOperationContext?> captureContext;

        public SevenDaysMapWorldOperationHandler(string approvedTemporaryRoot)
            : this(approvedTemporaryRoot, () => null)
        {
        }

        public SevenDaysMapWorldOperationHandler(
            string approvedTemporaryRoot,
            Func<string?> currentMapResourceVersion)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CreateNativeContextCapture(approvedTemporaryRoot, currentMapResourceVersion))
        {
        }

        internal SevenDaysMapWorldOperationHandler(
            Func<
                string,
                Func<SevenDaysMapWorldOperationResult>,
                TimeSpan,
                CancellationToken,
                Task<SevenDaysMapWorldOperationResult>> dispatcher,
            Func<WorldOperationIntent, SevenDaysMapWorldOperationContext?> captureContext)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
        }

        public async Task<SevenDaysMapWorldOperationResult> HandleAsync(
            WorldOperationIntent intent,
            CancellationToken cancellationToken)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));

            var shapeError = ValidateIntentShape(intent);
            if (shapeError != null)
                return SevenDaysMapWorldOperationResult.Rejected(shapeError);

            var sideEffectStarted = 0;
            try
            {
                return await dispatcher(
                        "7DPanel.World.MapOperation",
                        () => ExecuteOnGameThread(intent, cancellationToken, ref sideEffectStarted),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysMapWorldOperationResult.Failed(
                        SevenDaysMapWorldOperationResult.DispatchCancelled)
                    : SevenDaysMapWorldOperationResult.Unknown();
            }
            catch
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysMapWorldOperationResult.Failed(
                        SevenDaysMapWorldOperationResult.DispatchFailed)
                    : SevenDaysMapWorldOperationResult.Unknown();
            }
        }

        private SevenDaysMapWorldOperationResult ExecuteOnGameThread(
            WorldOperationIntent intent,
            CancellationToken cancellationToken,
            ref int sideEffectStarted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = captureContext(intent);
            if (context == null || !context.WorldAvailable)
            {
                return SevenDaysMapWorldOperationResult.Rejected(
                    SevenDaysMapWorldOperationResult.WorldUnavailable);
            }

            var validationError = ValidateCurrentContext(intent, context);
            if (validationError != null)
                return SevenDaysMapWorldOperationResult.Rejected(validationError);

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref sideEffectStarted, 1);
            var stagedMapRoot = context.Apply();
            if (IsMapOperation(intent.Kind) && string.IsNullOrWhiteSpace(stagedMapRoot))
                return SevenDaysMapWorldOperationResult.Unknown();

            return SevenDaysMapWorldOperationResult.Succeeded(stagedMapRoot);
        }

        private static string? ValidateCurrentContext(
            WorldOperationIntent intent,
            SevenDaysMapWorldOperationContext context)
        {
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysMapWorldOperationResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysMapWorldOperationResult.WorldVersionChanged;
            if (!string.Equals(
                    context.MapResourceVersion,
                    intent.MapResourceVersion,
                    StringComparison.Ordinal))
            {
                return SevenDaysMapWorldOperationResult.MapResourceVersionChanged;
            }

            if (IsMapOperation(intent.Kind))
                return ValidateMapTarget((WorldMapOperationTarget)intent.Target, context);

            var target = (WorldEntityOperationTarget)intent.Target;
            var destinationError = intent.Kind == WorldOperationKind.DeleteLandClaim
                ? null
                : ValidateDestination(target, context);
            if (destinationError != null) return destinationError;
            if (!context.TargetExists) return SevenDaysMapWorldOperationResult.TargetMissing;

            switch (intent.Kind)
            {
                case WorldOperationKind.DeleteLandClaim:
                    if (!Same(context.TargetId, target.TargetId) ||
                        !Same(context.StableIdentity, target.StableIdentity) ||
                        !Same(context.OwnerIdentity, target.OwnerIdentity))
                    {
                        return SevenDaysMapWorldOperationResult.TargetIdentityChanged;
                    }
                    return SamePosition(context, target)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetPositionChanged;

                case WorldOperationKind.MoveOnlinePlayer:
                    return context.EntityId == target.EntityId &&
                           Same(context.TargetId, target.TargetId) &&
                           Same(context.StableIdentity, target.StableIdentity)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetIdentityChanged;

                case WorldOperationKind.MoveEntity:
                    if (context.EntityId != target.EntityId)
                        return SevenDaysMapWorldOperationResult.TargetIdentityChanged;
                    if (!Same(context.EntityTypeResourceId, target.EntityTypeResourceId))
                        return SevenDaysMapWorldOperationResult.TargetTypeChanged;
                    if (!Same(context.TargetId, target.TargetId) ||
                        !Same(context.StableIdentity, target.StableIdentity) ||
                        !Same(context.OwnerIdentity, target.OwnerIdentity))
                    {
                        return SevenDaysMapWorldOperationResult.TargetIdentityChanged;
                    }
                    return SamePosition(context, target)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetPositionChanged;

                default:
                    return SevenDaysMapWorldOperationResult.OperationKindNotSupported;
            }
        }

        private static string? ValidateDestination(
            WorldEntityOperationTarget target,
            SevenDaysMapWorldOperationContext context)
        {
            if (!target.DestinationX.HasValue ||
                !target.DestinationY.HasValue ||
                !target.DestinationZ.HasValue ||
                !IsFiniteFloat(target.DestinationX.Value) ||
                !IsFiniteFloat(target.DestinationY.Value) ||
                !IsFiniteFloat(target.DestinationZ.Value) ||
                target.DestinationX.Value < context.MinimumX ||
                target.DestinationX.Value > context.MaximumX ||
                target.DestinationZ.Value < context.MinimumZ ||
                target.DestinationZ.Value > context.MaximumZ)
            {
                return SevenDaysMapWorldOperationResult.DestinationInvalid;
            }

            return null;
        }

        private static string? ValidateMapTarget(
            WorldMapOperationTarget target,
            SevenDaysMapWorldOperationContext context)
        {
            var hasAny = target.MinimumX.HasValue || target.MinimumZ.HasValue ||
                         target.MaximumX.HasValue || target.MaximumZ.HasValue;
            if (!hasAny) return null;
            if (!target.MinimumX.HasValue || !target.MinimumZ.HasValue ||
                !target.MaximumX.HasValue || !target.MaximumZ.HasValue ||
                target.MinimumX.Value > target.MaximumX.Value ||
                target.MinimumZ.Value > target.MaximumZ.Value ||
                target.MinimumX.Value < context.MinimumX ||
                target.MinimumZ.Value < context.MinimumZ ||
                target.MaximumX.Value > context.MaximumX ||
                target.MaximumZ.Value > context.MaximumZ)
            {
                return SevenDaysMapWorldOperationResult.MapBoundsInvalid;
            }

            return null;
        }

        private static string? ValidateIntentShape(WorldOperationIntent intent)
        {
            switch (intent.Kind)
            {
                case WorldOperationKind.DeleteLandClaim:
                    return intent.Target is WorldEntityOperationTarget claim &&
                           HasText(claim.TargetId) &&
                           Same(claim.TargetId, claim.StableIdentity) &&
                           claim.EntityId == null &&
                           claim.EntityTypeResourceId == null &&
                           HasText(claim.OwnerIdentity) &&
                           HasObservedPosition(claim) &&
                           !HasDestination(claim) &&
                           HasNoExtendedEntityPayload(claim)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetInvalid;

                case WorldOperationKind.MoveOnlinePlayer:
                    return intent.Target is WorldEntityOperationTarget player &&
                           HasText(player.TargetId) &&
                           Same(player.TargetId, player.StableIdentity) &&
                           player.EntityId.HasValue && player.EntityId.Value >= 0 &&
                           player.EntityTypeResourceId == null &&
                           player.OwnerIdentity == null &&
                           !HasObservedPosition(player) &&
                           HasDestination(player) &&
                           HasNoExtendedEntityPayload(player)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetInvalid;

                case WorldOperationKind.MoveEntity:
                    return intent.Target is WorldEntityOperationTarget entity &&
                           HasText(entity.TargetId) &&
                           Same(entity.TargetId, entity.StableIdentity) &&
                           entity.EntityId.HasValue && entity.EntityId.Value >= 0 &&
                           HasText(entity.EntityTypeResourceId) &&
                           HasObservedPosition(entity) &&
                           HasDestination(entity) &&
                           HasNoExtendedEntityPayload(entity)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetInvalid;

                case WorldOperationKind.RefreshMapResources:
                case WorldOperationKind.RenderExploredMap:
                case WorldOperationKind.RenderFullMap:
                    return intent.Target is WorldMapOperationTarget map &&
                           HasCompleteOrEmptyBounds(map)
                        ? null
                        : SevenDaysMapWorldOperationResult.TargetInvalid;

                default:
                    return SevenDaysMapWorldOperationResult.OperationKindNotSupported;
            }
        }

        private static bool HasNoExtendedEntityPayload(WorldEntityOperationTarget target) =>
            target.Quantity == null && target.Radius == null && target.EntityCategory == null;

        private static bool HasCompleteOrEmptyBounds(WorldMapOperationTarget target)
        {
            var count = (target.MinimumX.HasValue ? 1 : 0) +
                        (target.MinimumZ.HasValue ? 1 : 0) +
                        (target.MaximumX.HasValue ? 1 : 0) +
                        (target.MaximumZ.HasValue ? 1 : 0);
            return count == 0 || count == 4;
        }

        private static bool HasObservedPosition(WorldEntityOperationTarget target) =>
            target.ObservedX.HasValue && target.ObservedY.HasValue && target.ObservedZ.HasValue;

        private static bool HasDestination(WorldEntityOperationTarget target) =>
            target.DestinationX.HasValue && target.DestinationY.HasValue && target.DestinationZ.HasValue;

        private static bool SamePosition(
            SevenDaysMapWorldOperationContext context,
            WorldEntityOperationTarget target) =>
            context.X.HasValue && context.Y.HasValue && context.Z.HasValue &&
            target.ObservedX.HasValue && target.ObservedY.HasValue && target.ObservedZ.HasValue &&
            NearlyEqual(context.X.Value, target.ObservedX.Value) &&
            NearlyEqual(context.Y.Value, target.ObservedY.Value) &&
            NearlyEqual(context.Z.Value, target.ObservedZ.Value);

        private static bool NearlyEqual(double left, double right) =>
            IsFinite(left) && IsFinite(right) && Math.Abs(left - right) <= PositionTolerance;

        private static bool IsMapOperation(WorldOperationKind kind) =>
            kind == WorldOperationKind.RefreshMapResources ||
            kind == WorldOperationKind.RenderExploredMap ||
            kind == WorldOperationKind.RenderFullMap;

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFiniteFloat(double value) =>
            IsFinite(value) && value >= -float.MaxValue && value <= float.MaxValue;

        private static bool Same(string? left, string? right) =>
            string.Equals(left, right, StringComparison.Ordinal);

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static Func<WorldOperationIntent, SevenDaysMapWorldOperationContext?>
            CreateNativeContextCapture(
                string approvedTemporaryRoot,
                Func<string?> currentMapResourceVersion)
        {
            if (currentMapResourceVersion == null)
                throw new ArgumentNullException(nameof(currentMapResourceVersion));
            var root = NormalizeApprovedTemporaryRoot(approvedTemporaryRoot);
            return intent => CaptureNativeContext(intent, root, currentMapResourceVersion);
        }

        private static SevenDaysMapWorldOperationContext CaptureNativeContext(
            WorldOperationIntent intent,
            string approvedTemporaryRoot,
            Func<string?> currentMapResourceVersion)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null)
                return SevenDaysMapWorldOperationContext.Unavailable();

            var worldId = world.Guid;
            if (string.IsNullOrWhiteSpace(worldId) ||
                !world.GetWorldExtent(out var minimum, out var maximum))
            {
                return SevenDaysMapWorldOperationContext.Unavailable();
            }

            var worldVersion = worldId + ":" +
                world.worldTime.ToString(CultureInfo.InvariantCulture);
            var mapResourceVersion = currentMapResourceVersion();

            switch (intent.Kind)
            {
                case WorldOperationKind.DeleteLandClaim:
                    return CaptureLandClaimContext(
                        manager,
                        intent,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum);
                case WorldOperationKind.MoveOnlinePlayer:
                    return CapturePlayerContext(
                        world,
                        intent,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum);
                case WorldOperationKind.MoveEntity:
                    return CaptureEntityContext(
                        world,
                        intent,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum);
                case WorldOperationKind.RefreshMapResources:
                case WorldOperationKind.RenderExploredMap:
                case WorldOperationKind.RenderFullMap:
                    return Context(
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        () => StageMapResources(
                            intent.Kind,
                            worldId,
                            approvedTemporaryRoot));
                default:
                    return Context(
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum,
                        false,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        () => null);
            }
        }

        private static SevenDaysMapWorldOperationContext CaptureLandClaimContext(
            global::GameManager manager,
            WorldOperationIntent intent,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum)
        {
            var target = (WorldEntityOperationTarget)intent.Target;
            if (!TryInteger(target.ObservedX, out var x) ||
                !TryInteger(target.ObservedY, out var y) ||
                !TryInteger(target.ObservedZ, out var z) ||
                manager.persistentPlayers == null)
            {
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);
            }

            var position = new global::Vector3i(x, y, z);
            var players = manager.persistentPlayers;
            if (!players.m_lpBlockMap.TryGetValue(position, out var owner) || owner == null)
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);

            var ownerIdentity = owner.PrimaryId?.CombinedString;
            var targetId = "claim:" + x.ToString(CultureInfo.InvariantCulture) + ":" +
                           y.ToString(CultureInfo.InvariantCulture) + ":" +
                           z.ToString(CultureInfo.InvariantCulture);
            return Context(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum,
                maximum,
                true,
                targetId,
                null,
                targetId,
                null,
                ownerIdentity,
                x,
                y,
                z,
                () =>
                {
                    players.RemoveLandProtectionBlock(position);
                    return null;
                });
        }

        private static SevenDaysMapWorldOperationContext CapturePlayerContext(
            global::World world,
            WorldOperationIntent intent,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum)
        {
            var target = (WorldEntityOperationTarget)intent.Target;
            if (!TryEntityId(target.EntityId, out var entityId))
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);

            var client = global::ConnectionManager.Instance?.Clients?.ForEntityId(entityId);
            var player = world.GetEntity(entityId) as global::EntityPlayer;
            var identity = client?.CrossplatformId?.CombinedString;
            if (client == null || player == null || string.IsNullOrWhiteSpace(identity))
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);

            return Context(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum,
                maximum,
                true,
                identity,
                entityId,
                identity,
                null,
                null,
                null,
                null,
                null,
                () =>
                {
                    var destination = Destination(target);
                    client.SendPackage(
                        global::NetPackageManager.GetPackage<global::NetPackageTeleportPlayer>()
                            .Setup(destination, null, false));
                    return null;
                });
        }

        private static SevenDaysMapWorldOperationContext CaptureEntityContext(
            global::World world,
            WorldOperationIntent intent,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum)
        {
            var target = (WorldEntityOperationTarget)intent.Target;
            if (!TryEntityId(target.EntityId, out var entityId))
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);

            var entity = world.GetEntity(entityId);
            if (entity == null)
                return MissingTarget(worldId, worldVersion, mapResourceVersion, minimum, maximum);

            var typeName = entity.EntityClass?.entityClassName;
            var typeResourceId = string.IsNullOrWhiteSpace(typeName)
                ? null
                : SevenDaysWorldResourceId.Create("entity-type", typeName!);
            string? ownerIdentity = null;
            string kind;
            if (entity is global::EntityVehicle vehicle)
            {
                kind = "vehicle";
                ownerIdentity = vehicle.GetOwner()?.CombinedString;
            }
            else if (entity is global::EntityDrone drone)
            {
                kind = "drone";
                ownerIdentity = drone.OwnerID?.CombinedString;
            }
            else
            {
                kind = "entity";
            }

            var serverId = entity.entityId.ToString(CultureInfo.InvariantCulture);
            var stableIdentity = kind + ":" + (typeResourceId ?? "unknown") + ":" +
                                 (ownerIdentity ?? "unknown") + ":" + serverId;
            return Context(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum,
                maximum,
                true,
                stableIdentity,
                entity.entityId,
                stableIdentity,
                typeResourceId,
                ownerIdentity,
                entity.position.x,
                entity.position.y,
                entity.position.z,
                () =>
                {
                    entity.SetPosition(Destination(target), true);
                    return null;
                });
        }

        private static SevenDaysMapWorldOperationContext MissingTarget(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum) =>
            Context(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum,
                maximum,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                () => null);

        private static SevenDaysMapWorldOperationContext Context(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum,
            bool targetExists,
            string? targetId,
            long? entityId,
            string? stableIdentity,
            string? entityTypeResourceId,
            string? ownerIdentity,
            double? x,
            double? y,
            double? z,
            Func<string?> apply) =>
            new SevenDaysMapWorldOperationContext(
                true,
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum.x,
                minimum.z,
                maximum.x,
                maximum.z,
                targetExists,
                targetId,
                entityId,
                stableIdentity,
                entityTypeResourceId,
                ownerIdentity,
                x,
                y,
                z,
                apply);

        private static global::UnityEngine.Vector3 Destination(WorldEntityOperationTarget target) =>
            new global::UnityEngine.Vector3(
                (float)target.DestinationX!.Value,
                (float)target.DestinationY!.Value,
                (float)target.DestinationZ!.Value);

        private static bool TryEntityId(long? value, out int entityId)
        {
            if (value.HasValue && value.Value >= 0 && value.Value <= int.MaxValue)
            {
                entityId = (int)value.Value;
                return true;
            }
            entityId = 0;
            return false;
        }

        private static bool TryInteger(double? value, out int result)
        {
            if (value.HasValue &&
                IsFinite(value.Value) &&
                value.Value >= int.MinValue &&
                value.Value <= int.MaxValue &&
                value.Value == Math.Truncate(value.Value))
            {
                result = (int)value.Value;
                return true;
            }
            result = 0;
            return false;
        }

        private static string NormalizeApprovedTemporaryRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new ArgumentException("map_temporary_root_must_be_absolute", nameof(value));
            var root = NormalizeRoot(value);
            Directory.CreateDirectory(root);
            RejectReparsePoints(root, root);
            return root;
        }

        private static string StageMapResources(
            WorldOperationKind kind,
            string worldId,
            string approvedTemporaryRoot)
        {
            RejectReparsePoints(approvedTemporaryRoot, approvedTemporaryRoot);
            var renderer = global::MapRendering.MapRenderer.Instance;
            switch (kind)
            {
                case WorldOperationKind.RefreshMapResources:
                case WorldOperationKind.RenderExploredMap:
                    renderer.RenderDirtyChunks();
                    break;
                case WorldOperationKind.RenderFullMap:
                    renderer.RenderFullMap();
                    break;
                default:
                    throw new InvalidOperationException("Unsupported map operation kind.");
            }

            var sourceRoot = NormalizeRoot(global::MapRendering.Constants.MapDirectory);
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException("The native map output is unavailable.");
            RejectReparsePoints(sourceRoot, sourceRoot);

            var stageRoot = Path.Combine(
                approvedTemporaryRoot,
                "map-stage-" + Guid.NewGuid().ToString("N"));
            EnsureContained(approvedTemporaryRoot, stageRoot);
            Directory.CreateDirectory(stageRoot);
            RejectReparsePoints(approvedTemporaryRoot, stageRoot);

            var entries = CopyNativeTiles(sourceRoot, stageRoot);
            WriteManifest(
                stageRoot,
                worldId,
                global::MapRendering.Constants.MapBlockSize,
                entries);
            return stageRoot;
        }

        private static IReadOnlyList<StagedTile> CopyNativeTiles(
            string sourceRoot,
            string stageRoot)
        {
            var entries = new List<StagedTile>();
            foreach (var sourcePath in Directory.EnumerateFiles(
                         sourceRoot,
                         "*.png",
                         SearchOption.AllDirectories))
            {
                RejectReparsePoints(sourceRoot, sourcePath);
                var relative = sourcePath.Substring(sourceRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '/');
                var segments = relative.Split('/');
                if (segments.Length != 3 ||
                    !int.TryParse(segments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var zoom) ||
                    !int.TryParse(segments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                    !int.TryParse(
                        Path.GetFileNameWithoutExtension(segments[2]),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var y) ||
                    zoom < 0 ||
                    !string.Equals(Path.GetExtension(segments[2]), ".png", StringComparison.Ordinal))
                {
                    continue;
                }

                var destination = Path.Combine(
                    stageRoot,
                    zoom.ToString(CultureInfo.InvariantCulture),
                    x.ToString(CultureInfo.InvariantCulture),
                    y.ToString(CultureInfo.InvariantCulture) + ".png");
                EnsureContained(stageRoot, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var content = File.ReadAllBytes(sourcePath);
                using (var stream = new FileStream(
                           destination,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(flushToDisk: true);
                }
                entries.Add(new StagedTile(
                    zoom,
                    x,
                    y,
                    zoom.ToString(CultureInfo.InvariantCulture) + "/" +
                    x.ToString(CultureInfo.InvariantCulture) + "/" +
                    y.ToString(CultureInfo.InvariantCulture) + ".png",
                    content.LongLength,
                    Hash(content)));
            }

            return entries.OrderBy(value => value.RelativePath, StringComparer.Ordinal).ToArray();
        }

        private static void WriteManifest(
            string stageRoot,
            string worldId,
            int tileSize,
            IReadOnlyList<StagedTile> tiles)
        {
            if (tileSize <= 0) throw new InvalidOperationException("Native map tile size is invalid.");
            var builder = new StringBuilder();
            builder.Append("{\"schemaVersion\":1,\"worldId\":\"")
                .Append(JsonEscape(worldId))
                .Append("\",\"tileSize\":")
                .Append(tileSize.ToString(CultureInfo.InvariantCulture))
                .Append(",\"tiles\":[");
            for (var index = 0; index < tiles.Count; index++)
            {
                if (index > 0) builder.Append(',');
                var tile = tiles[index];
                builder.Append("{\"zoom\":")
                    .Append(tile.Zoom.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"x\":")
                    .Append(tile.X.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"y\":")
                    .Append(tile.Y.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"relativePath\":\"")
                    .Append(tile.RelativePath)
                    .Append("\",\"sizeBytes\":")
                    .Append(tile.SizeBytes.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"sha256\":\"")
                    .Append(tile.Sha256)
                    .Append("\"}");
            }
            builder.Append("]}");

            var manifestPath = Path.Combine(stageRoot, "manifest.json");
            using var stream = new FileStream(
                manifestPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            var content = new UTF8Encoding(false).GetBytes(builder.ToString());
            stream.Write(content, 0, content.Length);
            stream.Flush(flushToDisk: true);
        }

        private static string JsonEscape(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u")
                                .Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
            return builder.ToString();
        }

        private static string Hash(byte[] content)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(content)
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static void EnsureContained(string root, string path)
        {
            var fullRoot = NormalizeRoot(root);
            var fullPath = Path.GetFullPath(path);
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var prefix = fullRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(prefix, comparison))
                throw new InvalidOperationException("map_path_outside_approved_root");
        }

        private static void RejectReparsePoints(string root, string path)
        {
            var normalizedRoot = NormalizeRoot(root);
            var normalizedPath = Path.GetFullPath(path);
            CheckReparsePoint(normalizedRoot);
            if (string.Equals(normalizedRoot, normalizedPath, PathComparison)) return;
            EnsureContained(normalizedRoot, normalizedPath);
            var relative = normalizedPath.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = normalizedRoot;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                CheckReparsePoint(current);
            }
        }

        private static void CheckReparsePoint(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("map_reparse_not_allowed");
            }
        }

        private static string NormalizeRoot(string value)
        {
            var fullPath = Path.GetFullPath(value);
            var volumeRoot = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, volumeRoot, PathComparison)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private sealed class StagedTile
        {
            public StagedTile(
                int zoom,
                int x,
                int y,
                string relativePath,
                long sizeBytes,
                string sha256)
            {
                Zoom = zoom;
                X = x;
                Y = y;
                RelativePath = relativePath;
                SizeBytes = sizeBytes;
                Sha256 = sha256;
            }

            public int Zoom { get; }
            public int X { get; }
            public int Y { get; }
            public string RelativePath { get; }
            public long SizeBytes { get; }
            public string Sha256 { get; }
        }
    }

    internal sealed class SevenDaysMapWorldOperationContext
    {
        public SevenDaysMapWorldOperationContext(
            bool worldAvailable,
            string? worldId,
            string? worldVersion,
            string? mapResourceVersion,
            double minimumX,
            double minimumZ,
            double maximumX,
            double maximumZ,
            bool targetExists,
            string? targetId,
            long? entityId,
            string? stableIdentity,
            string? entityTypeResourceId,
            string? ownerIdentity,
            double? x,
            double? y,
            double? z,
            Func<string?> apply)
        {
            WorldAvailable = worldAvailable;
            WorldId = worldId;
            WorldVersion = worldVersion;
            MapResourceVersion = mapResourceVersion;
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
            TargetExists = targetExists;
            TargetId = targetId;
            EntityId = entityId;
            StableIdentity = stableIdentity;
            EntityTypeResourceId = entityTypeResourceId;
            OwnerIdentity = ownerIdentity;
            X = x;
            Y = y;
            Z = z;
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public bool WorldAvailable { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public double MinimumX { get; }
        public double MinimumZ { get; }
        public double MaximumX { get; }
        public double MaximumZ { get; }
        public bool TargetExists { get; }
        public string? TargetId { get; }
        public long? EntityId { get; }
        public string? StableIdentity { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerIdentity { get; }
        public double? X { get; }
        public double? Y { get; }
        public double? Z { get; }
        public Func<string?> Apply { get; }

        public static SevenDaysMapWorldOperationContext Unavailable() =>
            new SevenDaysMapWorldOperationContext(
                false,
                null,
                null,
                null,
                0,
                0,
                0,
                0,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                () => null);
    }
}
