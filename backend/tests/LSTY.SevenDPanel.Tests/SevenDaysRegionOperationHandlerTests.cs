using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
    public sealed class SevenDaysRegionOperationHandlerTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Copy_batches_game_thread_capture_and_only_generates_a_change_set()
        {
            var inDispatch = false;
            var dispatchBlockCounts = new List<int>();
            var blocksInDispatch = 0;
            var applyCalls = 0;
            var metadata = new RecordingMetadataStore();
            var blobs = new RecordingBlobStore(() => Assert.False(inDispatch));
            var context = Context(
                capture: index =>
                {
                    Assert.True(inDispatch);
                    blocksInDispatch++;
                    return Block((uint)(index + 1));
                },
                apply: (_, _) =>
                {
                    applyCalls++;
                    return true;
                });
            var handler = Handler(
                context,
                metadata,
                blobs,
                (name, action, timeout, cancellationToken) =>
                {
                    Assert.Equal("7DPanel.World.RegionOperation.Batch", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    Assert.False(cancellationToken.CanBeCanceled);
                    blocksInDispatch = 0;
                    inDispatch = true;
                    try { return Task.FromResult(action()); }
                    finally
                    {
                        inDispatch = false;
                        dispatchBlockCounts.Add(blocksInDispatch);
                    }
                });
            var progress = new List<long>();

            var result = await handler.HandleAsync(
                Execution(WorldOperationKind.CopyRegion, Target(volumeX: 300)),
                value => progress.Add(value.Current!.Value),
                CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.Succeeded, result.Outcome);
            Assert.Null(result.ErrorCode);
            Assert.Equal("change-set-1", result.ChangeSetId);
            Assert.Equal(0, applyCalls);
            Assert.All(dispatchBlockCounts, count => Assert.InRange(count, 1, 256));
            Assert.Equal(0, progress[0]);
            Assert.Equal(300, progress[progress.Count - 1]);
            Assert.True(IsMonotonic(progress));
            Assert.NotNull(metadata.CreatedDraft);
            Assert.Equal(metadata.CreatedDraft!.BeforeHash, metadata.CreatedDraft.AfterHash);
            Assert.Matches(
                new Regex("^wcs-[0-9a-f]{32}$", RegexOptions.CultureInvariant),
                metadata.CreatedDraft.StorageResourceId);
            Assert.Equal(metadata.CreatedDraft.StorageResourceId, blobs.LastWrittenResourceId);
        }

        [Fact]
        public async Task Fill_persists_before_snapshot_before_first_write_and_reports_monotonic_progress()
        {
            var trace = new List<string>();
            var metadata = new RecordingMetadataStore(trace);
            var blobs = new RecordingBlobStore(trace: trace);
            var applied = 0;
            var context = Context(
                blockInternalName: "steelBlock",
                fillBlock: Block(99),
                capture: index =>
                {
                    trace.Add(applied == 0 ? "capture-before" : "capture-after");
                    return Block((uint)(index + (applied == 0 ? 0 : 1000)));
                },
                apply: (_, value) =>
                {
                    Assert.NotNull(metadata.CreatedDraft);
                    Assert.Equal(1, blobs.WriteCalls);
                    Assert.Equal(99u, value.RawData);
                    trace.Add("apply");
                    applied++;
                    return true;
                });
            var progress = new List<long>();

            var result = await Handler(context, metadata, blobs).HandleAsync(
                Execution(
                    WorldOperationKind.FillRegion,
                    Target(volumeX: 300, blockInternalName: "steelBlock")),
                value => progress.Add(value.Current!.Value),
                CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.Succeeded, result.Outcome);
            Assert.Equal(300, applied);
            Assert.Equal(0, progress[0]);
            Assert.Equal(300, progress[progress.Count - 1]);
            Assert.True(IsMonotonic(progress));
            Assert.Equal("change-set-1", metadata.MarkedChangeSetId);
            Assert.NotEqual(metadata.CreatedDraft!.BeforeHash, metadata.MarkedAfterHash);
            Assert.True(trace.IndexOf("blob") < trace.IndexOf("metadata"));
            Assert.True(trace.IndexOf("metadata") < trace.IndexOf("apply"));
        }

        [Theory]
        [InlineData("world", SevenDaysRegionOperationResult.WorldIdChanged)]
        [InlineData("version", SevenDaysRegionOperationResult.WorldVersionChanged)]
        [InlineData("unloaded", SevenDaysRegionOperationResult.TargetRegionUnavailable)]
        [InlineData("block", SevenDaysRegionOperationResult.BlockResourceInvalid)]
        public async Task Live_region_and_block_validation_rejects_before_evidence_or_world_write(
            string invalid,
            string expectedCode)
        {
            var metadata = new RecordingMetadataStore();
            var blobs = new RecordingBlobStore();
            var applyCalls = 0;
            var context = Context(
                worldId: invalid == "world" ? "world-2" : "world-1",
                worldVersion: invalid == "version" ? "world-v2" : "world-v1",
                targetRegionLoaded: invalid != "unloaded",
                blockInternalName: invalid == "block" ? null : "steelBlock",
                fillBlock: invalid == "block" ? null : Block(99),
                apply: (_, _) =>
                {
                    applyCalls++;
                    return true;
                });

            var result = await Handler(context, metadata, blobs).HandleAsync(
                Execution(
                    WorldOperationKind.FillRegion,
                    Target(blockInternalName: "steelBlock")),
                CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(0, blobs.WriteCalls);
            Assert.Null(metadata.CreatedDraft);
            Assert.Equal(0, applyCalls);
        }

        [Theory]
        [InlineData("cross-world", SevenDaysRegionOperationResult.SourceChangeSetWorldMismatch)]
        [InlineData("expired", SevenDaysRegionOperationResult.SourceChangeSetExpired)]
        [InlineData("corrupt", SevenDaysRegionOperationResult.SourceChangeSetInvalid)]
        public async Task Paste_rejects_invalid_source_change_sets_before_target_evidence_or_write(
            string invalid,
            string expectedCode)
        {
            var sourceRegion = Region(0, 0, 0, 1, 0, 0);
            var content = Snapshot(sourceRegion, Block(7), Block(8));
            var hash = Hash(content);
            var source = new WorldChangeSetDescriptor(
                "change-set-source",
                "copy-operation",
                invalid == "cross-world" ? "world-2" : "world-1",
                "world-v1",
                sourceRegion,
                hash,
                hash,
                "wcs-11111111111111111111111111111111",
                Now.AddDays(-1),
                invalid == "expired" ? Now : Now.AddDays(1));
            var metadata = new RecordingMetadataStore { Source = source };
            var blobs = new RecordingBlobStore
            {
                Source = invalid == "corrupt" ? new byte[] { 1, 2, 3 } : content
            };
            var applyCalls = 0;

            var result = await Handler(
                    Context(apply: (_, _) =>
                    {
                        applyCalls++;
                        return true;
                    }),
                    metadata,
                    blobs)
                .HandleAsync(
                    Execution(
                        WorldOperationKind.PasteRegion,
                        Target(volumeX: 2, sourceChangeSetId: "change-set-source")),
                    CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(0, blobs.WriteCalls);
            Assert.Null(metadata.CreatedDraft);
            Assert.Equal(0, applyCalls);
        }

        [Fact]
        public async Task Partial_batch_application_is_result_unknown_and_never_success()
        {
            var applied = 0;
            var context = Context(
                blockInternalName: "steelBlock",
                fillBlock: Block(99),
                apply: (_, _) => ++applied < 11);

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(),
                    new RecordingBlobStore())
                .HandleAsync(
                    Execution(
                        WorldOperationKind.FillRegion,
                        Target(volumeX: 20, blockInternalName: "steelBlock")),
                    CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysRegionOperationResult.ResultUnknown, result.ErrorCode);
            Assert.Equal("change-set-1", result.ChangeSetId);
            Assert.Equal(11, applied);
        }

        [Fact]
        public async Task Cancellation_is_observed_only_after_a_complete_safe_batch()
        {
            using var cancellation = new CancellationTokenSource();
            var applied = 0;
            var context = Context(
                blockInternalName: "steelBlock",
                fillBlock: Block(99),
                apply: (_, _) =>
                {
                    applied++;
                    if (applied == 1) cancellation.Cancel();
                    return true;
                });

            var result = await Handler(
                    context,
                    new RecordingMetadataStore(),
                    new RecordingBlobStore())
                .HandleAsync(
                    Execution(
                        WorldOperationKind.FillRegion,
                        Target(volumeX: 300, blockInternalName: "steelBlock")),
                    cancellation.Token);

            Assert.Equal(SevenDaysRegionOperationOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(WorldOperationBatchExecutor.MaximumBlocksPerBatch, applied);
        }

        [Fact]
        public async Task Batch_executor_enforces_frame_budget_and_monotonic_progress()
        {
            var ticks = 0L;
            var dispatches = 0;
            var processed = new List<long>();
            var progress = new List<long>();
            var executor = new WorldOperationBatchExecutor(
                (_, action, _, cancellationToken) =>
                {
                    Assert.False(cancellationToken.CanBeCanceled);
                    dispatches++;
                    return Task.FromResult(action());
                },
                () => ticks++ * 5,
                timestampFrequency: 1000);
            using var lease = await executor.TryEnterAsync(CancellationToken.None);
            Assert.NotNull(lease);

            var result = await executor.ExecuteAsync(
                totalBlocks: 3,
                () => WorldOperationBatchContext.Ready(index =>
                {
                    processed.Add(index);
                    return true;
                }),
                (current, _) => progress.Add(current),
                CancellationToken.None);

            Assert.Equal(WorldOperationBatchExecutionStatus.Completed, result.Status);
            Assert.Equal(new long[] { 0, 1, 2 }, processed);
            Assert.Equal(new long[] { 0, 1, 2, 3 }, progress);
            Assert.Equal(3, dispatches);
        }

        [Fact]
        public async Task Batch_executor_rejects_admission_beyond_fixed_capacity()
        {
            var executor = new WorldOperationBatchExecutor(
                (_, action, _, _) => Task.FromResult(action()));
            var first = await executor.TryEnterAsync(CancellationToken.None);
            Assert.NotNull(first);
            var secondTask = executor.TryEnterAsync(CancellationToken.None);
            var thirdTask = executor.TryEnterAsync(CancellationToken.None);
            var fourthTask = executor.TryEnterAsync(CancellationToken.None);

            var rejected = await executor.TryEnterAsync(CancellationToken.None);

            Assert.Null(rejected);
            first!.Dispose();
            var second = await secondTask;
            Assert.NotNull(second);
            second!.Dispose();
            var third = await thirdTask;
            Assert.NotNull(third);
            third!.Dispose();
            var fourth = await fourthTask;
            Assert.NotNull(fourth);
            fourth!.Dispose();
        }

        [Fact]
        public async Task Other_operation_kinds_are_rejected_by_the_closed_switch()
        {
            var dispatched = false;
            var handler = new SevenDaysRegionOperationHandler(
                (_, _, _, _) =>
                {
                    dispatched = true;
                    throw new InvalidOperationException("must not dispatch");
                },
                _ => throw new InvalidOperationException("must not capture context"),
                new RecordingMetadataStore(),
                new RecordingBlobStore(),
                () => Now);

            var result = await handler.HandleAsync(
                Execution(
                    WorldOperationKind.MoveEntity,
                    new WorldEntityOperationTarget(
                        "entity-1", 1, "entity-1", "type-1", null,
                        1, 2, 3, 4, 5, 6)),
                CancellationToken.None);

            Assert.Equal(SevenDaysRegionOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysRegionOperationResult.OperationKindNotSupported, result.ErrorCode);
            Assert.False(dispatched);
        }

        private static SevenDaysRegionOperationHandler Handler(
            SevenDaysRegionOperationContext context,
            IWorldChangeSetMetadataStore metadata,
            IWorldChangeSetBlobStore blobs,
            WorldOperationBatchDispatcher? dispatcher = null) =>
            new SevenDaysRegionOperationHandler(
                dispatcher ?? ((_, action, _, _) => Task.FromResult(action())),
                _ => context,
                metadata,
                blobs,
                () => Now);

        private static SevenDaysRegionOperationContext Context(
            string worldId = "world-1",
            string worldVersion = "world-v1",
            string? mapResourceVersion = "map-v1",
            bool targetRegionLoaded = true,
            string? blockInternalName = null,
            SevenDaysRegionBlock? fillBlock = null,
            Func<long, SevenDaysRegionBlock>? capture = null,
            Func<long, SevenDaysRegionBlock, bool>? apply = null) =>
            SevenDaysRegionOperationContext.Available(
                worldId,
                worldVersion,
                mapResourceVersion,
                minimumX: -1000,
                minimumY: 0,
                minimumZ: -1000,
                maximumX: 1000,
                maximumY: 255,
                maximumZ: 1000,
                targetRegionLoaded,
                blockInternalName,
                fillBlock,
                capture ?? (_ => Block(1)),
                apply ?? ((_, _) => true));

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
                    "Approved region operation",
                    kind != WorldOperationKind.CopyRegion,
                    target,
                    Now));

        private static WorldRegionOperationTarget Target(
            int volumeX = 1,
            string? sourceChangeSetId = null,
            string? blockInternalName = null) =>
            new WorldRegionOperationTarget(
                0, 1, 0,
                volumeX - 1, 1, 0,
                sourceChangeSetId,
                blockInternalName);

        private static SevenDaysRegionBlock Block(uint rawData) =>
            new SevenDaysRegionBlock(rawData, checked((int)rawData), unchecked((sbyte)rawData));

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

        private static byte[] Snapshot(WorldRegion region, params SevenDaysRegionBlock[] blocks)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(1);
                writer.Write(checked((int)region.Minimum.X));
                writer.Write(checked((int)region.Minimum.Y));
                writer.Write(checked((int)region.Minimum.Z));
                writer.Write(checked((int)region.Maximum.X));
                writer.Write(checked((int)region.Maximum.Y));
                writer.Write(checked((int)region.Maximum.Z));
                foreach (var block in blocks)
                {
                    writer.Write(block.RawData);
                    writer.Write(block.Damage);
                    writer.Write(block.Density);
                }
            }
            return stream.ToArray();
        }

        private static string Hash(byte[] content)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(content).Select(value => value.ToString("x2")));
        }

        private static bool IsMonotonic(IReadOnlyList<long> values)
        {
            for (var index = 1; index < values.Count; index++)
            {
                if (values[index] < values[index - 1]) return false;
            }
            return true;
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingMetadataStore : IWorldChangeSetMetadataStore
        {
            private readonly ICollection<string>? trace;

            public RecordingMetadataStore(ICollection<string>? trace = null) => this.trace = trace;

            public WorldChangeSetDraft? CreatedDraft { get; private set; }
            public string? MarkedChangeSetId { get; private set; }
            public string? MarkedAfterHash { get; private set; }
            public WorldChangeSetDescriptor? Source { get; set; }

            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft)
            {
                trace?.Add("metadata");
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

            public WorldChangeSetDescriptor Read(string changeSetId)
            {
                if (Source == null) throw new FileNotFoundException();
                Assert.Equal(Source.ChangeSetId, changeSetId);
                return Source;
            }

            public void MarkApplied(string changeSetId, string afterHash)
            {
                trace?.Add("mark");
                MarkedChangeSetId = changeSetId;
                MarkedAfterHash = afterHash;
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "SevenDays")]

        private sealed class RecordingBlobStore : IWorldChangeSetBlobStore
        {
            private readonly Action? beforeWrite;
            private readonly ICollection<string>? trace;

            public RecordingBlobStore(
                Action? beforeWrite = null,
                ICollection<string>? trace = null)
            {
                this.beforeWrite = beforeWrite;
                this.trace = trace;
            }

            public int WriteCalls { get; private set; }
            public string? LastWrittenResourceId { get; private set; }
            public byte[]? Source { get; set; }

            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft)
            {
                beforeWrite?.Invoke();
                trace?.Add("blob");
                WriteCalls++;
                LastWrittenResourceId = draft.StorageResourceId;
                Source ??= (byte[])draft.Content.Clone();
                return new WorldChangeSetBlobReceipt(
                    draft.StorageResourceId,
                    draft.ExpectedHash,
                    draft.Content.LongLength);
            }

            public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash)
            {
                if (Source == null) throw new FileNotFoundException();
                return new WorldChangeSetBlobReadResult(
                    storageResourceId,
                    expectedHash,
                    Source);
            }
        }
    }
}
