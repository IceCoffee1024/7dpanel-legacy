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
    public enum SevenDaysBlockPrefabOperationOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class SevenDaysBlockPrefabOperationResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string MapResourceVersionChanged = "map_resource_version_changed";
        public const string ResourceMissing = "resource_missing";
        public const string ResourceIdentityChanged = "resource_identity_changed";
        public const string BlockShapeInvalid = "block_shape_invalid";
        public const string BlockRotationInvalid = "block_rotation_invalid";
        public const string TargetBoundsInvalid = "target_bounds_invalid";
        public const string TargetRegionUnavailable = "target_region_unavailable";
        public const string PrefabMissing = "prefab_missing";
        public const string PrefabBoundsChanged = "prefab_bounds_changed";
        public const string PrefabOverlap = "prefab_overlap";
        public const string PrefabIdentityChanged = "prefab_identity_changed";
        public const string ChangeSetCaptureFailed = "change_set_capture_failed";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string DispatchCancelled = "game_thread_dispatch_cancelled";
        public const string ResultUnknown = "result_unknown";

        private SevenDaysBlockPrefabOperationResult(
            SevenDaysBlockPrefabOperationOutcome outcome,
            string? errorCode,
            string? changeSetId)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
            ChangeSetId = changeSetId;
        }

        public SevenDaysBlockPrefabOperationOutcome Outcome { get; }
        public string? ErrorCode { get; }
        public string? ChangeSetId { get; }

        internal static SevenDaysBlockPrefabOperationResult Succeeded(string changeSetId) =>
            new SevenDaysBlockPrefabOperationResult(
                SevenDaysBlockPrefabOperationOutcome.Succeeded,
                null,
                RequireText(changeSetId, nameof(changeSetId)));

        internal static SevenDaysBlockPrefabOperationResult Rejected(string errorCode) =>
            new SevenDaysBlockPrefabOperationResult(
                SevenDaysBlockPrefabOperationOutcome.Rejected,
                RequireText(errorCode, nameof(errorCode)),
                null);

        internal static SevenDaysBlockPrefabOperationResult Failed(
            string errorCode,
            string? changeSetId = null) =>
            new SevenDaysBlockPrefabOperationResult(
                SevenDaysBlockPrefabOperationOutcome.Failed,
                RequireText(errorCode, nameof(errorCode)),
                changeSetId);

        internal static SevenDaysBlockPrefabOperationResult Unknown(string? changeSetId) =>
            new SevenDaysBlockPrefabOperationResult(
                SevenDaysBlockPrefabOperationOutcome.ResultUnknown,
                ResultUnknown,
                changeSetId);

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value;
        }
    }

    public sealed class SevenDaysBlockPrefabOperationHandler
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ChangeSetRetention = TimeSpan.FromDays(30);

        private readonly Func<
            string,
            Func<SevenDaysBlockPrefabOperationResult>,
            TimeSpan,
            CancellationToken,
            Task<SevenDaysBlockPrefabOperationResult>> dispatcher;
        private readonly Func<WorldOperationIntent, SevenDaysBlockPrefabOperationContext?> captureContext;
        private readonly IWorldChangeSetMetadataStore changeSets;
        private readonly IWorldChangeSetBlobStore blobs;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createStorageResourceId;

        public SevenDaysBlockPrefabOperationHandler(
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs)
            : this(changeSets, blobs, () => null)
        {
        }

        public SevenDaysBlockPrefabOperationHandler(
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            Func<string?> currentMapResourceVersion)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CreateNativeContextCapture(currentMapResourceVersion),
                changeSets,
                blobs,
                () => DateTimeOffset.UtcNow,
                WorldChangeSetValidation.CreateStorageResourceId)
        {
        }

        internal SevenDaysBlockPrefabOperationHandler(
            Func<
                string,
                Func<SevenDaysBlockPrefabOperationResult>,
                TimeSpan,
                CancellationToken,
                Task<SevenDaysBlockPrefabOperationResult>> dispatcher,
            Func<WorldOperationIntent, SevenDaysBlockPrefabOperationContext?> captureContext,
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            Func<DateTimeOffset> utcNow,
            Func<string> createStorageResourceId)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
            this.changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
            this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createStorageResourceId = createStorageResourceId ??
                throw new ArgumentNullException(nameof(createStorageResourceId));
        }

        public async Task<SevenDaysBlockPrefabOperationResult> HandleAsync(
            WorldOperationExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            if (execution.Intent == null) throw new ArgumentException("An intent is required.", nameof(execution));
            if (string.IsNullOrWhiteSpace(execution.OperationId))
                throw new ArgumentException("An operation identifier is required.", nameof(execution));

            var shapeError = ValidateIntentShape(execution.Intent);
            if (shapeError != null)
                return SevenDaysBlockPrefabOperationResult.Rejected(shapeError);

            var sideEffectStarted = 0;
            string? changeSetId = null;
            try
            {
                return await dispatcher(
                        "7DPanel.World.BlockPrefabOperation",
                        () => ExecuteOnGameThread(
                            execution,
                            cancellationToken,
                            ref sideEffectStarted,
                            ref changeSetId),
                        DispatchTimeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysBlockPrefabOperationResult.Failed(
                        SevenDaysBlockPrefabOperationResult.DispatchCancelled,
                        changeSetId)
                    : SevenDaysBlockPrefabOperationResult.Unknown(changeSetId);
            }
            catch
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysBlockPrefabOperationResult.Failed(
                        SevenDaysBlockPrefabOperationResult.DispatchFailed,
                        changeSetId)
                    : SevenDaysBlockPrefabOperationResult.Unknown(changeSetId);
            }
        }

        private SevenDaysBlockPrefabOperationResult ExecuteOnGameThread(
            WorldOperationExecutionRecord execution,
            CancellationToken cancellationToken,
            ref int sideEffectStarted,
            ref string? changeSetId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var intent = execution.Intent;
            var context = captureContext(intent);
            if (context == null || !context.WorldAvailable)
                return SevenDaysBlockPrefabOperationResult.Rejected(
                    SevenDaysBlockPrefabOperationResult.WorldUnavailable);

            var validationError = ValidateCurrentContext(intent, context);
            if (validationError != null)
                return SevenDaysBlockPrefabOperationResult.Rejected(validationError);

            WorldChangeSetDescriptor descriptor;
            try
            {
                var before = context.CaptureSnapshot();
                if (before == null) throw new InvalidOperationException();
                var beforeHash = Hash(before);
                var storageResourceId = createStorageResourceId();
                var receipt = blobs.Write(new WorldChangeSetBlobDraft(
                    storageResourceId,
                    beforeHash,
                    before));
                if (!string.Equals(receipt.StorageResourceId, storageResourceId, StringComparison.Ordinal) ||
                    !string.Equals(receipt.ContentHash, beforeHash, StringComparison.Ordinal) ||
                    receipt.ByteCount != before.LongLength)
                {
                    throw new InvalidOperationException();
                }

                var createdAtUtc = utcNow();
                descriptor = changeSets.Create(new WorldChangeSetDraft(
                    execution.OperationId,
                    intent.WorldId,
                    intent.WorldVersion,
                    TargetRegion(intent),
                    beforeHash,
                    beforeHash,
                    storageResourceId,
                    createdAtUtc,
                    createdAtUtc.Add(ChangeSetRetention)));
                changeSetId = descriptor.ChangeSetId;
            }
            catch
            {
                return SevenDaysBlockPrefabOperationResult.Failed(
                    SevenDaysBlockPrefabOperationResult.ChangeSetCaptureFailed);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref sideEffectStarted, 1);
            if (!context.Apply())
                return SevenDaysBlockPrefabOperationResult.Unknown(changeSetId);

            var after = context.CaptureSnapshot();
            if (after == null) throw new InvalidOperationException();
            changeSets.MarkApplied(descriptor.ChangeSetId, Hash(after));
            return SevenDaysBlockPrefabOperationResult.Succeeded(descriptor.ChangeSetId);
        }

        private static string? ValidateIntentShape(WorldOperationIntent intent)
        {
            switch (intent.Kind)
            {
                case WorldOperationKind.SetBlock:
                    if (!(intent.Target is WorldBlockOperationTarget block) ||
                        !SafeToken(block.BlockInternalName) ||
                        block.Rotation < 0 || block.Rotation > 3 ||
                        !ValidShape(block.Shape))
                    {
                        return SevenDaysBlockPrefabOperationResult.TargetInvalid;
                    }
                    return null;

                case WorldOperationKind.PlacePrefab:
                    return intent.Target is WorldPrefabOperationTarget place &&
                           SafeToken(place.PrefabResourceId) &&
                           place.PrefabInstanceId == null &&
                           ValidPrefabTarget(place)
                        ? null
                        : SevenDaysBlockPrefabOperationResult.TargetInvalid;

                case WorldOperationKind.RemovePrefab:
                    return intent.Target is WorldPrefabOperationTarget remove &&
                           SafeToken(remove.PrefabResourceId) &&
                           SafeToken(remove.PrefabInstanceId) &&
                           ValidPrefabTarget(remove)
                        ? null
                        : SevenDaysBlockPrefabOperationResult.TargetInvalid;

                default:
                    return SevenDaysBlockPrefabOperationResult.OperationKindNotSupported;
            }
        }

        private static string? ValidateCurrentContext(
            WorldOperationIntent intent,
            SevenDaysBlockPrefabOperationContext context)
        {
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysBlockPrefabOperationResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysBlockPrefabOperationResult.WorldVersionChanged;
            if (!string.Equals(
                    context.MapResourceVersion,
                    intent.MapResourceVersion,
                    StringComparison.Ordinal))
            {
                return SevenDaysBlockPrefabOperationResult.MapResourceVersionChanged;
            }

            var region = TargetRegion(intent);
            if (!WithinWorld(context, region))
                return SevenDaysBlockPrefabOperationResult.TargetBoundsInvalid;
            if (!context.TargetRegionLoaded)
                return SevenDaysBlockPrefabOperationResult.TargetRegionUnavailable;

            switch (intent.Kind)
            {
                case WorldOperationKind.SetBlock:
                    return ValidateBlock(
                        (WorldBlockOperationTarget)intent.Target,
                        context);

                case WorldOperationKind.PlacePrefab:
                    return ValidatePlacedPrefab(
                        (WorldPrefabOperationTarget)intent.Target,
                        context);

                case WorldOperationKind.RemovePrefab:
                    return ValidateRemovedPrefab(
                        (WorldPrefabOperationTarget)intent.Target,
                        context);

                default:
                    return SevenDaysBlockPrefabOperationResult.OperationKindNotSupported;
            }
        }

        private static string? ValidateBlock(
            WorldBlockOperationTarget target,
            SevenDaysBlockPrefabOperationContext context)
        {
            if (context.BlockInternalName == null)
                return SevenDaysBlockPrefabOperationResult.ResourceMissing;
            if (!string.Equals(
                    context.BlockInternalName,
                    target.BlockInternalName,
                    StringComparison.Ordinal))
            {
                return SevenDaysBlockPrefabOperationResult.ResourceIdentityChanged;
            }
            if (target.Shape != null &&
                (!TryShape(target.Shape, out var shape) || context.BlockShape != shape))
            {
                return SevenDaysBlockPrefabOperationResult.BlockShapeInvalid;
            }
            return context.BlockRotationSupported
                ? null
                : SevenDaysBlockPrefabOperationResult.BlockRotationInvalid;
        }

        private static string? ValidatePlacedPrefab(
            WorldPrefabOperationTarget target,
            SevenDaysBlockPrefabOperationContext context)
        {
            if (context.PrefabResourceId == null)
                return SevenDaysBlockPrefabOperationResult.ResourceMissing;
            if (!string.Equals(
                    context.PrefabResourceId,
                    target.PrefabResourceId,
                    StringComparison.Ordinal))
            {
                return SevenDaysBlockPrefabOperationResult.ResourceIdentityChanged;
            }
            if (!SamePrefabTarget(target, context, requireInstance: false))
                return SevenDaysBlockPrefabOperationResult.PrefabBoundsChanged;
            return context.PrefabOverlaps
                ? SevenDaysBlockPrefabOperationResult.PrefabOverlap
                : null;
        }

        private static string? ValidateRemovedPrefab(
            WorldPrefabOperationTarget target,
            SevenDaysBlockPrefabOperationContext context)
        {
            if (context.PrefabInstanceId == null)
                return SevenDaysBlockPrefabOperationResult.PrefabMissing;
            if (context.PrefabResourceId == null)
                return SevenDaysBlockPrefabOperationResult.ResourceMissing;
            return SamePrefabTarget(target, context, requireInstance: true)
                ? null
                : SevenDaysBlockPrefabOperationResult.PrefabIdentityChanged;
        }

        private static bool SamePrefabTarget(
            WorldPrefabOperationTarget target,
            SevenDaysBlockPrefabOperationContext context,
            bool requireInstance) =>
            string.Equals(context.PrefabResourceId, target.PrefabResourceId, StringComparison.Ordinal) &&
            (!requireInstance ||
             string.Equals(context.PrefabInstanceId, target.PrefabInstanceId, StringComparison.Ordinal)) &&
            context.AnchorX == target.AnchorX &&
            context.AnchorY == target.AnchorY &&
            context.AnchorZ == target.AnchorZ &&
            context.Rotation == target.Rotation &&
            context.PrefabBounds != null &&
            SameRegion(context.PrefabBounds, TargetRegion(target));

        private static bool WithinWorld(
            SevenDaysBlockPrefabOperationContext context,
            WorldRegion region) =>
            MinimumX(region) >= context.MinimumX &&
            MinimumY(region) >= context.MinimumY &&
            MinimumZ(region) >= context.MinimumZ &&
            MaximumX(region) <= context.MaximumX &&
            MaximumY(region) <= context.MaximumY &&
            MaximumZ(region) <= context.MaximumZ;

        private static bool ValidShape(string? value) =>
            value == null || TryShape(value, out _);

        private static bool TryShape(string value, out WorldBlockShape shape) =>
            Enum.TryParse(value, false, out shape) &&
            Enum.IsDefined(typeof(WorldBlockShape), shape);

        private static bool ValidPrefabTarget(WorldPrefabOperationTarget target)
        {
            if (target.Rotation < 0 || target.Rotation > 3 || !CompleteBounds(target))
                return false;
            if (target.MinimumX!.Value > target.MaximumX!.Value ||
                target.MinimumY!.Value > target.MaximumY!.Value ||
                target.MinimumZ!.Value > target.MaximumZ!.Value ||
                target.AnchorX != target.MinimumX.Value ||
                target.AnchorY != target.MinimumY.Value ||
                target.AnchorZ != target.MinimumZ.Value)
            {
                return false;
            }

            try
            {
                var volume = checked(
                    ((long)target.MaximumX.Value - target.MinimumX.Value + 1) *
                    ((long)target.MaximumY.Value - target.MinimumY.Value + 1) *
                    ((long)target.MaximumZ.Value - target.MinimumZ.Value + 1));
                return volume <= WorldRegion.MaximumOperationVolume;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool CompleteBounds(WorldPrefabOperationTarget target) =>
            target.MinimumX.HasValue && target.MinimumY.HasValue && target.MinimumZ.HasValue &&
            target.MaximumX.HasValue && target.MaximumY.HasValue && target.MaximumZ.HasValue;

        private static bool SafeToken(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value!.IndexOf('/') < 0 &&
            value.IndexOf('\\') < 0 &&
            value.IndexOf("..", StringComparison.Ordinal) < 0 &&
            value.IndexOf(".xml", StringComparison.OrdinalIgnoreCase) < 0 &&
            value.IndexOf('<') < 0 &&
            value.IndexOf('>') < 0;

        private static WorldRegion TargetRegion(WorldOperationIntent intent)
        {
            switch (intent.Kind)
            {
                case WorldOperationKind.SetBlock:
                    var block = (WorldBlockOperationTarget)intent.Target;
                    return Region(block.X, block.Y, block.Z, block.X, block.Y, block.Z);

                case WorldOperationKind.PlacePrefab:
                case WorldOperationKind.RemovePrefab:
                    return TargetRegion((WorldPrefabOperationTarget)intent.Target);

                default:
                    throw new InvalidOperationException();
            }
        }

        private static WorldRegion TargetRegion(WorldPrefabOperationTarget target) =>
            Region(
                target.MinimumX!.Value,
                target.MinimumY!.Value,
                target.MinimumZ!.Value,
                target.MaximumX!.Value,
                target.MaximumY!.Value,
                target.MaximumZ!.Value);

        private static WorldRegion Region(
            int minimumX,
            int minimumY,
            int minimumZ,
            int maximumX,
            int maximumY,
            int maximumZ) =>
            new WorldRegion(
                new WorldCoordinate(minimumX, minimumY, minimumZ),
                new WorldCoordinate(maximumX, maximumY, maximumZ));

        private static bool SameRegion(WorldRegion left, WorldRegion right) =>
            MinimumX(left) == MinimumX(right) &&
            MinimumY(left) == MinimumY(right) &&
            MinimumZ(left) == MinimumZ(right) &&
            MaximumX(left) == MaximumX(right) &&
            MaximumY(left) == MaximumY(right) &&
            MaximumZ(left) == MaximumZ(right);

        private static int MinimumX(WorldRegion region) => checked((int)region.Minimum.X);
        private static int MinimumY(WorldRegion region) => checked((int)region.Minimum.Y);
        private static int MinimumZ(WorldRegion region) => checked((int)region.Minimum.Z);
        private static int MaximumX(WorldRegion region) => checked((int)region.Maximum.X);
        private static int MaximumY(WorldRegion region) => checked((int)region.Maximum.Y);
        private static int MaximumZ(WorldRegion region) => checked((int)region.Maximum.Z);

        private static string Hash(byte[] content)
        {
            byte[] hash;
            using (var algorithm = SHA256.Create())
                hash = algorithm.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static Func<WorldOperationIntent, SevenDaysBlockPrefabOperationContext?>
            CreateNativeContextCapture(Func<string?> currentMapResourceVersion)
        {
            if (currentMapResourceVersion == null)
                throw new ArgumentNullException(nameof(currentMapResourceVersion));
            return intent => CaptureNativeContext(intent, currentMapResourceVersion);
        }

        private static SevenDaysBlockPrefabOperationContext CaptureNativeContext(
            WorldOperationIntent intent,
            Func<string?> currentMapResourceVersion)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null ||
                string.IsNullOrWhiteSpace(world.Guid) ||
                !world.GetWorldExtent(out var minimum, out var maximum))
            {
                return SevenDaysBlockPrefabOperationContext.Unavailable();
            }

            var worldId = world.Guid;
            var worldVersion = worldId + ":" +
                world.worldTime.ToString(CultureInfo.InvariantCulture);
            var mapResourceVersion = currentMapResourceVersion();

            switch (intent.Kind)
            {
                case WorldOperationKind.SetBlock:
                    return CaptureNativeBlock(
                        world,
                        (WorldBlockOperationTarget)intent.Target,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum);

                case WorldOperationKind.PlacePrefab:
                    return CaptureNativePrefab(
                        world,
                        (WorldPrefabOperationTarget)intent.Target,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum,
                        remove: false);

                case WorldOperationKind.RemovePrefab:
                    return CaptureNativePrefab(
                        world,
                        (WorldPrefabOperationTarget)intent.Target,
                        worldId,
                        worldVersion,
                        mapResourceVersion,
                        minimum,
                        maximum,
                        remove: true);

                default:
                    return SevenDaysBlockPrefabOperationContext.Unavailable();
            }
        }

        private static SevenDaysBlockPrefabOperationContext CaptureNativeBlock(
            global::World world,
            WorldBlockOperationTarget target,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum)
        {
            var position = new global::Vector3i(target.X, target.Y, target.Z);
            var block = global::Block.GetBlockByName(target.BlockInternalName, false);
            var region = Region(target.X, target.Y, target.Z, target.X, target.Y, target.Z);
            return SevenDaysBlockPrefabOperationContext.ForBlock(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum.x,
                minimum.y,
                minimum.z,
                maximum.x,
                maximum.y,
                maximum.z,
                world.GetChunkFromWorldPos(position) != null,
                block?.GetBlockName(),
                NativeShape(block),
                SupportsRotation(block, target.Rotation),
                () => CaptureRegion(world, region),
                () =>
                {
                    if (block == null) return false;
                    var value = block.ToBlockValue();
                    value.rotation = checked((byte)target.Rotation);
                    world.SetBlockRPC(position, value);
                    return true;
                });
        }

        private static SevenDaysBlockPrefabOperationContext CaptureNativePrefab(
            global::World world,
            WorldPrefabOperationTarget target,
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            global::Vector3i minimum,
            global::Vector3i maximum,
            bool remove)
        {
            var decorator = world.ChunkCache?.ChunkProvider?.GetDynamicPrefabDecorator();
            var prefabs = new List<global::PrefabInstance>();
            decorator?.GetAllPrefabs(prefabs);
            var template = prefabs.FirstOrDefault(instance =>
                instance?.prefab != null &&
                SamePrefabResource(instance.prefab, target.PrefabResourceId));
            var instance = remove
                ? FindPrefabInstance(prefabs, target.PrefabInstanceId)
                : null;

            var current = remove ? instance : template;
            var resourceId = current?.prefab == null
                ? null
                : SevenDaysWorldResourceId.Create("prefab", current.prefab.PrefabName);
            var bounds = remove
                ? InstanceBounds(instance)
                : TemplateBounds(template, target);
            var anchorX = remove ? instance?.boundingBoxPosition.x : target.AnchorX;
            var anchorY = remove ? instance?.boundingBoxPosition.y : target.AnchorY;
            var anchorZ = remove ? instance?.boundingBoxPosition.z : target.AnchorZ;
            var rotation = remove ? instance?.rotation : checked((byte)target.Rotation);
            var targetRegion = TargetRegion(target);
            var overlaps = !remove && prefabs.Any(candidate =>
                candidate != null && Overlaps(InstanceBounds(candidate), targetRegion));
            var loaded = RegionLoaded(world, targetRegion);

            return SevenDaysBlockPrefabOperationContext.ForPrefab(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimum.x,
                minimum.y,
                minimum.z,
                maximum.x,
                maximum.y,
                maximum.z,
                loaded,
                resourceId,
                instance?.id.ToString(CultureInfo.InvariantCulture),
                anchorX,
                anchorY,
                anchorZ,
                rotation,
                bounds,
                overlaps,
                () => CaptureRegion(world, targetRegion),
                () => remove
                    ? RemoveNativePrefab(world, decorator, instance)
                    : PlaceNativePrefab(world, decorator, template, target));
        }

        private static global::PrefabInstance? FindPrefabInstance(
            IEnumerable<global::PrefabInstance> prefabs,
            string? prefabInstanceId)
        {
            if (!int.TryParse(
                    prefabInstanceId,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var id))
            {
                return null;
            }
            return prefabs.FirstOrDefault(instance => instance != null && instance.id == id);
        }

        private static bool SamePrefabResource(global::Prefab prefab, string resourceId) =>
            string.Equals(
                SevenDaysWorldResourceId.Create("prefab", prefab.PrefabName),
                resourceId,
                StringComparison.Ordinal);

        private static WorldRegion? TemplateBounds(
            global::PrefabInstance? template,
            WorldPrefabOperationTarget target)
        {
            if (template?.prefab == null) return null;
            var size = template.prefab.size;
            var sizeX = (target.Rotation & 1) == 0 ? size.x : size.z;
            var sizeZ = (target.Rotation & 1) == 0 ? size.z : size.x;
            return Region(
                target.AnchorX,
                target.AnchorY,
                target.AnchorZ,
                checked(target.AnchorX + sizeX - 1),
                checked(target.AnchorY + size.y - 1),
                checked(target.AnchorZ + sizeZ - 1));
        }

        private static WorldRegion? InstanceBounds(global::PrefabInstance? instance)
        {
            if (instance == null) return null;
            var position = instance.boundingBoxPosition;
            var size = instance.boundingBoxSize;
            return Region(
                position.x,
                position.y,
                position.z,
                checked(position.x + size.x - 1),
                checked(position.y + size.y - 1),
                checked(position.z + size.z - 1));
        }

        private static bool Overlaps(WorldRegion? left, WorldRegion right) =>
            left != null &&
            MinimumX(left) <= MaximumX(right) && MaximumX(left) >= MinimumX(right) &&
            MinimumY(left) <= MaximumY(right) && MaximumY(left) >= MinimumY(right) &&
            MinimumZ(left) <= MaximumZ(right) && MaximumZ(left) >= MinimumZ(right);

        private static bool RegionLoaded(global::World world, WorldRegion region)
        {
            for (var x = MinimumX(region); ; x = checked(x + 16))
            {
                for (var z = MinimumZ(region); ; z = checked(z + 16))
                {
                    if (world.GetChunkFromWorldPos(
                            new global::Vector3i(x, MinimumY(region), z)) == null)
                    {
                        return false;
                    }
                    if (z >= MaximumZ(region)) break;
                    if (z > MaximumZ(region) - 16) z = MaximumZ(region) - 16;
                }
                if (x >= MaximumX(region)) break;
                if (x > MaximumX(region) - 16) x = MaximumX(region) - 16;
            }
            return true;
        }

        private static byte[] CaptureRegion(global::World world, WorldRegion region)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(1);
                writer.Write(MinimumX(region));
                writer.Write(MinimumY(region));
                writer.Write(MinimumZ(region));
                writer.Write(MaximumX(region));
                writer.Write(MaximumY(region));
                writer.Write(MaximumZ(region));
                for (long x = MinimumX(region); x <= MaximumX(region); x++)
                {
                    for (long z = MinimumZ(region); z <= MaximumZ(region); z++)
                    {
                        for (long y = MinimumY(region); y <= MaximumY(region); y++)
                        {
                            var position = new global::Vector3i((int)x, (int)y, (int)z);
                            var value = world.GetBlock(position);
                            writer.Write(value.rawData);
                            writer.Write(value.damage);
                            writer.Write(world.GetDensity(position));
                        }
                    }
                }
            }
            return stream.ToArray();
        }

        private static bool PlaceNativePrefab(
            global::World world,
            global::DynamicPrefabDecorator? decorator,
            global::PrefabInstance? template,
            WorldPrefabOperationTarget target)
        {
            if (decorator == null || template?.prefab == null) return false;
            var prefab = template.prefab.Clone();
            var instance = new global::PrefabInstance(
                decorator.GetNextId(),
                template.location,
                new global::Vector3i(target.AnchorX, target.AnchorY, target.AnchorZ),
                0,
                prefab,
                0);
            instance.SetRotation(checked((byte)target.Rotation));
            decorator.AddWorldPrefab(instance, false);
            instance.CopyIntoWorld(
                world,
                true,
                true,
                global::FastTags<global::TagGroup.Global>.none);
            return true;
        }

        private static bool RemoveNativePrefab(
            global::World world,
            global::DynamicPrefabDecorator? decorator,
            global::PrefabInstance? instance)
        {
            if (decorator == null || instance == null) return false;
            decorator.RemovePrefabAndSelection(world, instance, true);
            return true;
        }

        private static WorldBlockShape? NativeShape(global::Block? block)
        {
            if (block == null) return null;
            if (block.shape is global::BlockShapeCube || block.shape.IsSolidCube)
                return WorldBlockShape.Cube;
            if (block.shape is global::BlockShapeNew modern)
            {
                if (modern.ShapeName.IndexOf("wedge", StringComparison.OrdinalIgnoreCase) >= 0)
                    return WorldBlockShape.Wedge;
                if (modern.ShapeName.IndexOf("ramp", StringComparison.OrdinalIgnoreCase) >= 0)
                    return WorldBlockShape.Ramp;
            }
            return WorldBlockShape.Default;
        }

        private static bool SupportsRotation(global::Block? block, int rotation) =>
            block != null &&
            (rotation == 0 ||
             (block.AllowedRotations & global::EBlockRotationClasses.Basic90) !=
             global::EBlockRotationClasses.None);
    }

    internal sealed class SevenDaysBlockPrefabOperationContext
    {
        private SevenDaysBlockPrefabOperationContext(
            bool worldAvailable,
            string? worldId,
            string? worldVersion,
            string? mapResourceVersion,
            int minimumX,
            int minimumY,
            int minimumZ,
            int maximumX,
            int maximumY,
            int maximumZ,
            bool targetRegionLoaded,
            string? blockInternalName,
            WorldBlockShape? blockShape,
            bool blockRotationSupported,
            string? prefabResourceId,
            string? prefabInstanceId,
            int? anchorX,
            int? anchorY,
            int? anchorZ,
            int? rotation,
            WorldRegion? prefabBounds,
            bool prefabOverlaps,
            Func<byte[]> captureSnapshot,
            Func<bool> apply)
        {
            WorldAvailable = worldAvailable;
            WorldId = worldId;
            WorldVersion = worldVersion;
            MapResourceVersion = mapResourceVersion;
            MinimumX = minimumX;
            MinimumY = minimumY;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumY = maximumY;
            MaximumZ = maximumZ;
            TargetRegionLoaded = targetRegionLoaded;
            BlockInternalName = blockInternalName;
            BlockShape = blockShape;
            BlockRotationSupported = blockRotationSupported;
            PrefabResourceId = prefabResourceId;
            PrefabInstanceId = prefabInstanceId;
            AnchorX = anchorX;
            AnchorY = anchorY;
            AnchorZ = anchorZ;
            Rotation = rotation;
            PrefabBounds = prefabBounds;
            PrefabOverlaps = prefabOverlaps;
            CaptureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public bool WorldAvailable { get; }
        public string? WorldId { get; }
        public string? WorldVersion { get; }
        public string? MapResourceVersion { get; }
        public int MinimumX { get; }
        public int MinimumY { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumY { get; }
        public int MaximumZ { get; }
        public bool TargetRegionLoaded { get; }
        public string? BlockInternalName { get; }
        public WorldBlockShape? BlockShape { get; }
        public bool BlockRotationSupported { get; }
        public string? PrefabResourceId { get; }
        public string? PrefabInstanceId { get; }
        public int? AnchorX { get; }
        public int? AnchorY { get; }
        public int? AnchorZ { get; }
        public int? Rotation { get; }
        public WorldRegion? PrefabBounds { get; }
        public bool PrefabOverlaps { get; }
        public Func<byte[]> CaptureSnapshot { get; }
        public Func<bool> Apply { get; }

        public static SevenDaysBlockPrefabOperationContext Unavailable() =>
            new SevenDaysBlockPrefabOperationContext(
                false,
                null,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                false,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                () => Array.Empty<byte>(),
                () => false);

        public static SevenDaysBlockPrefabOperationContext ForBlock(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            int minimumX,
            int minimumY,
            int minimumZ,
            int maximumX,
            int maximumY,
            int maximumZ,
            bool targetRegionLoaded,
            string? blockInternalName,
            WorldBlockShape? blockShape,
            bool rotationSupported,
            Func<byte[]> captureSnapshot,
            Func<bool> apply) =>
            new SevenDaysBlockPrefabOperationContext(
                true,
                worldId,
                worldVersion,
                mapResourceVersion,
                minimumX,
                minimumY,
                minimumZ,
                maximumX,
                maximumY,
                maximumZ,
                targetRegionLoaded,
                blockInternalName,
                blockShape,
                rotationSupported,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                captureSnapshot,
                apply);

        public static SevenDaysBlockPrefabOperationContext ForPrefab(
            string worldId,
            string worldVersion,
            string? mapResourceVersion,
            int minimumX,
            int minimumY,
            int minimumZ,
            int maximumX,
            int maximumY,
            int maximumZ,
            bool targetRegionLoaded,
            string? prefabResourceId,
            string? prefabInstanceId,
            int? anchorX,
            int? anchorY,
            int? anchorZ,
            int? rotation,
            WorldRegion? bounds,
            bool overlaps,
            Func<byte[]> captureSnapshot,
            Func<bool> apply) =>
            new SevenDaysBlockPrefabOperationContext(
                true,
                worldId,
                worldVersion,
                mapResourceVersion,
                minimumX,
                minimumY,
                minimumZ,
                maximumX,
                maximumY,
                maximumZ,
                targetRegionLoaded,
                null,
                null,
                false,
                prefabResourceId,
                prefabInstanceId,
                anchorX,
                anchorY,
                anchorZ,
                rotation,
                bounds,
                overlaps,
                captureSnapshot,
                apply);
    }
}
