using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public enum SevenDaysRegionOperationOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class SevenDaysRegionOperationResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string MapResourceVersionChanged = "map_resource_version_changed";
        public const string TargetBoundsInvalid = "target_bounds_invalid";
        public const string TargetRegionUnavailable = "target_region_unavailable";
        public const string BlockResourceInvalid = "block_resource_invalid";
        public const string SourceChangeSetInvalid = "source_change_set_invalid";
        public const string SourceChangeSetWorldMismatch = "source_change_set_world_mismatch";
        public const string SourceChangeSetWorldVersionMismatch = "source_change_set_world_version_mismatch";
        public const string SourceChangeSetExpired = "source_change_set_expired";
        public const string SourceChangeSetRegionMismatch = "source_change_set_region_mismatch";
        public const string ChangeSetCaptureFailed = "change_set_capture_failed";
        public const string QueueCapacityExceeded = "operation_queue_capacity_exceeded";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string DispatchCancelled = "game_thread_dispatch_cancelled";
        public const string ResultUnknown = "result_unknown";

        private SevenDaysRegionOperationResult(
            SevenDaysRegionOperationOutcome outcome,
            string? errorCode,
            string? changeSetId,
            long progressCurrent,
            long progressTotal)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
            ChangeSetId = changeSetId;
            Progress = new WorldOperationProgress(progressCurrent, progressTotal);
        }

        public SevenDaysRegionOperationOutcome Outcome { get; }
        public string? ErrorCode { get; }
        public string? ChangeSetId { get; }
        public WorldOperationProgress Progress { get; }

        internal static SevenDaysRegionOperationResult Succeeded(
            string changeSetId,
            long total) =>
            new SevenDaysRegionOperationResult(
                SevenDaysRegionOperationOutcome.Succeeded,
                null,
                RequireText(changeSetId, nameof(changeSetId)),
                total,
                total);

        internal static SevenDaysRegionOperationResult Rejected(string errorCode, long total) =>
            new SevenDaysRegionOperationResult(
                SevenDaysRegionOperationOutcome.Rejected,
                RequireText(errorCode, nameof(errorCode)),
                null,
                0,
                total);

        internal static SevenDaysRegionOperationResult Failed(
            string errorCode,
            long current,
            long total) =>
            new SevenDaysRegionOperationResult(
                SevenDaysRegionOperationOutcome.Failed,
                RequireText(errorCode, nameof(errorCode)),
                null,
                current,
                total);

        internal static SevenDaysRegionOperationResult Unknown(
            string? changeSetId,
            long current,
            long total) =>
            new SevenDaysRegionOperationResult(
                SevenDaysRegionOperationOutcome.ResultUnknown,
                ResultUnknown,
                changeSetId,
                current,
                total);

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value;
        }
    }

    internal readonly struct SevenDaysRegionBlock
    {
        internal SevenDaysRegionBlock(uint rawData, int damage, sbyte density)
        {
            RawData = rawData;
            Damage = damage;
            Density = density;
        }

        public uint RawData { get; }
        public int Damage { get; }
        public sbyte Density { get; }
    }

    internal sealed class SevenDaysRegionOperationContext
    {
        private SevenDaysRegionOperationContext(
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
            SevenDaysRegionBlock? fillBlock,
            Func<long, SevenDaysRegionBlock> captureBlock,
            Func<long, SevenDaysRegionBlock, bool> applyBlock)
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
            FillBlock = fillBlock;
            CaptureBlock = captureBlock ?? throw new ArgumentNullException(nameof(captureBlock));
            ApplyBlock = applyBlock ?? throw new ArgumentNullException(nameof(applyBlock));
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
        public SevenDaysRegionBlock? FillBlock { get; }
        public Func<long, SevenDaysRegionBlock> CaptureBlock { get; }
        public Func<long, SevenDaysRegionBlock, bool> ApplyBlock { get; }

        internal static SevenDaysRegionOperationContext Unavailable() =>
            new SevenDaysRegionOperationContext(
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
                _ => default,
                (_, _) => false);

        internal static SevenDaysRegionOperationContext Available(
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
            SevenDaysRegionBlock? fillBlock,
            Func<long, SevenDaysRegionBlock> captureBlock,
            Func<long, SevenDaysRegionBlock, bool> applyBlock) =>
            new SevenDaysRegionOperationContext(
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
                fillBlock,
                captureBlock,
                applyBlock);
    }

    public sealed class SevenDaysRegionOperationHandler
    {
        private static readonly TimeSpan ChangeSetRetention = TimeSpan.FromDays(30);

        private readonly Func<WorldOperationIntent, SevenDaysRegionOperationContext?> captureContext;
        private readonly IWorldChangeSetMetadataStore changeSets;
        private readonly IWorldChangeSetBlobStore blobs;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly WorldOperationBatchExecutor batchExecutor;

        public SevenDaysRegionOperationHandler(
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs)
            : this(changeSets, blobs, () => null)
        {
        }

        public SevenDaysRegionOperationHandler(
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            Func<string?> currentMapResourceVersion)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CreateNativeContextCapture(currentMapResourceVersion),
                changeSets,
                blobs,
                () => DateTimeOffset.UtcNow)
        {
        }

        internal SevenDaysRegionOperationHandler(
            WorldOperationBatchDispatcher dispatcher,
            Func<WorldOperationIntent, SevenDaysRegionOperationContext?> captureContext,
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            Func<DateTimeOffset> utcNow)
        {
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
            this.changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
            this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            batchExecutor = new WorldOperationBatchExecutor(
                dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)));
        }

        public Task<SevenDaysRegionOperationResult> HandleAsync(
            WorldOperationExecutionRecord execution,
            CancellationToken cancellationToken) =>
            HandleAsync(execution, null, cancellationToken);

        public async Task<SevenDaysRegionOperationResult> HandleAsync(
            WorldOperationExecutionRecord execution,
            Action<WorldOperationProgress>? reportProgress,
            CancellationToken cancellationToken)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            if (execution.Intent == null) throw new ArgumentException("An intent is required.", nameof(execution));
            if (string.IsNullOrWhiteSpace(execution.OperationId))
                throw new ArgumentException("An operation identifier is required.", nameof(execution));

            var shapeError = ValidateIntentShape(execution.Intent, out var targetRegion);
            if (shapeError != null || targetRegion == null)
                return SevenDaysRegionOperationResult.Rejected(
                    shapeError ?? SevenDaysRegionOperationResult.TargetInvalid,
                    0);

            WorldOperationBatchLease? lease;
            try
            {
                lease = await batchExecutor.TryEnterAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SevenDaysRegionOperationResult.Failed(
                    SevenDaysRegionOperationResult.DispatchCancelled,
                    0,
                    targetRegion.Volume);
            }
            if (lease == null)
            {
                return SevenDaysRegionOperationResult.Rejected(
                    SevenDaysRegionOperationResult.QueueCapacityExceeded,
                    targetRegion.Volume);
            }

            using (lease)
            {
                return await ExecuteReservedAsync(
                        execution,
                        targetRegion,
                        reportProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private async Task<SevenDaysRegionOperationResult> ExecuteReservedAsync(
            WorldOperationExecutionRecord execution,
            WorldRegion targetRegion,
            Action<WorldOperationProgress>? reportProgress,
            CancellationToken cancellationToken)
        {
            var intent = execution.Intent;
            var total = targetRegion.Volume;
            var sideEffectStarted = 0;
            string? changeSetId = null;
            try
            {
                SourceChangeSet? source = null;
                if (intent.Kind == WorldOperationKind.PasteRegion)
                {
                    source = LoadSource((WorldRegionOperationTarget)intent.Target);
                    if (source == null)
                    {
                        return SevenDaysRegionOperationResult.Rejected(
                            SevenDaysRegionOperationResult.SourceChangeSetInvalid,
                            total);
                    }
                }

                var before = RegionSnapshot.Create(targetRegion);
                var capture = await batchExecutor.ExecuteAsync(
                        total,
                        () => OpenCaptureBatch(intent, targetRegion, source, before),
                        intent.Kind == WorldOperationKind.CopyRegion
                            ? Progress(reportProgress)
                            : null,
                        cancellationToken)
                    .ConfigureAwait(false);
                var captureFailure = MapCaptureFailure(capture, total);
                if (captureFailure != null) return captureFailure;

                var beforeHash = Hash(before);
                WorldChangeSetDescriptor descriptor;
                Volatile.Write(ref sideEffectStarted, 1);
                try
                {
                    descriptor = PersistChangeSet(execution, targetRegion, before, beforeHash);
                    changeSetId = descriptor.ChangeSetId;
                }
                catch
                {
                    return SevenDaysRegionOperationResult.Unknown(changeSetId, 0, total);
                }

                if (intent.Kind == WorldOperationKind.CopyRegion)
                    return SevenDaysRegionOperationResult.Succeeded(descriptor.ChangeSetId, total);

                var apply = await batchExecutor.ExecuteAsync(
                        total,
                        () => OpenApplyBatch(intent, targetRegion, source),
                        Progress(reportProgress),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (apply.Status != WorldOperationBatchExecutionStatus.Completed)
                {
                    return SevenDaysRegionOperationResult.Unknown(
                        descriptor.ChangeSetId,
                        apply.CompletedBlocks,
                        total);
                }

                var after = RegionSnapshot.Create(targetRegion);
                var afterCapture = await batchExecutor.ExecuteAsync(
                        total,
                        () => OpenCaptureBatch(intent, targetRegion, source, after),
                        null,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (afterCapture.Status != WorldOperationBatchExecutionStatus.Completed)
                {
                    return SevenDaysRegionOperationResult.Unknown(
                        descriptor.ChangeSetId,
                        total,
                        total);
                }

                try
                {
                    changeSets.MarkApplied(descriptor.ChangeSetId, Hash(after));
                }
                catch
                {
                    return SevenDaysRegionOperationResult.Unknown(
                        descriptor.ChangeSetId,
                        total,
                        total);
                }
                return SevenDaysRegionOperationResult.Succeeded(descriptor.ChangeSetId, total);
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysRegionOperationResult.Failed(
                        SevenDaysRegionOperationResult.DispatchCancelled,
                        0,
                        total)
                    : SevenDaysRegionOperationResult.Unknown(changeSetId, 0, total);
            }
            catch
            {
                return Volatile.Read(ref sideEffectStarted) == 0
                    ? SevenDaysRegionOperationResult.Failed(
                        SevenDaysRegionOperationResult.DispatchFailed,
                        0,
                        total)
                    : SevenDaysRegionOperationResult.Unknown(changeSetId, 0, total);
            }
        }

        private WorldOperationBatchContext OpenCaptureBatch(
            WorldOperationIntent intent,
            WorldRegion targetRegion,
            SourceChangeSet? source,
            byte[] destination)
        {
            var context = captureContext(intent);
            var validationError = ValidateCurrentContext(intent, targetRegion, source, context);
            if (validationError != null)
                return WorldOperationBatchContext.Rejected(validationError);

            return WorldOperationBatchContext.Ready(index =>
            {
                RegionSnapshot.WriteBlock(destination, index, context!.CaptureBlock(index));
                return true;
            });
        }

        private WorldOperationBatchContext OpenApplyBatch(
            WorldOperationIntent intent,
            WorldRegion targetRegion,
            SourceChangeSet? source)
        {
            var context = captureContext(intent);
            var validationError = ValidateCurrentContext(intent, targetRegion, source, context);
            if (validationError != null)
                return WorldOperationBatchContext.Rejected(validationError);

            return WorldOperationBatchContext.Ready(index =>
            {
                SevenDaysRegionBlock desired;
                switch (intent.Kind)
                {
                    case WorldOperationKind.FillRegion:
                        desired = context!.FillBlock!.Value;
                        break;
                    case WorldOperationKind.ClearRegion:
                        desired = new SevenDaysRegionBlock(0, 0, sbyte.MaxValue);
                        break;
                    case WorldOperationKind.PasteRegion:
                        desired = RegionSnapshot.ReadBlock(source!.Content, index);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
                return context!.ApplyBlock(index, desired);
            });
        }

        private string? ValidateCurrentContext(
            WorldOperationIntent intent,
            WorldRegion targetRegion,
            SourceChangeSet? source,
            SevenDaysRegionOperationContext? context)
        {
            if (context == null || !context.WorldAvailable)
                return SevenDaysRegionOperationResult.WorldUnavailable;
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysRegionOperationResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysRegionOperationResult.WorldVersionChanged;
            if (!string.Equals(
                    context.MapResourceVersion,
                    intent.MapResourceVersion,
                    StringComparison.Ordinal))
            {
                return SevenDaysRegionOperationResult.MapResourceVersionChanged;
            }
            if (!WithinWorld(context, targetRegion))
                return SevenDaysRegionOperationResult.TargetBoundsInvalid;
            if (!context.TargetRegionLoaded)
                return SevenDaysRegionOperationResult.TargetRegionUnavailable;

            switch (intent.Kind)
            {
                case WorldOperationKind.CopyRegion:
                case WorldOperationKind.ClearRegion:
                    return null;

                case WorldOperationKind.FillRegion:
                    var target = (WorldRegionOperationTarget)intent.Target;
                    return context.FillBlock.HasValue &&
                           string.Equals(
                               context.BlockInternalName,
                               target.BlockInternalName,
                               StringComparison.Ordinal)
                        ? null
                        : SevenDaysRegionOperationResult.BlockResourceInvalid;

                case WorldOperationKind.PasteRegion:
                    return ValidateSource(intent, targetRegion, source);

                default:
                    return SevenDaysRegionOperationResult.OperationKindNotSupported;
            }
        }

        private string? ValidateSource(
            WorldOperationIntent intent,
            WorldRegion targetRegion,
            SourceChangeSet? source)
        {
            if (source == null)
                return SevenDaysRegionOperationResult.SourceChangeSetInvalid;
            var target = (WorldRegionOperationTarget)intent.Target;
            var descriptor = source.Descriptor;
            if (!string.Equals(
                    descriptor.ChangeSetId,
                    target.SourceChangeSetId,
                    StringComparison.Ordinal))
            {
                return SevenDaysRegionOperationResult.SourceChangeSetInvalid;
            }
            if (!string.Equals(descriptor.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysRegionOperationResult.SourceChangeSetWorldMismatch;
            if (!string.Equals(descriptor.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysRegionOperationResult.SourceChangeSetWorldVersionMismatch;
            if (descriptor.ExpiresAtUtc <= utcNow())
                return SevenDaysRegionOperationResult.SourceChangeSetExpired;
            return SameShape(descriptor.Region, targetRegion)
                ? null
                : SevenDaysRegionOperationResult.SourceChangeSetRegionMismatch;
        }

        private SourceChangeSet? LoadSource(WorldRegionOperationTarget target)
        {
            try
            {
                var descriptor = changeSets.Read(target.SourceChangeSetId!);
                var read = blobs.Read(descriptor.StorageResourceId, descriptor.BeforeHash);
                if (!string.Equals(
                        descriptor.BeforeHash,
                        descriptor.AfterHash,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        read.StorageResourceId,
                        descriptor.StorageResourceId,
                        StringComparison.Ordinal) ||
                    !string.Equals(read.ContentHash, descriptor.BeforeHash, StringComparison.Ordinal) ||
                    !string.Equals(Hash(read.Content), descriptor.BeforeHash, StringComparison.Ordinal) ||
                    !RegionSnapshot.Matches(read.Content, descriptor.Region))
                {
                    return null;
                }
                return new SourceChangeSet(descriptor, read.Content);
            }
            catch
            {
                return null;
            }
        }

        private WorldChangeSetDescriptor PersistChangeSet(
            WorldOperationExecutionRecord execution,
            WorldRegion region,
            byte[] content,
            string hash)
        {
            var storageResourceId = WorldChangeSetValidation.CreateStorageResourceId();
            var receipt = blobs.Write(new WorldChangeSetBlobDraft(
                storageResourceId,
                hash,
                content));
            if (!string.Equals(receipt.StorageResourceId, storageResourceId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ContentHash, hash, StringComparison.Ordinal) ||
                receipt.ByteCount != content.LongLength)
            {
                throw new InvalidOperationException();
            }

            var createdAtUtc = utcNow();
            var draft = new WorldChangeSetDraft(
                execution.OperationId,
                execution.Intent.WorldId,
                execution.Intent.WorldVersion,
                region,
                hash,
                hash,
                storageResourceId,
                createdAtUtc,
                createdAtUtc.Add(ChangeSetRetention));
            var descriptor = changeSets.Create(draft);
            if (descriptor == null ||
                string.IsNullOrWhiteSpace(descriptor.ChangeSetId) ||
                !string.Equals(descriptor.SourceOperationId, draft.SourceOperationId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.WorldId, draft.WorldId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.WorldVersion, draft.WorldVersion, StringComparison.Ordinal) ||
                !SameRegion(descriptor.Region, draft.Region) ||
                !string.Equals(descriptor.BeforeHash, draft.BeforeHash, StringComparison.Ordinal) ||
                !string.Equals(descriptor.AfterHash, draft.AfterHash, StringComparison.Ordinal) ||
                !string.Equals(
                    descriptor.StorageResourceId,
                    draft.StorageResourceId,
                    StringComparison.Ordinal) ||
                descriptor.CreatedAtUtc != draft.CreatedAtUtc ||
                descriptor.ExpiresAtUtc != draft.ExpiresAtUtc)
            {
                throw new InvalidOperationException();
            }
            return descriptor;
        }

        private static SevenDaysRegionOperationResult? MapCaptureFailure(
            WorldOperationBatchExecutionResult capture,
            long total)
        {
            switch (capture.Status)
            {
                case WorldOperationBatchExecutionStatus.Completed:
                    return null;
                case WorldOperationBatchExecutionStatus.Rejected:
                    return SevenDaysRegionOperationResult.Rejected(
                        capture.ErrorCode ?? SevenDaysRegionOperationResult.TargetInvalid,
                        total);
                case WorldOperationBatchExecutionStatus.Cancelled:
                    return SevenDaysRegionOperationResult.Failed(
                        SevenDaysRegionOperationResult.DispatchCancelled,
                        capture.CompletedBlocks,
                        total);
                default:
                    return SevenDaysRegionOperationResult.Failed(
                        SevenDaysRegionOperationResult.ChangeSetCaptureFailed,
                        capture.CompletedBlocks,
                        total);
            }
        }

        private static Action<long, long>? Progress(Action<WorldOperationProgress>? reportProgress) =>
            reportProgress == null
                ? null
                : (current, total) => reportProgress(new WorldOperationProgress(current, total));

        private static string? ValidateIntentShape(
            WorldOperationIntent intent,
            out WorldRegion? region)
        {
            region = null;
            if (!(intent.Target is WorldRegionOperationTarget target) ||
                !TryRegion(target, out region))
            {
                return IsRegionOperation(intent.Kind)
                    ? SevenDaysRegionOperationResult.TargetInvalid
                    : SevenDaysRegionOperationResult.OperationKindNotSupported;
            }

            switch (intent.Kind)
            {
                case WorldOperationKind.CopyRegion:
                    return !intent.IsReversible &&
                           target.SourceChangeSetId == null &&
                           target.BlockInternalName == null
                        ? null
                        : SevenDaysRegionOperationResult.TargetInvalid;

                case WorldOperationKind.FillRegion:
                    return intent.IsReversible &&
                           target.SourceChangeSetId == null &&
                           SafeToken(target.BlockInternalName)
                        ? null
                        : SevenDaysRegionOperationResult.TargetInvalid;

                case WorldOperationKind.ClearRegion:
                    return intent.IsReversible &&
                           target.SourceChangeSetId == null &&
                           target.BlockInternalName == null
                        ? null
                        : SevenDaysRegionOperationResult.TargetInvalid;

                case WorldOperationKind.PasteRegion:
                    return intent.IsReversible &&
                           SafeToken(target.SourceChangeSetId) &&
                           target.BlockInternalName == null
                        ? null
                        : SevenDaysRegionOperationResult.TargetInvalid;

                default:
                    region = null;
                    return SevenDaysRegionOperationResult.OperationKindNotSupported;
            }
        }

        private static bool IsRegionOperation(WorldOperationKind kind) =>
            kind == WorldOperationKind.CopyRegion ||
            kind == WorldOperationKind.FillRegion ||
            kind == WorldOperationKind.ClearRegion ||
            kind == WorldOperationKind.PasteRegion;

        private static bool SafeToken(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value!.IndexOf('/') < 0 &&
            value.IndexOf('\\') < 0 &&
            value.IndexOf("..", StringComparison.Ordinal) < 0 &&
            value.IndexOf('<') < 0 &&
            value.IndexOf('>') < 0;

        private static bool TryRegion(
            WorldRegionOperationTarget target,
            out WorldRegion? region)
        {
            try
            {
                if (target.MinimumX > target.MaximumX ||
                    target.MinimumY > target.MaximumY ||
                    target.MinimumZ > target.MaximumZ)
                {
                    region = null;
                    return false;
                }
                region = Region(
                    target.MinimumX,
                    target.MinimumY,
                    target.MinimumZ,
                    target.MaximumX,
                    target.MaximumY,
                    target.MaximumZ);
                return true;
            }
            catch
            {
                region = null;
                return false;
            }
        }

        private static bool WithinWorld(
            SevenDaysRegionOperationContext context,
            WorldRegion region) =>
            MinimumX(region) >= context.MinimumX &&
            MinimumY(region) >= context.MinimumY &&
            MinimumZ(region) >= context.MinimumZ &&
            MaximumX(region) <= context.MaximumX &&
            MaximumY(region) <= context.MaximumY &&
            MaximumZ(region) <= context.MaximumZ;

        private static bool SameShape(WorldRegion left, WorldRegion right) =>
            MaximumX(left) - MinimumX(left) == MaximumX(right) - MinimumX(right) &&
            MaximumY(left) - MinimumY(left) == MaximumY(right) - MinimumY(right) &&
            MaximumZ(left) - MinimumZ(left) == MaximumZ(right) - MinimumZ(right);

        private static bool SameRegion(WorldRegion left, WorldRegion right) =>
            MinimumX(left) == MinimumX(right) &&
            MinimumY(left) == MinimumY(right) &&
            MinimumZ(left) == MinimumZ(right) &&
            MaximumX(left) == MaximumX(right) &&
            MaximumY(left) == MaximumY(right) &&
            MaximumZ(left) == MaximumZ(right);

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

        private static int MinimumX(WorldRegion region) => checked((int)region.Minimum.X);
        private static int MinimumY(WorldRegion region) => checked((int)region.Minimum.Y);
        private static int MinimumZ(WorldRegion region) => checked((int)region.Minimum.Z);
        private static int MaximumX(WorldRegion region) => checked((int)region.Maximum.X);
        private static int MaximumY(WorldRegion region) => checked((int)region.Maximum.Y);
        private static int MaximumZ(WorldRegion region) => checked((int)region.Maximum.Z);

        private static string Hash(byte[] content)
        {
            byte[] hash;
            using (var algorithm = SHA256.Create()) hash = algorithm.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var value in hash)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static Func<WorldOperationIntent, SevenDaysRegionOperationContext?>
            CreateNativeContextCapture(Func<string?> currentMapResourceVersion)
        {
            if (currentMapResourceVersion == null)
                throw new ArgumentNullException(nameof(currentMapResourceVersion));
            return intent => CaptureNativeContext(intent, currentMapResourceVersion);
        }

        private static SevenDaysRegionOperationContext CaptureNativeContext(
            WorldOperationIntent intent,
            Func<string?> currentMapResourceVersion)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null ||
                string.IsNullOrWhiteSpace(world.Guid) ||
                !world.GetWorldExtent(out var minimum, out var maximum) ||
                !(intent.Target is WorldRegionOperationTarget target) ||
                !TryRegion(target, out var region) ||
                region == null)
            {
                return SevenDaysRegionOperationContext.Unavailable();
            }

            global::Block? block = null;
            SevenDaysRegionBlock? fillBlock = null;
            if (intent.Kind == WorldOperationKind.FillRegion)
            {
                block = global::Block.GetBlockByName(target.BlockInternalName, false);
                if (block != null)
                {
                    var value = block.ToBlockValue();
                    fillBlock = new SevenDaysRegionBlock(
                        value.rawData,
                        value.damage,
                        sbyte.MinValue);
                }
            }

            return SevenDaysRegionOperationContext.Available(
                world.Guid,
                world.Guid + ":" + world.worldTime.ToString(CultureInfo.InvariantCulture),
                currentMapResourceVersion(),
                minimum.x,
                minimum.y,
                minimum.z,
                maximum.x,
                maximum.y,
                maximum.z,
                RegionLoaded(world, region),
                block?.GetBlockName(),
                fillBlock,
                index =>
                {
                    var position = PositionAt(target, index);
                    var value = world.GetBlock(position);
                    return new SevenDaysRegionBlock(
                        value.rawData,
                        value.damage,
                        world.GetDensity(position));
                },
                (index, value) =>
                {
                    var position = PositionAt(target, index);
                    world.SetBlockRPC(
                        position,
                        new global::BlockValue(value.RawData, value.Damage));
                    world.SetDensity(position, value.Density, true);
                    return true;
                });
        }

        private static global::Vector3i PositionAt(
            WorldRegionOperationTarget target,
            long index)
        {
            var sizeY = checked((long)target.MaximumY - target.MinimumY + 1);
            var sizeZ = checked((long)target.MaximumZ - target.MinimumZ + 1);
            var yOffset = index % sizeY;
            var horizontal = index / sizeY;
            var zOffset = horizontal % sizeZ;
            var xOffset = horizontal / sizeZ;
            return new global::Vector3i(
                checked(target.MinimumX + (int)xOffset),
                checked(target.MinimumY + (int)yOffset),
                checked(target.MinimumZ + (int)zOffset));
        }

        private static bool RegionLoaded(global::World world, WorldRegion region)
        {
            var firstChunkX = ChunkCoordinate(MinimumX(region));
            var lastChunkX = ChunkCoordinate(MaximumX(region));
            var firstChunkZ = ChunkCoordinate(MinimumZ(region));
            var lastChunkZ = ChunkCoordinate(MaximumZ(region));
            for (var chunkX = firstChunkX; chunkX <= lastChunkX; chunkX++)
            {
                for (var chunkZ = firstChunkZ; chunkZ <= lastChunkZ; chunkZ++)
                {
                    var x = Math.Max((long)MinimumX(region), (long)chunkX * 16);
                    var z = Math.Max((long)MinimumZ(region), (long)chunkZ * 16);
                    if (world.GetChunkFromWorldPos(new global::Vector3i(
                            checked((int)x),
                            MinimumY(region),
                            checked((int)z))) == null)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static int ChunkCoordinate(int value) =>
            value >= 0 ? value / 16 : ((value + 1) / 16) - 1;

        private sealed class SourceChangeSet
        {
            internal SourceChangeSet(WorldChangeSetDescriptor descriptor, byte[] content)
            {
                Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
                Content = content == null
                    ? throw new ArgumentNullException(nameof(content))
                    : (byte[])content.Clone();
            }

            internal WorldChangeSetDescriptor Descriptor { get; }
            internal byte[] Content { get; }
        }
    }

    internal static class RegionSnapshot
    {
        private const int HeaderSize = 28;
        private const int BlockSize = 9;

        internal static byte[] Create(WorldRegion region)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            var length = checked(HeaderSize + checked((int)region.Volume) * BlockSize);
            var content = new byte[length];
            WriteInt32(content, 0, 1);
            WriteInt32(content, 4, checked((int)region.Minimum.X));
            WriteInt32(content, 8, checked((int)region.Minimum.Y));
            WriteInt32(content, 12, checked((int)region.Minimum.Z));
            WriteInt32(content, 16, checked((int)region.Maximum.X));
            WriteInt32(content, 20, checked((int)region.Maximum.Y));
            WriteInt32(content, 24, checked((int)region.Maximum.Z));
            return content;
        }

        internal static void WriteBlock(byte[] content, long index, SevenDaysRegionBlock block)
        {
            var offset = BlockOffset(content, index);
            WriteUInt32(content, offset, block.RawData);
            WriteInt32(content, offset + 4, block.Damage);
            content[offset + 8] = unchecked((byte)block.Density);
        }

        internal static SevenDaysRegionBlock ReadBlock(byte[] content, long index)
        {
            var offset = BlockOffset(content, index);
            return new SevenDaysRegionBlock(
                ReadUInt32(content, offset),
                ReadInt32(content, offset + 4),
                unchecked((sbyte)content[offset + 8]));
        }

        internal static bool Matches(byte[] content, WorldRegion region)
        {
            if (content == null || region == null || content.Length < HeaderSize)
                return false;
            try
            {
                return ReadInt32(content, 0) == 1 &&
                       ReadInt32(content, 4) == checked((int)region.Minimum.X) &&
                       ReadInt32(content, 8) == checked((int)region.Minimum.Y) &&
                       ReadInt32(content, 12) == checked((int)region.Minimum.Z) &&
                       ReadInt32(content, 16) == checked((int)region.Maximum.X) &&
                       ReadInt32(content, 20) == checked((int)region.Maximum.Y) &&
                       ReadInt32(content, 24) == checked((int)region.Maximum.Z) &&
                       content.Length == checked(HeaderSize + checked((int)region.Volume) * BlockSize);
            }
            catch
            {
                return false;
            }
        }

        private static int BlockOffset(byte[] content, long index)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var offset = checked(HeaderSize + checked((int)index) * BlockSize);
            if (offset < HeaderSize || offset > content.Length - BlockSize)
                throw new ArgumentOutOfRangeException(nameof(index));
            return offset;
        }

        private static void WriteInt32(byte[] content, int offset, int value) =>
            WriteUInt32(content, offset, unchecked((uint)value));

        private static void WriteUInt32(byte[] content, int offset, uint value)
        {
            content[offset] = (byte)value;
            content[offset + 1] = (byte)(value >> 8);
            content[offset + 2] = (byte)(value >> 16);
            content[offset + 3] = (byte)(value >> 24);
        }

        private static int ReadInt32(byte[] content, int offset) =>
            unchecked((int)ReadUInt32(content, offset));

        private static uint ReadUInt32(byte[] content, int offset) =>
            content[offset] |
            ((uint)content[offset + 1] << 8) |
            ((uint)content[offset + 2] << 16) |
            ((uint)content[offset + 3] << 24);
    }
}
