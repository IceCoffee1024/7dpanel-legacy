using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public sealed class SevenDaysWorldSnapshotProjection : IWorldSnapshotProjection
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private readonly object sync = new object();
        private readonly Func<
            string,
            Func<SevenDaysWorldScalarSnapshot>,
            TimeSpan,
            CancellationToken,
            Task<SevenDaysWorldScalarSnapshot>> dispatch;
        private readonly Func<SevenDaysWorldScalarSnapshot> capture;
        private WorldSnapshot snapshot = WorldSnapshot.Unavailable();

        public SevenDaysWorldSnapshotProjection()
            : this(
                (operationName, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(operationName, action, timeout, cancellationToken),
                CaptureOnGameThread)
        {
        }

        internal SevenDaysWorldSnapshotProjection(
            Func<
                string,
                Func<SevenDaysWorldScalarSnapshot>,
                TimeSpan,
                CancellationToken,
                Task<SevenDaysWorldScalarSnapshot>> dispatch,
            Func<SevenDaysWorldScalarSnapshot> capture)
        {
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        }

        public WorldSnapshot Query()
        {
            lock (sync) return snapshot;
        }

        internal Task<SevenDaysWorldScalarSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            dispatch("7DPanel.World.ReadSnapshot", capture, DispatchTimeout, cancellationToken);

        internal void Publish(SevenDaysWorldScalarSnapshot sample, DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));
            lock (sync)
            {
                if (!sample.WorldAvailable)
                {
                    snapshot = WorldSnapshot.Unavailable();
                    return;
                }

                var world = sample.WorldCaptureFailed || sample.World == null
                    ? WorldSummary.Unavailable()
                    : ToSummary(sample.World, observedAtUtc);
                snapshot = new WorldSnapshot(
                    world,
                    Collection(sample.LandClaimsCaptureFailed, observedAtUtc, sample.LandClaims),
                    Collection(sample.VehiclesCaptureFailed, observedAtUtc, sample.Vehicles),
                    Collection(sample.DronesCaptureFailed, observedAtUtc, sample.Drones),
                    Collection(sample.ContainersCaptureFailed, observedAtUtc, sample.Containers));
            }
        }

        internal void MarkCaptureFailed()
        {
            lock (sync)
            {
                var current = snapshot;
                var world = current.World.SourceState == AvailabilityState.Unavailable ||
                            current.World.WorldId == null ||
                            current.World.WorldVersion == null ||
                            !current.World.ObservedAtUtc.HasValue
                    ? WorldSummary.Unavailable()
                    : new WorldSummary(
                        AvailabilityState.Stale,
                        current.World.WorldId,
                        current.World.WorldVersion,
                        current.World.Seed,
                        current.World.Width,
                        current.World.Height,
                        current.World.GameVersion,
                        current.World.MapResourceVersion,
                        current.World.AvailableExtent,
                        current.World.ObservedAtUtc);
                snapshot = new WorldSnapshot(
                    world,
                    Stale(current.LandClaims),
                    Stale(current.Vehicles),
                    Stale(current.Drones),
                    Stale(current.Containers));
            }
        }

        internal void Clear()
        {
            lock (sync) snapshot = WorldSnapshot.Unavailable();
        }

        internal static SevenDaysWorldScalarSnapshot CaptureOnGameThread()
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null)
            {
                return new SevenDaysWorldScalarSnapshot(
                    new SevenDaysMapSample(null, null, worldAvailable: false),
                    null,
                    Array.Empty<LandClaimSummary>(),
                    Array.Empty<VehicleSummary>(),
                    Array.Empty<DroneSummary>(),
                    Array.Empty<ContainerSummary>(),
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    Array.Empty<string>());
            }

            var mapSample = CaptureMapSample(world);
            SevenDaysWorldScalar? worldScalar = null;
            var worldCaptureFailed = false;
            try { worldScalar = CaptureWorldScalar(world); }
            catch { worldCaptureFailed = true; }

            IReadOnlyList<LandClaimSummary> landClaims = Array.Empty<LandClaimSummary>();
            var landClaimsCaptureFailed = false;
            try { landClaims = CaptureLandClaims(manager, world); }
            catch { landClaimsCaptureFailed = true; }

            IReadOnlyList<VehicleSummary> vehicles = Array.Empty<VehicleSummary>();
            var vehiclesCaptureFailed = false;
            try { vehicles = CaptureVehicles(world); }
            catch { vehiclesCaptureFailed = true; }

            IReadOnlyList<DroneSummary> drones = Array.Empty<DroneSummary>();
            var dronesCaptureFailed = false;
            try { drones = CaptureDrones(world); }
            catch { dronesCaptureFailed = true; }

            IReadOnlyList<ContainerSummary> containers = Array.Empty<ContainerSummary>();
            var containersCaptureFailed = false;
            try { containers = CaptureContainers(world, vehicles, drones); }
            catch { containersCaptureFailed = true; }

            IReadOnlyList<string> blocks = Array.Empty<string>();
            IReadOnlyList<string> prefabs = Array.Empty<string>();
            IReadOnlyList<string> entityTypes = Array.Empty<string>();
            var toolCatalogCaptureFailed = false;
            try { CaptureToolCatalog(world, out blocks, out prefabs, out entityTypes); }
            catch { toolCatalogCaptureFailed = true; }

            return new SevenDaysWorldScalarSnapshot(
                mapSample,
                worldScalar,
                landClaims,
                vehicles,
                drones,
                containers,
                blocks,
                prefabs,
                entityTypes,
                worldCaptureFailed,
                landClaimsCaptureFailed,
                vehiclesCaptureFailed,
                dronesCaptureFailed,
                containersCaptureFailed,
                toolCatalogCaptureFailed);
        }

        private static SevenDaysMapSample CaptureMapSample(global::World world)
        {
            SevenDaysMapMetadataSample? metadata = null;
            SevenDaysMapGameTimeSample? gameTime = null;
            var metadataCaptureFailed = false;
            var gameTimeCaptureFailed = false;
            try
            {
                if (world.GetWorldExtent(out var minimum, out var maximum))
                {
                    var worldName = global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);
                    var worldId = world.Guid;
                    var tileSize = global::MapRendering.Constants.MapBlockSize;
                    var zoomLevelCount = global::MapRendering.Constants.Zoomlevels;
                    if (!string.IsNullOrWhiteSpace(worldName) &&
                        !string.IsNullOrWhiteSpace(worldId) &&
                        maximum.x > minimum.x &&
                        maximum.z > minimum.z &&
                        tileSize > 0 &&
                        zoomLevelCount > 0)
                    {
                        metadata = new SevenDaysMapMetadataSample(
                            worldName,
                            worldId,
                            minimum.x,
                            minimum.z,
                            maximum.x,
                            maximum.z,
                            tileSize,
                            zoomLevelCount);
                    }
                }
            }
            catch { metadataCaptureFailed = true; }

            try
            {
                var worldTime = world.worldTime;
                gameTime = new SevenDaysMapGameTimeSample(
                    global::GameUtils.WorldTimeToDays(worldTime),
                    global::GameUtils.WorldTimeToHours(worldTime),
                    global::GameUtils.WorldTimeToMinutes(worldTime));
            }
            catch { gameTimeCaptureFailed = true; }

            return new SevenDaysMapSample(
                metadata,
                gameTime,
                metadataCaptureFailed,
                gameTimeCaptureFailed);
        }

        private static SevenDaysWorldScalar CaptureWorldScalar(global::World world)
        {
            if (!world.GetWorldExtent(out var minimum, out var maximum))
                throw new InvalidOperationException("World extent is unavailable.");
            var worldId = world.Guid;
            if (string.IsNullOrWhiteSpace(worldId))
                throw new InvalidOperationException("World identity is unavailable.");
            var seed = Normalize(global::GamePrefs.GetString(global::EnumGamePrefs.WorldGenSeed));
            var gameVersion = Normalize(global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion));
            var width = maximum.x - minimum.x;
            var height = maximum.z - minimum.z;
            return new SevenDaysWorldScalar(
                worldId,
                worldId + ":" + world.worldTime.ToString(CultureInfo.InvariantCulture),
                seed,
                width > 0 ? width : (int?)null,
                height > 0 ? height : (int?)null,
                gameVersion,
                null,
                new MapExtent(minimum.x, minimum.z, maximum.x, maximum.z));
        }

        private static IReadOnlyList<LandClaimSummary> CaptureLandClaims(
            global::GameManager manager,
            global::World world)
        {
            var persistentPlayers = manager.persistentPlayers ??
                                    throw new InvalidOperationException("Persistent players are unavailable.");
            var claims = persistentPlayers.m_lpBlockMap ??
                         throw new InvalidOperationException("Land claims are unavailable.");
            var radius = global::GameStats.GetInt(global::EnumGameStats.LandClaimSize) / 2d;
            var result = new List<LandClaimSummary>(claims.Count);
            foreach (var pair in claims)
            {
                var owner = pair.Value;
                var ownerId = owner?.PrimaryId?.CombinedString;
                if (owner == null || string.IsNullOrWhiteSpace(ownerId))
                    continue;
                var position = pair.Key;
                var serverId = PositionId("claim", position.x, position.y, position.z);
                result.Add(new LandClaimSummary(
                    serverId,
                    "claim:" + ownerId + ":" + position.x.ToString(CultureInfo.InvariantCulture) + ":" +
                    position.y.ToString(CultureInfo.InvariantCulture) + ":" +
                    position.z.ToString(CultureInfo.InvariantCulture),
                    new MapLayerPosition(position.x, position.y, position.z),
                    ownerId,
                    radius,
                    world.IsLandProtectionValidForPlayer(owner),
                    new DateTimeOffset(owner.LastLogin).ToUniversalTime()));
            }
            return result;
        }

        private static IReadOnlyList<VehicleSummary> CaptureVehicles(global::World world)
        {
            var entities = world.Entities?.list ??
                           throw new InvalidOperationException("World entities are unavailable.");
            var result = new List<VehicleSummary>();
            for (var index = 0; index < entities.Count; index++)
            {
                if (!(entities[index] is global::EntityVehicle vehicle)) continue;
                var serverId = vehicle.entityId.ToString(CultureInfo.InvariantCulture);
                var ownerId = vehicle.GetOwner()?.CombinedString;
                var typeName = vehicle.EntityClass?.entityClassName;
                var typeResourceId = string.IsNullOrWhiteSpace(typeName)
                    ? null
                    : SevenDaysWorldResourceId.Create("entity-type", typeName!);
                var stableIdentity = EntityStableIdentity("vehicle", serverId, typeResourceId, ownerId);
                var position = Position(vehicle.position.x, vehicle.position.y, vehicle.position.z);
                var nativeVehicle = vehicle.GetVehicle();
                var container = CaptureEntityContainer(
                    serverId + ":storage",
                    stableIdentity + ":storage",
                    stableIdentity,
                    position,
                    vehicle.IsLocked(),
                    vehicle.bag);
                result.Add(new VehicleSummary(
                    serverId,
                    stableIdentity,
                    typeResourceId,
                    ownerId,
                    position,
                    MapEntityLoadState.Loaded,
                    vehicle.IsLocked(),
                    nativeVehicle == null ? null : (double?)(nativeVehicle.GetFuelPercent() * 100f),
                    nativeVehicle?.GetVehicleQuality(),
                    container));
            }
            return result;
        }

        private static IReadOnlyList<DroneSummary> CaptureDrones(global::World world)
        {
            var entities = world.Entities?.list ??
                           throw new InvalidOperationException("World entities are unavailable.");
            var result = new List<DroneSummary>();
            for (var index = 0; index < entities.Count; index++)
            {
                if (!(entities[index] is global::EntityDrone drone)) continue;
                var serverId = drone.entityId.ToString(CultureInfo.InvariantCulture);
                var ownerId = drone.OwnerID?.CombinedString;
                var typeName = drone.EntityClass?.entityClassName;
                var typeResourceId = string.IsNullOrWhiteSpace(typeName)
                    ? null
                    : SevenDaysWorldResourceId.Create("entity-type", typeName!);
                var stableIdentity = EntityStableIdentity("drone", serverId, typeResourceId, ownerId);
                var position = Position(drone.position.x, drone.position.y, drone.position.z);
                var container = CaptureEntityContainer(
                    serverId + ":storage",
                    stableIdentity + ":storage",
                    stableIdentity,
                    position,
                    drone.IsLocked(),
                    drone.bag);
                result.Add(new DroneSummary(
                    serverId,
                    stableIdentity,
                    typeResourceId,
                    ownerId,
                    position,
                    MapEntityLoadState.Loaded,
                    drone.IsLocked(),
                    (int)drone.GetUpdatedItemValue().Quality,
                    container));
            }
            return result;
        }

        private static IReadOnlyList<ContainerSummary> CaptureContainers(
            global::World world,
            IReadOnlyList<VehicleSummary> vehicles,
            IReadOnlyList<DroneSummary> drones)
        {
            var result = new List<ContainerSummary>();
            result.AddRange(vehicles.Where(value => value.Container != null).Select(value => value.Container!));
            result.AddRange(drones.Where(value => value.Container != null).Select(value => value.Container!));

            var chunks = world.ChunkCache?.GetChunkArrayCopySync() ??
                         throw new InvalidOperationException("Loaded chunks are unavailable.");
            foreach (var chunk in chunks)
            {
                var tileEntities = chunk?.GetTileEntities()?.list;
                if (tileEntities == null) continue;
                for (var index = 0; index < tileEntities.Count; index++)
                {
                    var tileEntity = tileEntities[index];
                    if (tileEntity == null ||
                        !tileEntity.TryGetSelfOrFeature<global::ITileEntityLootable>(out var lootable))
                    {
                        continue;
                    }
                    var worldPosition = tileEntity.ToWorldPos();
                    var serverId = PositionId("container", worldPosition.x, worldPosition.y, worldPosition.z);
                    var stableIdentity = "tile:" + world.Guid + ":" + serverId;
                    bool? isLocked = null;
                    if (tileEntity.TryGetSelfOrFeature<global::ILockable>(out var lockable))
                        isLocked = lockable.IsLocked();
                    var items = lootable.items;
                    result.Add(new ContainerSummary(
                        serverId,
                        stableIdentity,
                        stableIdentity,
                        new MapLayerPosition(worldPosition.x, worldPosition.y, worldPosition.z),
                        MapEntityLoadState.Loaded,
                        isLocked,
                        items?.Length,
                        items == null ? null : (int?)items.Count(item => item != null && !item.IsEmpty()),
                        null));
                }
            }
            return result;
        }

        private static ContainerSummary? CaptureEntityContainer(
            string serverId,
            string stableIdentity,
            string parentStableIdentity,
            MapLayerPosition position,
            bool isLocked,
            global::Bag? bag)
        {
            if (bag == null) return null;
            var slots = bag.GetSlots();
            return new ContainerSummary(
                serverId,
                stableIdentity,
                parentStableIdentity,
                position,
                MapEntityLoadState.Loaded,
                isLocked,
                slots?.Length,
                slots == null ? null : (int?)slots.Count(item => item != null && !item.IsEmpty()),
                null);
        }

        private static void CaptureToolCatalog(
            global::World world,
            out IReadOnlyList<string> blocks,
            out IReadOnlyList<string> prefabs,
            out IReadOnlyList<string> entityTypes)
        {
            blocks = (global::Block.list ?? Array.Empty<global::Block>())
                .Where(block => block != null)
                .Select(block => block.GetBlockName())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var prefabInstances = new List<global::PrefabInstance>();
            var decorator = world.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
            decorator?.GetAllPrefabs(prefabInstances);
            prefabs = prefabInstances
                .Select(instance => instance?.prefab?.PrefabName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => SevenDaysWorldResourceId.Create("prefab", value!))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            entityTypes = global::EntityClass.list.Dict.Values
                .Where(value => value != null && !string.IsNullOrWhiteSpace(value.entityClassName))
                .Select(value => SevenDaysWorldResourceId.Create("entity-type", value.entityClassName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static WorldSummary ToSummary(SevenDaysWorldScalar value, DateTimeOffset observedAtUtc) =>
            new WorldSummary(
                AvailabilityState.Available,
                value.WorldId,
                value.WorldVersion,
                value.Seed,
                value.Width,
                value.Height,
                value.GameVersion,
                value.MapResourceVersion,
                value.AvailableExtent,
                observedAtUtc);

        private static WorldCollectionSnapshot<T> Collection<T>(
            bool captureFailed,
            DateTimeOffset observedAtUtc,
            IEnumerable<T> items)
            where T : class =>
            captureFailed
                ? WorldCollectionSnapshot<T>.Unavailable()
                : WorldCollectionSnapshot<T>.Available(observedAtUtc, items);

        private static WorldCollectionSnapshot<T> Stale<T>(WorldCollectionSnapshot<T> previous)
            where T : class =>
            previous.SourceState == AvailabilityState.Unavailable || !previous.ObservedAtUtc.HasValue
                ? WorldCollectionSnapshot<T>.Unavailable()
                : WorldCollectionSnapshot<T>.Stale(previous.ObservedAtUtc.Value, previous.Items);

        private static MapLayerPosition Position(float x, float y, float z) =>
            new MapLayerPosition(x, y, z);

        private static string PositionId(string kind, int x, int y, int z) =>
            kind + ":" + x.ToString(CultureInfo.InvariantCulture) + ":" +
            y.ToString(CultureInfo.InvariantCulture) + ":" +
            z.ToString(CultureInfo.InvariantCulture);

        private static string EntityStableIdentity(
            string kind,
            string serverId,
            string? typeResourceId,
            string? ownerId) =>
            kind + ":" + (typeResourceId ?? "unknown") + ":" + (ownerId ?? "unknown") + ":" + serverId;

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
