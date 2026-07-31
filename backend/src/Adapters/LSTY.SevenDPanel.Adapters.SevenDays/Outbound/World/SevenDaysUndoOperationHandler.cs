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
    public enum SevenDaysUndoOperationOutcome
    {
        Succeeded,
        Rejected,
        Failed,
        Interrupted,
        ResultUnknown,
        RollbackFailed
    }

    public sealed class SevenDaysUndoOperationResult
    {
        public const string OperationKindNotSupported = "operation_kind_not_supported";
        public const string TargetInvalid = "target_invalid";
        public const string WorldUnavailable = "world_unavailable";
        public const string WorldIdChanged = "world_id_changed";
        public const string WorldVersionChanged = "world_version_changed";
        public const string TargetBoundsInvalid = "target_bounds_invalid";
        public const string TargetRegionUnavailable = "target_region_unavailable";
        public const string SourceOperationMismatch = "source_operation_mismatch";
        public const string ChangeSetInvalid = "change_set_invalid";
        public const string ChangeSetExpired = "change_set_expired";
        public const string ChangeSetCorrupt = "change_set_corrupt";
        public const string CurrentRegionChanged = "current_region_changed";
        public const string AlreadyUndone = "change_set_already_undone";
        public const string ChangeSetCaptureFailed = "change_set_capture_failed";
        public const string QueueCapacityExceeded = "operation_queue_capacity_exceeded";
        public const string DispatchFailed = "game_thread_dispatch_failed";
        public const string Interrupted = "interrupted";
        public const string ResultUnknown = "result_unknown";
        public const string RollbackFailed = "rollback_failed";

        private SevenDaysUndoOperationResult(
            SevenDaysUndoOperationOutcome outcome,
            string? errorCode,
            string? changeSetId,
            long current,
            long total)
        {
            Outcome = outcome;
            ErrorCode = errorCode;
            ChangeSetId = changeSetId;
            Progress = new WorldOperationProgress(current, total);
        }

        public SevenDaysUndoOperationOutcome Outcome { get; }
        public string? ErrorCode { get; }
        public string? ChangeSetId { get; }
        public WorldOperationProgress Progress { get; }

        internal static SevenDaysUndoOperationResult Succeeded(string changeSetId, long total) =>
            Create(SevenDaysUndoOperationOutcome.Succeeded, null, changeSetId, total, total);

        internal static SevenDaysUndoOperationResult Rejected(string errorCode, long total) =>
            Create(SevenDaysUndoOperationOutcome.Rejected, errorCode, null, 0, total);

        internal static SevenDaysUndoOperationResult Failed(string errorCode, long current, long total) =>
            Create(SevenDaysUndoOperationOutcome.Failed, errorCode, null, current, total);

        internal static SevenDaysUndoOperationResult InterruptedResult(
            string? changeSetId,
            long current,
            long total) =>
            Create(SevenDaysUndoOperationOutcome.Interrupted, Interrupted, changeSetId, current, total);

        internal static SevenDaysUndoOperationResult Unknown(
            string? changeSetId,
            long current,
            long total) =>
            Create(SevenDaysUndoOperationOutcome.ResultUnknown, ResultUnknown, changeSetId, current, total);

        internal static SevenDaysUndoOperationResult RollbackFailure(
            string? changeSetId,
            long current,
            long total) =>
            Create(SevenDaysUndoOperationOutcome.RollbackFailed, RollbackFailed, changeSetId, current, total);

        private static SevenDaysUndoOperationResult Create(
            SevenDaysUndoOperationOutcome outcome,
            string? errorCode,
            string? changeSetId,
            long current,
            long total) =>
            new SevenDaysUndoOperationResult(outcome, errorCode, changeSetId, current, total);
    }

    public sealed class SevenDaysUndoOperationHandler : IWorldChangeSetPreflightGateway
    {
        private static readonly TimeSpan ChangeSetRetention = TimeSpan.FromDays(30);

        private readonly Func<WorldOperationIntent, SevenDaysRegionOperationContext?> captureContext;
        private readonly IWorldChangeSetMetadataStore changeSets;
        private readonly IWorldChangeSetBlobStore blobs;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<string> createStorageResourceId;
        private readonly WorldOperationBatchExecutor batchExecutor;

        public SevenDaysUndoOperationHandler(
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs)
            : this(
                (name, action, timeout, cancellationToken) =>
                    GameThreadDispatcher.Enqueue(name, action, timeout, cancellationToken),
                CaptureNativeContext,
                changeSets,
                blobs,
                () => DateTimeOffset.UtcNow,
                WorldChangeSetValidation.CreateStorageResourceId)
        {
        }

        internal SevenDaysUndoOperationHandler(
            WorldOperationBatchDispatcher dispatcher,
            Func<WorldOperationIntent, SevenDaysRegionOperationContext?> captureContext,
            IWorldChangeSetMetadataStore changeSets,
            IWorldChangeSetBlobStore blobs,
            Func<DateTimeOffset> utcNow,
            Func<string> createStorageResourceId)
        {
            this.captureContext = captureContext ?? throw new ArgumentNullException(nameof(captureContext));
            this.changeSets = changeSets ?? throw new ArgumentNullException(nameof(changeSets));
            this.blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            this.createStorageResourceId = createStorageResourceId ??
                throw new ArgumentNullException(nameof(createStorageResourceId));
            batchExecutor = new WorldOperationBatchExecutor(
                dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)));
        }

        public async Task<WorldChangeSetRuntimeHashResult> ReadCurrentRegionHashAsync(
            WorldChangeSetDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (descriptor.Region == null)
                return WorldChangeSetRuntimeHashResult.Unavailable("undo_preflight_region_invalid");

            WorldOperationBatchLease? lease;
            try { lease = await batchExecutor.TryEnterAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return WorldChangeSetRuntimeHashResult.Unavailable(
                    "undo_preflight_runtime_unavailable");
            }
            if (lease == null)
                return WorldChangeSetRuntimeHashResult.Unavailable("undo_preflight_busy");

            using (lease)
            {
                var region = descriptor.Region;
                var intent = new WorldOperationIntent(
                    "undo-preflight",
                    WorldOperationKind.UndoChangeSet,
                    descriptor.WorldId,
                    descriptor.WorldVersion,
                    null,
                    "undo-preflight",
                    "Inspect undo change set",
                    true,
                    new WorldRegionOperationTarget(
                        MinimumX(region),
                        MinimumY(region),
                        MinimumZ(region),
                        MaximumX(region),
                        MaximumY(region),
                        MaximumZ(region),
                        descriptor.ChangeSetId,
                        null),
                    descriptor.CreatedAtUtc);
                var captured = await CaptureHashAsync(intent, region, cancellationToken)
                    .ConfigureAwait(false);
                if (captured.Status == WorldOperationBatchExecutionStatus.Completed &&
                    captured.Hash != null)
                {
                    return WorldChangeSetRuntimeHashResult.Available(captured.Hash);
                }
                if (captured.Status == WorldOperationBatchExecutionStatus.Cancelled)
                    throw new OperationCanceledException(cancellationToken);
                return WorldChangeSetRuntimeHashResult.Unavailable(
                    captured.ErrorCode ?? "undo_preflight_runtime_unavailable");
            }
        }

        public Task<SevenDaysUndoOperationResult> HandleAsync(
            WorldOperationExecutionRecord execution,
            CancellationToken cancellationToken) =>
            HandleAsync(execution, null, cancellationToken);

        public async Task<SevenDaysUndoOperationResult> HandleAsync(
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
                return SevenDaysUndoOperationResult.Rejected(
                    shapeError ?? SevenDaysUndoOperationResult.TargetInvalid,
                    0);

            WorldOperationBatchLease? lease;
            try { lease = await batchExecutor.TryEnterAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                return SevenDaysUndoOperationResult.InterruptedResult(null, 0, targetRegion.Volume);
            }
            if (lease == null)
                return SevenDaysUndoOperationResult.Rejected(
                    SevenDaysUndoOperationResult.QueueCapacityExceeded,
                    targetRegion.Volume);

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

        private async Task<SevenDaysUndoOperationResult> ExecuteReservedAsync(
            WorldOperationExecutionRecord execution,
            WorldRegion region,
            Action<WorldOperationProgress>? reportProgress,
            CancellationToken cancellationToken)
        {
            var total = region.Volume;
            var loaded = LoadSource(execution.Intent, region);
            if (loaded.Source == null)
                return SevenDaysUndoOperationResult.Rejected(loaded.ErrorCode!, total);
            var source = loaded.Source;

            var current = RegionSnapshot.Create(region);
            var capture = await batchExecutor.ExecuteAsync(
                    total,
                    () => OpenCaptureBatch(execution.Intent, region, current),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (capture.Status == WorldOperationBatchExecutionStatus.Cancelled)
                return SevenDaysUndoOperationResult.InterruptedResult(null, 0, total);
            if (capture.Status == WorldOperationBatchExecutionStatus.Rejected)
                return SevenDaysUndoOperationResult.Rejected(capture.ErrorCode!, total);
            if (capture.Status != WorldOperationBatchExecutionStatus.Completed)
                return SevenDaysUndoOperationResult.Failed(
                    SevenDaysUndoOperationResult.DispatchFailed,
                    0,
                    total);

            var currentHash = Hash(current);
            if (!string.Equals(currentHash, source.Descriptor.AfterHash, StringComparison.Ordinal))
                return SevenDaysUndoOperationResult.Rejected(
                    SevenDaysUndoOperationResult.CurrentRegionChanged,
                    total);

            var refreshed = RefreshSource(source.Descriptor, region);
            if (refreshed != null)
                return SevenDaysUndoOperationResult.Rejected(refreshed, total);

            WorldChangeSetDescriptor rollbackEvidence;
            try { rollbackEvidence = PersistRollbackEvidence(execution, region, current, currentHash); }
            catch
            {
                return SevenDaysUndoOperationResult.Failed(
                    SevenDaysUndoOperationResult.ChangeSetCaptureFailed,
                    0,
                    total);
            }

            if (cancellationToken.IsCancellationRequested)
                return SevenDaysUndoOperationResult.InterruptedResult(
                    rollbackEvidence.ChangeSetId,
                    0,
                    total);

            var apply = await batchExecutor.ExecuteAsync(
                    total,
                    () => OpenApplyBatch(execution.Intent, region, source.Content, current),
                    Progress(reportProgress),
                    cancellationToken)
                .ConfigureAwait(false);
            if (apply.Status == WorldOperationBatchExecutionStatus.Cancelled)
            {
                if (apply.CompletedBlocks == 0)
                    return SevenDaysUndoOperationResult.InterruptedResult(
                        rollbackEvidence.ChangeSetId,
                        0,
                        total);
                return await RollbackInterruptedAsync(
                        execution.Intent,
                        region,
                        current,
                        currentHash,
                        rollbackEvidence.ChangeSetId,
                        apply.CompletedBlocks,
                        total)
                    .ConfigureAwait(false);
            }
            if (apply.Status == WorldOperationBatchExecutionStatus.Rejected &&
                apply.CompletedBlocks == 0)
            {
                return SevenDaysUndoOperationResult.Rejected(
                    apply.ErrorCode ?? SevenDaysUndoOperationResult.TargetInvalid,
                    total);
            }
            if (apply.Status != WorldOperationBatchExecutionStatus.Completed)
                return SevenDaysUndoOperationResult.Unknown(
                    rollbackEvidence.ChangeSetId,
                    apply.CompletedBlocks,
                    total);

            var verified = await CaptureHashAsync(
                    execution.Intent,
                    region,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (verified.Status != WorldOperationBatchExecutionStatus.Completed ||
                !string.Equals(verified.Hash, source.Descriptor.BeforeHash, StringComparison.Ordinal))
            {
                return SevenDaysUndoOperationResult.Unknown(rollbackEvidence.ChangeSetId, total, total);
            }

            try
            {
                changeSets.MarkApplied(rollbackEvidence.ChangeSetId, source.Descriptor.BeforeHash);
                changeSets.MarkApplied(source.Descriptor.ChangeSetId, source.Descriptor.BeforeHash);
            }
            catch
            {
                return SevenDaysUndoOperationResult.Unknown(rollbackEvidence.ChangeSetId, total, total);
            }
            return SevenDaysUndoOperationResult.Succeeded(rollbackEvidence.ChangeSetId, total);
        }

        private async Task<SevenDaysUndoOperationResult> RollbackInterruptedAsync(
            WorldOperationIntent intent,
            WorldRegion region,
            byte[] current,
            string currentHash,
            string changeSetId,
            long completed,
            long total)
        {
            try
            {
                var rollback = await batchExecutor.ExecuteAsync(
                        total,
                        () => OpenRollbackBatch(intent, region, current),
                        null,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (rollback.Status != WorldOperationBatchExecutionStatus.Completed)
                    return SevenDaysUndoOperationResult.RollbackFailure(changeSetId, completed, total);
                var verified = await CaptureHashAsync(intent, region, CancellationToken.None)
                    .ConfigureAwait(false);
                return verified.Status == WorldOperationBatchExecutionStatus.Completed &&
                       string.Equals(verified.Hash, currentHash, StringComparison.Ordinal)
                    ? SevenDaysUndoOperationResult.InterruptedResult(changeSetId, completed, total)
                    : SevenDaysUndoOperationResult.RollbackFailure(changeSetId, completed, total);
            }
            catch
            {
                return SevenDaysUndoOperationResult.RollbackFailure(changeSetId, completed, total);
            }
        }

        private WorldOperationBatchContext OpenCaptureBatch(
            WorldOperationIntent intent,
            WorldRegion region,
            byte[] destination)
        {
            var context = captureContext(intent);
            var error = ValidateContext(intent, region, context);
            if (error != null) return WorldOperationBatchContext.Rejected(error);
            return WorldOperationBatchContext.Ready(index =>
            {
                RegionSnapshot.WriteBlock(destination, index, context!.CaptureBlock(index));
                return true;
            });
        }

        private WorldOperationBatchContext OpenApplyBatch(
            WorldOperationIntent intent,
            WorldRegion region,
            byte[] before,
            byte[] expectedCurrent)
        {
            var context = captureContext(intent);
            var error = ValidateContext(intent, region, context);
            if (error != null) return WorldOperationBatchContext.Rejected(error);
            return WorldOperationBatchContext.Ready(index =>
            {
                if (!Equal(context!.CaptureBlock(index), RegionSnapshot.ReadBlock(expectedCurrent, index)))
                    return false;
                return context.ApplyBlock(index, RegionSnapshot.ReadBlock(before, index));
            });
        }

        private WorldOperationBatchContext OpenRollbackBatch(
            WorldOperationIntent intent,
            WorldRegion region,
            byte[] current)
        {
            var context = captureContext(intent);
            var error = ValidateContext(intent, region, context);
            if (error != null) return WorldOperationBatchContext.Rejected(error);
            return WorldOperationBatchContext.Ready(index =>
                context!.ApplyBlock(index, RegionSnapshot.ReadBlock(current, index)));
        }

        private async Task<(
            WorldOperationBatchExecutionStatus Status,
            string? Hash,
            string? ErrorCode)> CaptureHashAsync(
            WorldOperationIntent intent,
            WorldRegion region,
            CancellationToken cancellationToken)
        {
            var content = RegionSnapshot.Create(region);
            var result = await batchExecutor.ExecuteAsync(
                    region.Volume,
                    () => OpenCaptureBatch(intent, region, content),
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            return (result.Status, result.Status == WorldOperationBatchExecutionStatus.Completed
                ? Hash(content)
                : null, result.ErrorCode);
        }

        private (SourceChangeSet? Source, string? ErrorCode) LoadSource(
            WorldOperationIntent intent,
            WorldRegion region)
        {
            try
            {
                var target = (WorldRegionOperationTarget)intent.Target;
                var descriptor = changeSets.Read(target.SourceChangeSetId!);
                if (descriptor == null || descriptor.Region == null ||
                    !string.Equals(descriptor.ChangeSetId, target.SourceChangeSetId, StringComparison.Ordinal) ||
                    !SafeToken(descriptor.SourceOperationId) ||
                    string.Equals(descriptor.SourceOperationId, target.SourceChangeSetId, StringComparison.Ordinal) ||
                    !GeneratedStorageResourceId(descriptor.StorageResourceId))
                    return (null, SevenDaysUndoOperationResult.ChangeSetInvalid);
                if (!string.Equals(descriptor.WorldId, intent.WorldId, StringComparison.Ordinal))
                    return (null, SevenDaysUndoOperationResult.WorldIdChanged);
                if (!string.Equals(descriptor.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                    return (null, SevenDaysUndoOperationResult.WorldVersionChanged);
                if (!SameRegion(descriptor.Region, region))
                    return (null, SevenDaysUndoOperationResult.TargetInvalid);
                if (descriptor.ExpiresAtUtc <= utcNow())
                    return (null, SevenDaysUndoOperationResult.ChangeSetExpired);
                if (string.Equals(descriptor.BeforeHash, descriptor.AfterHash, StringComparison.Ordinal))
                    return (null, SevenDaysUndoOperationResult.AlreadyUndone);
                var read = blobs.Read(descriptor.StorageResourceId, descriptor.BeforeHash);
                if (read == null ||
                    !string.Equals(read.StorageResourceId, descriptor.StorageResourceId, StringComparison.Ordinal) ||
                    !string.Equals(read.ContentHash, descriptor.BeforeHash, StringComparison.Ordinal) ||
                    !string.Equals(Hash(read.Content), descriptor.BeforeHash, StringComparison.Ordinal) ||
                    !RegionSnapshot.Matches(read.Content, descriptor.Region))
                    return (null, SevenDaysUndoOperationResult.ChangeSetCorrupt);
                return (new SourceChangeSet(descriptor, read.Content), null);
            }
            catch
            {
                return (null, SevenDaysUndoOperationResult.ChangeSetCorrupt);
            }
        }

        private string? RefreshSource(WorldChangeSetDescriptor expected, WorldRegion region)
        {
            try
            {
                var current = changeSets.Read(expected.ChangeSetId);
                if (current == null ||
                    !string.Equals(current.ChangeSetId, expected.ChangeSetId, StringComparison.Ordinal) ||
                    !string.Equals(current.SourceOperationId, expected.SourceOperationId, StringComparison.Ordinal) ||
                    !string.Equals(current.WorldId, expected.WorldId, StringComparison.Ordinal) ||
                    !string.Equals(current.WorldVersion, expected.WorldVersion, StringComparison.Ordinal) ||
                    !string.Equals(current.StorageResourceId, expected.StorageResourceId, StringComparison.Ordinal) ||
                    !string.Equals(current.BeforeHash, expected.BeforeHash, StringComparison.Ordinal) ||
                    !string.Equals(current.AfterHash, expected.AfterHash, StringComparison.Ordinal) ||
                    !SameRegion(current.Region, region))
                    return SevenDaysUndoOperationResult.ChangeSetInvalid;
                if (current.ExpiresAtUtc <= utcNow()) return SevenDaysUndoOperationResult.ChangeSetExpired;
                return null;
            }
            catch { return SevenDaysUndoOperationResult.ChangeSetInvalid; }
        }

        private WorldChangeSetDescriptor PersistRollbackEvidence(
            WorldOperationExecutionRecord execution,
            WorldRegion region,
            byte[] current,
            string currentHash)
        {
            var resourceId = createStorageResourceId();
            var receipt = blobs.Write(new WorldChangeSetBlobDraft(resourceId, currentHash, current));
            if (receipt == null ||
                !string.Equals(receipt.StorageResourceId, resourceId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ContentHash, currentHash, StringComparison.Ordinal) ||
                receipt.ByteCount != current.LongLength)
                throw new InvalidOperationException();
            var createdAtUtc = utcNow();
            var draft = new WorldChangeSetDraft(
                execution.OperationId,
                execution.Intent.WorldId,
                execution.Intent.WorldVersion,
                region,
                currentHash,
                currentHash,
                resourceId,
                createdAtUtc,
                createdAtUtc.Add(ChangeSetRetention));
            var descriptor = changeSets.Create(draft);
            if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.ChangeSetId) ||
                !string.Equals(descriptor.SourceOperationId, draft.SourceOperationId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.WorldId, draft.WorldId, StringComparison.Ordinal) ||
                !string.Equals(descriptor.WorldVersion, draft.WorldVersion, StringComparison.Ordinal) ||
                !SameRegion(descriptor.Region, draft.Region) ||
                !string.Equals(descriptor.BeforeHash, draft.BeforeHash, StringComparison.Ordinal) ||
                !string.Equals(descriptor.AfterHash, draft.AfterHash, StringComparison.Ordinal) ||
                !string.Equals(descriptor.StorageResourceId, draft.StorageResourceId, StringComparison.Ordinal) ||
                descriptor.CreatedAtUtc != draft.CreatedAtUtc ||
                descriptor.ExpiresAtUtc != draft.ExpiresAtUtc)
                throw new InvalidOperationException();
            return descriptor;
        }

        private static string? ValidateIntentShape(WorldOperationIntent intent, out WorldRegion? region)
        {
            region = null;
            if (intent.Kind != WorldOperationKind.UndoChangeSet)
                return SevenDaysUndoOperationResult.OperationKindNotSupported;
            if (!intent.IsReversible || intent.MapResourceVersion != null ||
                !(intent.Target is WorldRegionOperationTarget target) ||
                !SafeToken(target.SourceChangeSetId) || target.BlockInternalName != null ||
                !TryRegion(target, out region))
                return SevenDaysUndoOperationResult.TargetInvalid;
            return null;
        }

        private static string? ValidateContext(
            WorldOperationIntent intent,
            WorldRegion region,
            SevenDaysRegionOperationContext? context)
        {
            if (context == null || !context.WorldAvailable)
                return SevenDaysUndoOperationResult.WorldUnavailable;
            if (!string.Equals(context.WorldId, intent.WorldId, StringComparison.Ordinal))
                return SevenDaysUndoOperationResult.WorldIdChanged;
            if (!string.Equals(context.WorldVersion, intent.WorldVersion, StringComparison.Ordinal))
                return SevenDaysUndoOperationResult.WorldVersionChanged;
            if (!WithinWorld(context, region))
                return SevenDaysUndoOperationResult.TargetBoundsInvalid;
            return context.TargetRegionLoaded
                ? null
                : SevenDaysUndoOperationResult.TargetRegionUnavailable;
        }

        private static Action<long, long>? Progress(Action<WorldOperationProgress>? reportProgress) =>
            reportProgress == null
                ? null
                : (current, total) => reportProgress(new WorldOperationProgress(current, total));

        private static bool Equal(SevenDaysRegionBlock left, SevenDaysRegionBlock right) =>
            left.RawData == right.RawData && left.Damage == right.Damage && left.Density == right.Density;

        private static bool SafeToken(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value!.Length <= 200 &&
            value.IndexOf('/') < 0 && value.IndexOf('\\') < 0 &&
            value.IndexOf("..", StringComparison.Ordinal) < 0 &&
            value.IndexOf('<') < 0 && value.IndexOf('>') < 0;

        private static bool GeneratedStorageResourceId(string? value)
        {
            if (value == null || value.Length != 36 || !value.StartsWith("wcs-", StringComparison.Ordinal))
                return false;
            for (var index = 4; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                    return false;
            }
            return true;
        }

        private static bool TryRegion(WorldRegionOperationTarget target, out WorldRegion? region)
        {
            try
            {
                if (target.MinimumX > target.MaximumX || target.MinimumY > target.MaximumY ||
                    target.MinimumZ > target.MaximumZ)
                {
                    region = null;
                    return false;
                }
                region = new WorldRegion(
                    new WorldCoordinate(target.MinimumX, target.MinimumY, target.MinimumZ),
                    new WorldCoordinate(target.MaximumX, target.MaximumY, target.MaximumZ));
                return true;
            }
            catch
            {
                region = null;
                return false;
            }
        }

        private static bool SameRegion(WorldRegion left, WorldRegion right) =>
            MinimumX(left) == MinimumX(right) && MinimumY(left) == MinimumY(right) &&
            MinimumZ(left) == MinimumZ(right) && MaximumX(left) == MaximumX(right) &&
            MaximumY(left) == MaximumY(right) && MaximumZ(left) == MaximumZ(right);

        private static bool WithinWorld(SevenDaysRegionOperationContext context, WorldRegion region) =>
            MinimumX(region) >= context.MinimumX && MinimumY(region) >= context.MinimumY &&
            MinimumZ(region) >= context.MinimumZ && MaximumX(region) <= context.MaximumX &&
            MaximumY(region) <= context.MaximumY && MaximumZ(region) <= context.MaximumZ;

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
            foreach (var value in hash) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static SevenDaysRegionOperationContext CaptureNativeContext(WorldOperationIntent intent)
        {
            var manager = global::GameManager.Instance;
            var world = manager?.World;
            if (manager == null || world == null || string.IsNullOrWhiteSpace(world.Guid) ||
                !world.GetWorldExtent(out var minimum, out var maximum) ||
                !(intent.Target is WorldRegionOperationTarget target) ||
                !TryRegion(target, out var region) || region == null)
                return SevenDaysRegionOperationContext.Unavailable();
            return SevenDaysRegionOperationContext.Available(
                world.Guid,
                world.Guid + ":" + world.worldTime.ToString(CultureInfo.InvariantCulture),
                null,
                minimum.x,
                minimum.y,
                minimum.z,
                maximum.x,
                maximum.y,
                maximum.z,
                RegionLoaded(world, region),
                null,
                null,
                index =>
                {
                    var position = PositionAt(target, index);
                    var value = world.GetBlock(position);
                    return new SevenDaysRegionBlock(value.rawData, value.damage, world.GetDensity(position));
                },
                (index, value) =>
                {
                    var position = PositionAt(target, index);
                    world.SetBlockRPC(position, new global::BlockValue(value.RawData, value.Damage));
                    world.SetDensity(position, value.Density, true);
                    return true;
                });
        }

        private static global::Vector3i PositionAt(WorldRegionOperationTarget target, long index)
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
            for (var chunkZ = firstChunkZ; chunkZ <= lastChunkZ; chunkZ++)
            {
                var x = Math.Max((long)MinimumX(region), (long)chunkX * 16);
                var z = Math.Max((long)MinimumZ(region), (long)chunkZ * 16);
                if (world.GetChunkFromWorldPos(new global::Vector3i(
                        checked((int)x), MinimumY(region), checked((int)z))) == null)
                    return false;
            }
            return true;
        }

        private static int ChunkCoordinate(int value) =>
            value >= 0 ? value / 16 : ((value + 1) / 16) - 1;

        private sealed class SourceChangeSet
        {
            internal SourceChangeSet(WorldChangeSetDescriptor descriptor, byte[] content)
            {
                Descriptor = descriptor;
                Content = (byte[])content.Clone();
            }

            internal WorldChangeSetDescriptor Descriptor { get; }
            internal byte[] Content { get; }
        }
    }
}
