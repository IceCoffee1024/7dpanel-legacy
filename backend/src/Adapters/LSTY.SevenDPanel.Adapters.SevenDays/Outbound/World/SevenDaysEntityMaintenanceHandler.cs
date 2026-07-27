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
    public enum SevenDaysEntityMaintenanceOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class SevenDaysEntityMaintenanceResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string MapResourceVersionChanged = "map_resource_version_changed";
        public const string EntityTypeMissing = "entity_type_missing";
        public const string TargetMissing = "target_missing";
        public const string TargetIdentityChanged = "target_identity_changed";
        public const string TargetTypeChanged = "target_type_changed";
        public const string TargetPositionChanged = "target_position_changed";
        public const string PlayerEntityForbidden = "player_entity_forbidden";
        public const string ProtectedEntityForbidden = "protected_entity_forbidden";
        public const string CleanupCategoryChanged = "cleanup_category_changed";
        public const string CleanupLimitExceeded = "cleanup_limit_exceeded";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string DispatchCancelled = "game_thread_dispatch_cancelled";
        public const string ResultUnknown = "result_unknown";

        private SevenDaysEntityMaintenanceResult(
            SevenDaysEntityMaintenanceOutcome outcome,
            string? errorCode)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
        }

        public SevenDaysEntityMaintenanceOutcome Outcome { get; }
        public string? ErrorCode { get; }

        internal static SevenDaysEntityMaintenanceResult Succeeded() =>
            new SevenDaysEntityMaintenanceResult(
                SevenDaysEntityMaintenanceOutcome.Succeeded,
                null);

        internal static SevenDaysEntityMaintenanceResult Rejected(string errorCode) =>
            new SevenDaysEntityMaintenanceResult(
                SevenDaysEntityMaintenanceOutcome.Rejected,
                RequireErrorCode(errorCode));

        internal static SevenDaysEntityMaintenanceResult Failed(string errorCode) =>
            new SevenDaysEntityMaintenanceResult(
                SevenDaysEntityMaintenanceOutcome.Failed,
                RequireErrorCode(errorCode));

        internal static SevenDaysEntityMaintenanceResult Unknown() =>
            new SevenDaysEntityMaintenanceResult(
                SevenDaysEntityMaintenanceOutcome.ResultUnknown,
                ResultUnknown);

        private static string RequireErrorCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("An error code is required.", nameof(value));
            return value;
        }
    }

    public sealed class SevenDaysEntityMaintenanceHandler
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly Func<
            string,
            Func<SevenDaysEntityMaintenanceResult>,
            TimeSpan,
            CancellationToken,
            Task<SevenDaysEntityMaintenanceResult>> dispatcher;
        private readonly Func<WorldOperationIntent, SevenDaysEntityMaintenanceContext?> captureContext;

        public SevenDaysEntityMaintenanceHandler()
            : this(() => null)
        {
        }

        public SevenDaysEntityMaintenanceHandler(Func<string?> currentMapResourceVersion)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CreateNativeContextCapture(currentMapResourceVersion))
        {
        }

        internal SevenDaysEntityMaintenanceHandler(
            Func<
                string,
                Func<SevenDaysEntityMaintenanceResult>,
                TimeSpan,
                CancellationToken,
                Task<SevenDaysEntityMaintenanceResult>> dispatcher,
            Func<WorldOperationIntent, SevenDaysEntityMaintenanceContext?> captureContext)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
        }

        public async Task<SevenDaysEntityMaintenanceResult> HandleAsync(
            WorldOperationIntent intent,
            CancellationToken cancellationToken)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));

            var shapeError = ValidateIntentShape(intent);
            if (shapeError != null)
                return SevenDaysEntityMaintenanceResult.Rejected(shapeError);

            var sideEffectStarted = 0;
            try
            {
                return await dispatcher(
                        "7DPanel.World.EntityMaintenance",
                        () => ExecuteOnGameThread(
                            intent,
                            cancellationToken,
                            ref sideEffectStarted),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysEntityMaintenanceResult.Failed(
                        SevenDaysEntityMaintenanceResult.DispatchCancelled)
                    : SevenDaysEntityMaintenanceResult.Unknown();
            }
            catch
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysEntityMaintenanceResult.Failed(
                        SevenDaysEntityMaintenanceResult.DispatchFailed)
                    : SevenDaysEntityMaintenanceResult.Unknown();
            }
        }

        private SevenDaysEntityMaintenanceResult ExecuteOnGameThread(
            WorldOperationIntent intent,
            CancellationToken cancellationToken,
            ref int sideEffectStarted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = captureContext(intent);
            if (context == null || !context.WorldAvailable)
            {
                return SevenDaysEntityMaintenanceResult.Rejected(
                    SevenDaysEntityMaintenanceResult.WorldUnavailable);
            }

            var validationError = ValidateCurrentContext(intent, context);
            if (validationError != null)
                return SevenDaysEntityMaintenanceResult.Rejected(validationError);

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref sideEffectStarted, 1);
            return context.Apply()
                ? SevenDaysEntityMaintenanceResult.Succeeded()
                : SevenDaysEntityMaintenanceResult.Unknown();
        }

        private static string? ValidateIntentShape(WorldOperationIntent intent)
        {
            if (!(intent.Target is WorldEntityOperationTarget target))
                return SevenDaysEntityMaintenanceResult.TargetInvalid;

            switch (intent.Kind)
            {
                case WorldOperationKind.SpawnEntity:
                    return SafeToken(target.TargetId) &&
                           target.EntityId == null &&
                           target.StableIdentity == null &&
                           OpaqueResourceId(target.EntityTypeResourceId) &&
                           target.OwnerIdentity == null &&
                           target.ObservedX == null &&
                           target.ObservedY == null &&
                           target.ObservedZ == null &&
                           Coordinates(target.DestinationX, target.DestinationY, target.DestinationZ) &&
                           target.Quantity >= 1 && target.Quantity <= 50 &&
                           FiniteInRange(target.Radius, 0, 100) &&
                           target.EntityCategory == null
                        ? null
                        : SevenDaysEntityMaintenanceResult.TargetInvalid;

                case WorldOperationKind.DeleteEntity:
                    return SafeToken(target.TargetId) &&
                           target.EntityId >= 0 && target.EntityId <= int.MaxValue &&
                           string.Equals(
                               target.StableIdentity,
                               target.TargetId,
                               StringComparison.Ordinal) &&
                           OpaqueResourceId(target.EntityTypeResourceId) &&
                           SafeOptionalIdentity(target.OwnerIdentity) &&
                           Coordinates(target.ObservedX, target.ObservedY, target.ObservedZ) &&
                           target.DestinationX == null &&
                           target.DestinationY == null &&
                           target.DestinationZ == null &&
                           target.Quantity == null &&
                           target.Radius == null &&
                           target.EntityCategory == null
                        ? null
                        : SevenDaysEntityMaintenanceResult.TargetInvalid;

                case WorldOperationKind.CleanupEntities:
                    if (!TryCategory(target.EntityCategory, out var category))
                        return SevenDaysEntityMaintenanceResult.TargetInvalid;
                    return string.Equals(
                               target.TargetId,
                               "cleanup-" + category,
                               StringComparison.Ordinal) &&
                           target.EntityId == null &&
                           target.StableIdentity == null &&
                           target.EntityTypeResourceId == null &&
                           target.OwnerIdentity == null &&
                           target.ObservedX == null &&
                           target.ObservedY == null &&
                           target.ObservedZ == null &&
                           Coordinates(target.DestinationX, target.DestinationY, target.DestinationZ) &&
                           target.Quantity >= 1 && target.Quantity <= 1000 &&
                           FiniteInRange(target.Radius, 0, 1000)
                        ? null
                        : SevenDaysEntityMaintenanceResult.TargetInvalid;

                default:
                    return SevenDaysEntityMaintenanceResult.OperationKindNotSupported;
            }
        }

        private static string? ValidateCurrentContext(
            WorldOperationIntent intent,
            SevenDaysEntityMaintenanceContext context)
        {
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysEntityMaintenanceResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysEntityMaintenanceResult.WorldVersionChanged;
            if (!string.Equals(
                    context.MapResourceVersion,
                    intent.MapResourceVersion,
                    StringComparison.Ordinal))
            {
                return SevenDaysEntityMaintenanceResult.MapResourceVersionChanged;
            }

            var target = (WorldEntityOperationTarget)intent.Target;
            switch (intent.Kind)
            {
                case WorldOperationKind.SpawnEntity:
                    if (context.EntityTypeResourceId == null)
                        return SevenDaysEntityMaintenanceResult.EntityTypeMissing;
                    if (!Same(context.EntityTypeResourceId, target.EntityTypeResourceId))
                        return SevenDaysEntityMaintenanceResult.TargetTypeChanged;
                    return ForbiddenEntity(context);

                case WorldOperationKind.DeleteEntity:
                    if (!context.TargetExists)
                        return SevenDaysEntityMaintenanceResult.TargetMissing;
                    if (context.EntityId != target.EntityId ||
                        !Same(context.TargetId, target.TargetId) ||
                        !Same(context.OwnerStableIdentity, target.OwnerIdentity))
                    {
                        return SevenDaysEntityMaintenanceResult.TargetIdentityChanged;
                    }
                    if (!Same(context.EntityTypeResourceId, target.EntityTypeResourceId))
                        return SevenDaysEntityMaintenanceResult.TargetTypeChanged;
                    if (!SamePosition(context, target))
                        return SevenDaysEntityMaintenanceResult.TargetPositionChanged;
                    return ForbiddenEntity(context);

                case WorldOperationKind.CleanupEntities:
                    if (!TryCategory(target.EntityCategory, out var category) ||
                        context.EntityCategory != category)
                    {
                        return SevenDaysEntityMaintenanceResult.CleanupCategoryChanged;
                    }
                    if (context.ContainsPlayer)
                        return SevenDaysEntityMaintenanceResult.PlayerEntityForbidden;
                    if (context.ContainsProtectedEntity)
                        return SevenDaysEntityMaintenanceResult.ProtectedEntityForbidden;
                    return context.CandidateCount <= target.Quantity
                        ? null
                        : SevenDaysEntityMaintenanceResult.CleanupLimitExceeded;

                default:
                    return SevenDaysEntityMaintenanceResult.OperationKindNotSupported;
            }
        }

        private static string? ForbiddenEntity(SevenDaysEntityMaintenanceContext context)
        {
            if (context.EntityTypeIsPlayer)
                return SevenDaysEntityMaintenanceResult.PlayerEntityForbidden;
            return context.EntityTypeIsProtected
                ? SevenDaysEntityMaintenanceResult.ProtectedEntityForbidden
                : null;
        }

        private static bool SamePosition(
            SevenDaysEntityMaintenanceContext context,
            WorldEntityOperationTarget target) =>
            SameCoordinate(context.ObservedX, target.ObservedX) &&
            SameCoordinate(context.ObservedY, target.ObservedY) &&
            SameCoordinate(context.ObservedZ, target.ObservedZ);

        private static bool SameCoordinate(double? left, double? right) =>
            left.HasValue && right.HasValue && Math.Abs(left.Value - right.Value) <= 0.01d;

        private static bool Coordinates(double? x, double? y, double? z) =>
            x.HasValue && y.HasValue && z.HasValue &&
            IsFinite(x.Value) && IsFinite(y.Value) && IsFinite(z.Value);

        private static bool FiniteInRange(double? value, double minimum, double maximum) =>
            value.HasValue && IsFinite(value.Value) &&
            value.Value >= minimum && value.Value <= maximum;

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool OpaqueResourceId(string? value) =>
            value != null && value.Length == 32 && value.All(character =>
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z') ||
                (character >= '0' && character <= '9') ||
                character == '-' || character == '_');

        private static bool SafeToken(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value!.Length <= 200 &&
            value.IndexOf('/') < 0 &&
            value.IndexOf('\\') < 0 &&
            value.IndexOf('\r') < 0 &&
            value.IndexOf('\n') < 0 &&
            value.IndexOf('<') < 0 &&
            value.IndexOf('>') < 0;

        private static bool SafeOptionalIdentity(string? value) =>
            value == null || SafeToken(value);

        private static bool TryCategory(
            string? value,
            out WorldEntityCategory category) =>
            Enum.TryParse(value, ignoreCase: false, out category) &&
            Enum.IsDefined(typeof(WorldEntityCategory), category) &&
            string.Equals(value, category.ToString(), StringComparison.Ordinal);

        private static bool Same(string? left, string? right) =>
            string.Equals(left, right, StringComparison.Ordinal);

        private static Func<WorldOperationIntent, SevenDaysEntityMaintenanceContext?>
            CreateNativeContextCapture(Func<string?> currentMapResourceVersion)
        {
            if (currentMapResourceVersion == null)
                throw new ArgumentNullException(nameof(currentMapResourceVersion));
            return intent => CaptureNativeContext(intent, currentMapResourceVersion);
        }

        private static SevenDaysEntityMaintenanceContext CaptureNativeContext(
            WorldOperationIntent intent,
            Func<string?> currentMapResourceVersion)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null || string.IsNullOrWhiteSpace(world.Guid))
                return SevenDaysEntityMaintenanceContext.Unavailable();

            var worldId = world.Guid;
            var worldVersion = worldId + ":" +
                world.worldTime.ToString(CultureInfo.InvariantCulture);
            var mapResourceVersion = currentMapResourceVersion();
            var target = (WorldEntityOperationTarget)intent.Target;

            switch (intent.Kind)
            {
                case WorldOperationKind.SpawnEntity:
                    return CaptureSpawnContext(
                        world,
                        target,
                        worldId,
                        worldVersion,
                        mapResourceVersion);
                case WorldOperationKind.DeleteEntity:
                    return CaptureDeleteContext(
                        world,
                        target,
                        worldId,
                        worldVersion,
                        mapResourceVersion);
                case WorldOperationKind.CleanupEntities:
                    return CaptureCleanupContext(
                        world,
                        target,
                        worldId,
                        worldVersion,
                        mapResourceVersion);
                default:
                    return SevenDaysEntityMaintenanceContext.Unavailable();
            }
        }

        private static SevenDaysEntityMaintenanceContext CaptureSpawnContext(
            global::World world,
            WorldEntityOperationTarget target,
            string worldId,
            string worldVersion,
            string? mapResourceVersion)
        {
            var entityClass = ResolveEntityClass(target.EntityTypeResourceId);
            if (!entityClass.HasValue)
            {
                return SevenDaysEntityMaintenanceContext.ForSpawn(
                    worldId,
                    worldVersion,
                    mapResourceVersion,
                    null,
                    false,
                    false,
                    () => false);
            }

            var nativeClass = entityClass.Value.Value.classname;
            var isPlayer = nativeClass != null &&
                typeof(global::EntityPlayer).IsAssignableFrom(nativeClass);
            return SevenDaysEntityMaintenanceContext.ForSpawn(
                worldId,
                worldVersion,
                mapResourceVersion,
                target.EntityTypeResourceId,
                isPlayer,
                false,
                () => SpawnEntities(world, entityClass.Value.Key, target));
        }

        private static SevenDaysEntityMaintenanceContext CaptureDeleteContext(
            global::World world,
            WorldEntityOperationTarget target,
            string worldId,
            string worldVersion,
            string? mapResourceVersion)
        {
            if (!TryEntityId(target.EntityId, out var entityId))
            {
                return SevenDaysEntityMaintenanceContext.ForDelete(
                    worldId, worldVersion, mapResourceVersion, false,
                    null, null, null, null, null, null, null,
                    false, false, () => false);
            }

            var entity = world.GetEntity(entityId);
            if (entity == null)
            {
                return SevenDaysEntityMaintenanceContext.ForDelete(
                    worldId, worldVersion, mapResourceVersion, false,
                    null, null, null, null, null, null, null,
                    false, false, () => false);
            }

            var typeName = entity.EntityClass?.entityClassName;
            var typeResourceId = string.IsNullOrWhiteSpace(typeName)
                ? null
                : SevenDaysWorldResourceId.Create("entity-type", typeName!);
            var ownerIdentity = OwnerIdentity(entity);
            var stableIdentity = StableIdentity(entity, typeResourceId, ownerIdentity);
            var isPlayer = entity is global::EntityPlayer;
            var isProtected = IsProtected(entity);

            return SevenDaysEntityMaintenanceContext.ForDelete(
                worldId,
                worldVersion,
                mapResourceVersion,
                true,
                stableIdentity,
                entity.entityId,
                typeResourceId,
                ownerIdentity,
                entity.position.x,
                entity.position.y,
                entity.position.z,
                isPlayer,
                isProtected,
                () => RemoveFixedEntity(world, entity, target));
        }

        private static SevenDaysEntityMaintenanceContext CaptureCleanupContext(
            global::World world,
            WorldEntityOperationTarget target,
            string worldId,
            string worldVersion,
            string? mapResourceVersion)
        {
            TryCategory(target.EntityCategory, out var category);
            var centerX = target.DestinationX!.Value;
            var centerY = target.DestinationY!.Value;
            var centerZ = target.DestinationZ!.Value;
            var radius = target.Radius!.Value;
            var maximumCount = target.Quantity!.Value;
            var candidates = world.Entities.list
                .Where(entity => entity != null &&
                    MatchesCategory(entity, category) &&
                    WithinRadius(entity, centerX, centerY, centerZ, radius))
                .OrderBy(entity => entity.entityId)
                .ToArray();
            var containsPlayer = candidates.Any(entity => entity is global::EntityPlayer);
            var containsProtected = candidates.Any(IsProtected);
            var selected = candidates.Take(maximumCount).ToArray();

            return SevenDaysEntityMaintenanceContext.ForCleanup(
                worldId,
                worldVersion,
                mapResourceVersion,
                category,
                selected.Length,
                containsPlayer,
                containsProtected,
                () => CleanupEntities(
                    world,
                    selected,
                    category,
                    centerX,
                    centerY,
                    centerZ,
                    radius));
        }

        private static KeyValuePair<int, global::EntityClass>? ResolveEntityClass(
            string? resourceId)
        {
            foreach (var pair in global::EntityClass.list.Dict)
            {
                var entityClass = pair.Value;
                if (entityClass != null &&
                    !string.IsNullOrWhiteSpace(entityClass.entityClassName) &&
                    string.Equals(
                        SevenDaysWorldResourceId.Create(
                            "entity-type",
                            entityClass.entityClassName),
                        resourceId,
                        StringComparison.Ordinal))
                {
                    return pair;
                }
            }
            return null;
        }

        private static bool SpawnEntities(
            global::World world,
            int entityClassId,
            WorldEntityOperationTarget target)
        {
            var quantity = target.Quantity!.Value;
            var radius = target.Radius!.Value;
            for (var index = 0; index < quantity; index++)
            {
                var angle = quantity == 1 ? 0d : index * Math.PI * 2d / quantity;
                var distance = quantity == 1 ? 0d :
                    radius * Math.Sqrt((index + 0.5d) / quantity);
                var position = new global::UnityEngine.Vector3(
                    checked((float)(target.DestinationX!.Value + Math.Cos(angle) * distance)),
                    checked((float)target.DestinationY!.Value),
                    checked((float)(target.DestinationZ!.Value + Math.Sin(angle) * distance)));
                var entity = global::EntityFactory.CreateEntity(entityClassId, position);
                if (entity == null || entity is global::EntityPlayer)
                    return false;
                world.SpawnEntityInWorld(entity);
            }
            return true;
        }

        private static bool RemoveFixedEntity(
            global::World world,
            global::Entity expected,
            WorldEntityOperationTarget target)
        {
            if (!TryEntityId(target.EntityId, out var entityId)) return false;
            var current = world.GetEntity(entityId);
            if (!ReferenceEquals(current, expected) ||
                current is global::EntityPlayer ||
                IsProtected(current))
            {
                return false;
            }
            return world.RemoveEntity(entityId, global::EnumRemoveEntityReason.Despawned) != null;
        }

        private static bool CleanupEntities(
            global::World world,
            IEnumerable<global::Entity> expectedEntities,
            WorldEntityCategory category,
            double centerX,
            double centerY,
            double centerZ,
            double radius)
        {
            foreach (var expected in expectedEntities)
            {
                var current = world.GetEntity(expected.entityId);
                if (!ReferenceEquals(current, expected) ||
                    current is global::EntityPlayer ||
                    IsProtected(current) ||
                    !MatchesCategory(current, category) ||
                    !WithinRadius(current, centerX, centerY, centerZ, radius))
                {
                    return false;
                }
                if (world.RemoveEntity(
                        current.entityId,
                        global::EnumRemoveEntityReason.Despawned) == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool MatchesCategory(
            global::Entity entity,
            WorldEntityCategory category)
        {
            switch (category)
            {
                case WorldEntityCategory.Animal:
                    return entity is global::EntityAnimal;
                case WorldEntityCategory.Hostile:
                    return entity is global::EntityEnemy;
                case WorldEntityCategory.Vehicle:
                    return entity is global::EntityVehicle;
                case WorldEntityCategory.Drone:
                    return entity is global::EntityDrone;
                case WorldEntityCategory.DroppedItem:
                    return entity is global::EntityItem;
                default:
                    return false;
            }
        }

        private static bool WithinRadius(
            global::Entity entity,
            double centerX,
            double centerY,
            double centerZ,
            double radius)
        {
            var x = entity.position.x - centerX;
            var y = entity.position.y - centerY;
            var z = entity.position.z - centerZ;
            return x * x + y * y + z * z <= radius * radius;
        }

        private static bool IsProtected(global::Entity entity)
        {
            if (entity is global::EntityPlayer) return true;
            return !string.IsNullOrWhiteSpace(OwnerIdentity(entity));
        }

        private static string? OwnerIdentity(global::Entity entity)
        {
            if (entity is global::EntityVehicle vehicle)
                return vehicle.GetOwner()?.CombinedString;
            if (entity is global::EntityDrone drone)
                return drone.OwnerID?.CombinedString;
            return null;
        }

        private static string StableIdentity(
            global::Entity entity,
            string? typeResourceId,
            string? ownerIdentity)
        {
            var kind = entity is global::EntityVehicle
                ? "vehicle"
                : entity is global::EntityDrone
                    ? "drone"
                    : "entity";
            return kind + ":" + (typeResourceId ?? "unknown") + ":" +
                   (ownerIdentity ?? "unknown") + ":" +
                   entity.entityId.ToString(CultureInfo.InvariantCulture);
        }

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
    }

    internal sealed class SevenDaysEntityMaintenanceContext
    {
        private SevenDaysEntityMaintenanceContext(
            bool worldAvailable,
            string? worldId,
            string? worldVersion,
            string? mapResourceVersion,
            bool targetExists,
            string? targetId,
            long? entityId,
            string? entityTypeResourceId,
            string? ownerStableIdentity,
            double? observedX,
            double? observedY,
            double? observedZ,
            bool entityTypeIsPlayer,
            bool entityTypeIsProtected,
            WorldEntityCategory? entityCategory,
            int candidateCount,
            bool containsPlayer,
            bool containsProtectedEntity,
            Func<bool> apply)
        {
            WorldAvailable = worldAvailable;
            WorldId = worldId;
            WorldVersion = worldVersion;
            MapResourceVersion = mapResourceVersion;
            TargetExists = targetExists;
            TargetId = targetId;
            EntityId = entityId;
            EntityTypeResourceId = entityTypeResourceId;
            OwnerStableIdentity = ownerStableIdentity;
            ObservedX = observedX;
            ObservedY = observedY;
            ObservedZ = observedZ;
            EntityTypeIsPlayer = entityTypeIsPlayer;
            EntityTypeIsProtected = entityTypeIsProtected;
            EntityCategory = entityCategory;
            CandidateCount = candidateCount;
            ContainsPlayer = containsPlayer;
            ContainsProtectedEntity = containsProtectedEntity;
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public bool WorldAvailable { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public bool TargetExists { get; }
        public string? TargetId { get; }
        public long? EntityId { get; }
        public string? EntityTypeResourceId { get; }
        public string? OwnerStableIdentity { get; }
        public double? ObservedX { get; }
        public double? ObservedY { get; }
        public double? ObservedZ { get; }
        public bool EntityTypeIsPlayer { get; }
        public bool EntityTypeIsProtected { get; }
        public WorldEntityCategory? EntityCategory { get; }
        public int CandidateCount { get; }
        public bool ContainsPlayer { get; }
        public bool ContainsProtectedEntity { get; }
        public Func<bool> Apply { get; }

        public static SevenDaysEntityMaintenanceContext Unavailable() =>
            new SevenDaysEntityMaintenanceContext(
                false, null, null, null, false, null, null, null, null,
                null, null, null, false, false, null, 0, false, false,
                () => false);

        public static SevenDaysEntityMaintenanceContext ForSpawn(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            string? entityTypeResourceId,
            bool entityTypeIsPlayer,
            bool entityTypeIsProtected,
            Func<bool> apply) =>
            new SevenDaysEntityMaintenanceContext(
                true, worldId, worldVersion, mapResourceVersion, false,
                null, null, entityTypeResourceId, null, null, null, null,
                entityTypeIsPlayer, entityTypeIsProtected, null, 0, false, false,
                apply);

        public static SevenDaysEntityMaintenanceContext ForDelete(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            bool targetExists,
            string? targetId,
            long? entityId,
            string? entityTypeResourceId,
            string? ownerStableIdentity,
            double? observedX,
            double? observedY,
            double? observedZ,
            bool isPlayer,
            bool isProtected,
            Func<bool> apply) =>
            new SevenDaysEntityMaintenanceContext(
                true, worldId, worldVersion, mapResourceVersion, targetExists,
                targetId, entityId, entityTypeResourceId, ownerStableIdentity,
                observedX, observedY, observedZ, isPlayer, isProtected, null, 0,
                false, false, apply);

        public static SevenDaysEntityMaintenanceContext ForCleanup(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            WorldEntityCategory category,
            int candidateCount,
            bool containsPlayer,
            bool containsProtectedEntity,
            Func<bool> apply) =>
            new SevenDaysEntityMaintenanceContext(
                true, worldId, worldVersion, mapResourceVersion, false,
                null, null, null, null, null, null, null, false, false,
                category, candidateCount, containsPlayer, containsProtectedEntity,
                apply);
    }
}
