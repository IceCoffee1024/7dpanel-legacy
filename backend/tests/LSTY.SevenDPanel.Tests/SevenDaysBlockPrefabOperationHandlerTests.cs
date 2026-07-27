using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysBlockPrefabOperationHandlerTests
    {
        private static readonly DateTimeOffset CreatedAtUtc =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Set_block_revalidates_and_persists_change_set_before_the_first_side_effect()
        {
            var trace = new List<string>();
            var snapshots = 0;
            var metadata = new RecordingMetadataStore(trace);
            var blobs = new RecordingBlobStore(trace);
            var context = BlockContext(
                captureSnapshot: () =>
                {
                    trace.Add(snapshots++ == 0 ? "capture-before" : "capture-after");
                    return snapshots == 1 ? new byte[] { 1, 2, 3 } : new byte[] { 4, 5, 6 };
                },
                apply: () =>
                {
                    Assert.Equal(
                        new[] { "dispatch", "context", "capture-before", "blob", "metadata" },
                        trace);
                    trace.Add("apply");
                    return true;
                });

            var result = await Handler(context, metadata, blobs, trace).HandleAsync(
                Execution(
                    WorldOperationKind.SetBlock,
                    new WorldBlockOperationTarget(1, 2, 3, "steelBlock", 1, "Cube")),
                CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Succeeded, result.Outcome);
            Assert.Null(result.ErrorCode);
            Assert.Equal("change-set-1", result.ChangeSetId);
            Assert.Equal(
                new[]
                {
                    "dispatch", "context", "capture-before", "blob", "metadata",
                    "apply", "capture-after", "mark"
                },
                trace);
            Assert.NotNull(metadata.CreatedDraft);
            Assert.Equal("operation-1", metadata.CreatedDraft!.SourceOperationId);
            Assert.Equal("world-1", metadata.CreatedDraft.WorldId);
            Assert.Equal("world-v1", metadata.CreatedDraft.WorldVersion);
            Assert.Equal(metadata.CreatedDraft.BeforeHash, metadata.CreatedDraft.AfterHash);
            Assert.DoesNotContain("/", metadata.CreatedDraft.StorageResourceId, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", metadata.CreatedDraft.StorageResourceId, StringComparison.Ordinal);
            Assert.Equal("change-set-1", metadata.MarkedChangeSetId);
            Assert.NotEqual(metadata.CreatedDraft.BeforeHash, metadata.MarkedAfterHash);
        }

        [Theory]
        [InlineData("world-id", SevenDaysBlockPrefabOperationResult.WorldIdChanged)]
        [InlineData("world-version", SevenDaysBlockPrefabOperationResult.WorldVersionChanged)]
        [InlineData("map-version", SevenDaysBlockPrefabOperationResult.MapResourceVersionChanged)]
        public async Task World_versions_are_revalidated_inside_dispatch_before_evidence_or_side_effect(
            string drift,
            string expectedCode)
        {
            var trace = new List<string>();
            var context = BlockContext(
                worldId: drift == "world-id" ? "world-2" : "world-1",
                worldVersion: drift == "world-version" ? "world-v2" : "world-v1",
                mapResourceVersion: drift == "map-version" ? "map-v2" : "map-v1",
                captureSnapshot: () => throw new InvalidOperationException("must not capture"),
                apply: () => throw new InvalidOperationException("must not apply"));

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(
                        WorldOperationKind.SetBlock,
                        new WorldBlockOperationTarget(1, 2, 3, "steelBlock", 1, "Cube")),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context" }, trace);
        }

        [Theory]
        [InlineData("missing", SevenDaysBlockPrefabOperationResult.ResourceMissing)]
        [InlineData("shape", SevenDaysBlockPrefabOperationResult.BlockShapeInvalid)]
        [InlineData("rotation", SevenDaysBlockPrefabOperationResult.BlockRotationInvalid)]
        public async Task Block_resource_shape_and_rotation_are_revalidated_without_side_effect(
            string invalid,
            string expectedCode)
        {
            var trace = new List<string>();
            var context = BlockContext(
                blockInternalName: invalid == "missing" ? null : "steelBlock",
                blockShape: invalid == "shape" ? WorldBlockShape.Wedge : WorldBlockShape.Cube,
                rotationSupported: invalid != "rotation",
                captureSnapshot: () => throw new InvalidOperationException("must not capture"),
                apply: () => throw new InvalidOperationException("must not apply"));

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(
                        WorldOperationKind.SetBlock,
                        new WorldBlockOperationTarget(1, 2, 3, "steelBlock", 1, "Cube")),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context" }, trace);
        }

        [Fact]
        public async Task Forged_prefab_path_or_xml_is_rejected_before_dispatch()
        {
            var dispatched = false;
            var handler = new SevenDaysBlockPrefabOperationHandler(
                (_, _, _, _) =>
                {
                    dispatched = true;
                    throw new InvalidOperationException("must not dispatch");
                },
                _ => throw new InvalidOperationException("must not capture context"),
                new RecordingMetadataStore(new List<string>()),
                new RecordingBlobStore(new List<string>()),
                () => CreatedAtUtc,
                () => "world-change-safe");

            var result = await handler.HandleAsync(
                Execution(
                    WorldOperationKind.PlacePrefab,
                    new WorldPrefabOperationTarget(
                        "C:\\server\\prefabs\\forged.xml",
                        null,
                        10,
                        5,
                        10,
                        0,
                        10,
                        5,
                        10,
                        11,
                        6,
                        11)),
                CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysBlockPrefabOperationResult.TargetInvalid, result.ErrorCode);
            Assert.False(dispatched);
        }

        [Theory]
        [InlineData("missing", SevenDaysBlockPrefabOperationResult.ResourceMissing)]
        [InlineData("bounds", SevenDaysBlockPrefabOperationResult.PrefabBoundsChanged)]
        [InlineData("overlap", SevenDaysBlockPrefabOperationResult.PrefabOverlap)]
        public async Task Prefab_resource_bounds_and_overlap_are_revalidated_before_evidence(
            string invalid,
            string expectedCode)
        {
            var trace = new List<string>();
            var context = PlaceContext(
                prefabResourceId: invalid == "missing" ? null : "prefab-resource-1",
                bounds: invalid == "bounds" ? Region(10, 5, 10, 12, 6, 11) : Region(10, 5, 10, 11, 6, 11),
                overlaps: invalid == "overlap",
                captureSnapshot: () => throw new InvalidOperationException("must not capture"),
                apply: () => throw new InvalidOperationException("must not apply"));

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(WorldOperationKind.PlacePrefab, PlaceTarget()),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context" }, trace);
        }

        [Fact]
        public async Task Remove_prefab_revalidates_the_fixed_instance_identity()
        {
            var trace = new List<string>();
            var context = RemoveContext(
                prefabInstanceId: "instance-replacement",
                captureSnapshot: () => throw new InvalidOperationException("must not capture"),
                apply: () => throw new InvalidOperationException("must not apply"));

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(WorldOperationKind.RemovePrefab, RemoveTarget()),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysBlockPrefabOperationResult.PrefabIdentityChanged, result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context" }, trace);
        }

        [Fact]
        public async Task Change_set_capture_failure_is_sanitized_and_prevents_the_side_effect()
        {
            var trace = new List<string>();
            var sideEffects = 0;
            var context = BlockContext(
                captureSnapshot: () =>
                    throw new InvalidOperationException("C:\\private\\world.xml secret"),
                apply: () =>
                {
                    sideEffects++;
                    return true;
                });

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(
                        WorldOperationKind.SetBlock,
                        new WorldBlockOperationTarget(1, 2, 3, "steelBlock", 1, "Cube")),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Failed, result.Outcome);
            Assert.Equal(SevenDaysBlockPrefabOperationResult.ChangeSetCaptureFailed, result.ErrorCode);
            Assert.Null(result.ChangeSetId);
            Assert.Equal(0, sideEffects);
            Assert.DoesNotContain("private", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Partial_side_effect_or_post_start_exception_is_result_unknown_and_sanitized(
            bool throws)
        {
            var trace = new List<string>();
            var context = PlaceContext(
                captureSnapshot: () => new byte[] { 1, 2, 3 },
                apply: () =>
                {
                    if (throws)
                        throw new InvalidOperationException("C:\\private\\prefab.xml leaked");
                    return false;
                });

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(trace),
                    new RecordingBlobStore(trace),
                    trace)
                .HandleAsync(
                    Execution(WorldOperationKind.PlacePrefab, PlaceTarget()),
                    CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysBlockPrefabOperationResult.ResultUnknown, result.ErrorCode);
            Assert.Equal("change-set-1", result.ChangeSetId);
            Assert.DoesNotContain("private", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Other_operation_kinds_are_rejected_by_the_closed_switch()
        {
            var dispatched = false;
            var handler = new SevenDaysBlockPrefabOperationHandler(
                (_, _, _, _) =>
                {
                    dispatched = true;
                    throw new InvalidOperationException("must not dispatch");
                },
                _ => throw new InvalidOperationException("must not capture context"),
                new RecordingMetadataStore(new List<string>()),
                new RecordingBlobStore(new List<string>()),
                () => CreatedAtUtc,
                () => "world-change-safe");

            var result = await handler.HandleAsync(
                Execution(
                    WorldOperationKind.MoveEntity,
                    new WorldEntityOperationTarget(
                        "entity-1", 1, "entity-1", "type-1", null,
                        1, 2, 3, 4, 5, 6)),
                CancellationToken.None);

            Assert.Equal(SevenDaysBlockPrefabOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysBlockPrefabOperationResult.OperationKindNotSupported, result.ErrorCode);
            Assert.False(dispatched);
        }

        private static SevenDaysBlockPrefabOperationHandler Handler(
            SevenDaysBlockPrefabOperationContext context,
            IWorldChangeSetMetadataStore metadata,
            IWorldChangeSetBlobStore blobs,
            ICollection<string> trace) =>
            new SevenDaysBlockPrefabOperationHandler(
                (name, action, timeout, _) =>
                {
                    Assert.Equal("7DPanel.World.BlockPrefabOperation", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    trace.Add("dispatch");
                    return Task.FromResult(action());
                },
                _ =>
                {
                    trace.Add("context");
                    return context;
                },
                metadata,
                blobs,
                () => CreatedAtUtc,
                () => "world-change-safe");

        private static SevenDaysBlockPrefabOperationContext BlockContext(
            string worldId = "world-1",
            string worldVersion = "world-v1",
            string? mapResourceVersion = "map-v1",
            string? blockInternalName = "steelBlock",
            WorldBlockShape? blockShape = WorldBlockShape.Cube,
            bool rotationSupported = true,
            Func<byte[]>? captureSnapshot = null,
            Func<bool>? apply = null) =>
            SevenDaysBlockPrefabOperationContext.ForBlock(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimumX: -100,
                minimumY: 0,
                minimumZ: -100,
                maximumX: 100,
                maximumY: 255,
                maximumZ: 100,
                targetRegionLoaded: true,
                blockInternalName,
                blockShape,
                rotationSupported,
                captureSnapshot ?? (() => new byte[] { 1 }),
                apply ?? (() => true));

        private static SevenDaysBlockPrefabOperationContext PlaceContext(
            string? prefabResourceId = "prefab-resource-1",
            WorldRegion? bounds = null,
            bool overlaps = false,
            Func<byte[]>? captureSnapshot = null,
            Func<bool>? apply = null) =>
            SevenDaysBlockPrefabOperationContext.ForPrefab(
                "world-1",
                "world-v1",
                "map-v1",
                minimumX: -100,
                minimumY: 0,
                minimumZ: -100,
                maximumX: 100,
                maximumY: 255,
                maximumZ: 100,
                targetRegionLoaded: true,
                prefabResourceId,
                prefabInstanceId: null,
                anchorX: 10,
                anchorY: 5,
                anchorZ: 10,
                rotation: 0,
                bounds ?? Region(10, 5, 10, 11, 6, 11),
                overlaps,
                captureSnapshot ?? (() => new byte[] { 1 }),
                apply ?? (() => true));

        private static SevenDaysBlockPrefabOperationContext RemoveContext(
            string? prefabInstanceId = "instance-1",
            Func<byte[]>? captureSnapshot = null,
            Func<bool>? apply = null) =>
            SevenDaysBlockPrefabOperationContext.ForPrefab(
                "world-1",
                "world-v1",
                "map-v1",
                minimumX: -100,
                minimumY: 0,
                minimumZ: -100,
                maximumX: 100,
                maximumY: 255,
                maximumZ: 100,
                targetRegionLoaded: true,
                prefabResourceId: "prefab-resource-1",
                prefabInstanceId,
                anchorX: 10,
                anchorY: 5,
                anchorZ: 10,
                rotation: 0,
                Region(10, 5, 10, 11, 6, 11),
                overlaps: false,
                captureSnapshot ?? (() => new byte[] { 1 }),
                apply ?? (() => true));

        private static WorldPrefabOperationTarget PlaceTarget() =>
            new WorldPrefabOperationTarget(
                "prefab-resource-1", null, 10, 5, 10, 0,
                10, 5, 10, 11, 6, 11);

        private static WorldPrefabOperationTarget RemoveTarget() =>
            new WorldPrefabOperationTarget(
                "prefab-resource-1", "instance-1", 10, 5, 10, 0,
                10, 5, 10, 11, 6, 11);

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

        private static WorldOperationExecutionRecord Execution(
            WorldOperationKind kind,
            WorldOperationTarget target) =>
            new WorldOperationExecutionRecord(
                "operation-1",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new WorldOperationIntent(
                    "operator-1",
                    kind,
                    "world-1",
                    "world-v1",
                    "map-v1",
                    "correlation-1",
                    "Approved block or prefab operation",
                    true,
                    target,
                    CreatedAtUtc));

        private sealed class RecordingMetadataStore : IWorldChangeSetMetadataStore
        {
            private readonly ICollection<string> trace;

            public RecordingMetadataStore(ICollection<string> trace) => this.trace = trace;

            public WorldChangeSetDraft? CreatedDraft { get; private set; }
            public string? MarkedChangeSetId { get; private set; }
            public string? MarkedAfterHash { get; private set; }

            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft)
            {
                trace.Add("metadata");
                CreatedDraft = draft;
                return new WorldChangeSetDescriptor(
                    "change-set-1",
                    draft.SourceOperationId,
                    draft.WorldId,
                    draft.WorldVersion,
                    draft.Region,
                    draft.BeforeHash,
                    draft.AfterHash,
                    draft.StorageResourceId,
                    draft.CreatedAtUtc,
                    draft.ExpiresAtUtc);
            }

            public WorldChangeSetDescriptor Read(string changeSetId) =>
                throw new NotSupportedException();

            public void MarkApplied(string changeSetId, string afterHash)
            {
                trace.Add("mark");
                MarkedChangeSetId = changeSetId;
                MarkedAfterHash = afterHash;
            }
        }

        private sealed class RecordingBlobStore : IWorldChangeSetBlobStore
        {
            private readonly ICollection<string> trace;

            public RecordingBlobStore(ICollection<string> trace) => this.trace = trace;

            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft)
            {
                trace.Add("blob");
                return new WorldChangeSetBlobReceipt(
                    draft.StorageResourceId,
                    draft.ExpectedHash,
                    draft.Content.LongLength);
            }

            public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash) =>
                throw new NotSupportedException();
        }
    }
}
