using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.MapTiles;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class UndoWorldChangeSetUseCaseTests
    {
        [Fact]
        public void Valid_owned_change_set_enqueues_only_a_fixed_undo_target()
        {
            var fixture = new Fixture();
            var receipt = fixture.UseCase.Execute(fixture.Request());

            Assert.Equal("undo-operation", receipt.OperationId);
            var intent = Assert.IsType<WorldOperationIntent>(fixture.Bridge.Enqueued);
            Assert.Equal(WorldOperationKind.UndoChangeSet, intent.Kind);
            Assert.Equal("owner", intent.ActorSubject);
            Assert.Equal("world-1", intent.WorldId);
            Assert.Equal("world-v1", intent.WorldVersion);
            Assert.Null(intent.MapResourceVersion);
            Assert.Equal("undo-correlation", intent.CorrelationId);
            Assert.True(intent.IsReversible);
            var target = Assert.IsType<WorldRegionOperationTarget>(intent.Target);
            Assert.Equal("change-set-1", target.SourceChangeSetId);
            Assert.Null(target.BlockInternalName);
            Assert.Equal(1, target.MinimumX);
            Assert.Equal(4, target.MaximumZ);
            Assert.Equal(0, fixture.Metadata.MarkAppliedCalls);
            Assert.Equal(0, fixture.Blobs.WriteCalls);
        }

        [Fact]
        public async Task Preflight_exposes_only_the_required_hash_match_state_without_enqueuing()
        {
            var fixture = new Fixture();

            var result = await fixture.UseCase.PreflightAsync(
                fixture.Descriptor.SourceOperationId,
                Utc(),
                TestContext.Current.CancellationToken);

            Assert.Equal(fixture.Descriptor.AfterHash, result.AfterHash);
            Assert.Equal(fixture.Descriptor.AfterHash, result.CurrentRegionHash);
            Assert.True(result.CurrentHashMatches);
            Assert.Equal("ready", result.Status);
            Assert.Null(fixture.Bridge.Enqueued);
            Assert.DoesNotContain(
                typeof(UndoWorldChangeSetPreflight).GetProperties(),
                property => string.Equals(
                    property.Name,
                    "BeforeHash",
                    StringComparison.Ordinal));
        }

        [Fact]
        public async Task Handler_persists_rollback_evidence_and_applies_the_before_snapshot_in_batches()
        {
            var fixture = new HandlerFixture(257);

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);

            Assert.Equal(SevenDaysUndoOperationOutcome.Succeeded, result.Outcome);
            Assert.Equal("undo-change-set", result.ChangeSetId);
            Assert.Equal(257, result.Progress.Current);
            Assert.All(fixture.Current, block => Assert.Equal(1u, block.RawData));
            Assert.Equal(1, fixture.Blobs.WriteCalls);
            Assert.Equal(fixture.Source.AfterHash, fixture.Blobs.LastDraft!.ExpectedHash);
            Assert.Contains(
                fixture.Metadata.Marked,
                item => item.ChangeSetId == fixture.Source.ChangeSetId &&
                        item.AfterHash == fixture.Source.BeforeHash);
            Assert.Contains(
                fixture.Metadata.Marked,
                item => item.ChangeSetId == "undo-change-set" &&
                        item.AfterHash == fixture.Source.BeforeHash);
        }

        [Fact]
        public async Task Handler_rejects_changed_current_region_before_any_side_effect()
        {
            var fixture = new HandlerFixture(2);
            fixture.Current[0] = new SevenDaysRegionBlock(9, 0, 0);

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);

            Assert.Equal(SevenDaysUndoOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysUndoOperationResult.CurrentRegionChanged, result.ErrorCode);
            Assert.Equal(0, fixture.ApplyCalls);
            Assert.Equal(0, fixture.Blobs.WriteCalls);
        }

        [Fact]
        public async Task Handler_rejects_expired_corrupt_and_already_undone_change_sets()
        {
            var expired = new HandlerFixture(2);
            expired.SetSource(expired.Source with { ExpiresAtUtc = Utc() });
            var expiredResult = await expired.Handler.HandleAsync(expired.Execution(), CancellationToken.None);
            Assert.Equal(SevenDaysUndoOperationResult.ChangeSetExpired, expiredResult.ErrorCode);

            var corrupt = new HandlerFixture(2);
            corrupt.Blobs.CorruptRead = true;
            var corruptResult = await corrupt.Handler.HandleAsync(corrupt.Execution(), CancellationToken.None);
            Assert.Equal(SevenDaysUndoOperationResult.ChangeSetCorrupt, corruptResult.ErrorCode);

            var duplicate = new HandlerFixture(2);
            duplicate.SetSource(duplicate.Source with { AfterHash = duplicate.Source.BeforeHash });
            var duplicateResult = await duplicate.Handler.HandleAsync(duplicate.Execution(), CancellationToken.None);
            Assert.Equal(SevenDaysUndoOperationResult.AlreadyUndone, duplicateResult.ErrorCode);
            Assert.Equal(0, expired.ApplyCalls + corrupt.ApplyCalls + duplicate.ApplyCalls);
        }

        [Theory]
        [InlineData("resource-id")]
        [InlineData("content-hash")]
        public async Task Handler_rejects_forged_blob_identity_before_evidence_or_apply(string forged)
        {
            var fixture = new HandlerFixture(2);
            if (forged == "resource-id")
                fixture.Blobs.ReadStorageResourceId = "wcs-99999999999999999999999999999999";
            else
                fixture.Blobs.ReadContentHash = new string('f', 64);

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);

            Assert.Equal(SevenDaysUndoOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysUndoOperationResult.ChangeSetCorrupt, result.ErrorCode);
            Assert.Equal(0, fixture.Blobs.WriteCalls);
            Assert.Equal(0, fixture.ApplyCalls);
        }

        [Fact]
        public async Task Successful_undo_replay_is_rejected_without_new_evidence_or_apply()
        {
            var fixture = new HandlerFixture(2);
            var first = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);
            var writesAfterFirst = fixture.Blobs.WriteCalls;
            var appliesAfterFirst = fixture.ApplyCalls;

            var replay = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);

            Assert.Equal(SevenDaysUndoOperationOutcome.Succeeded, first.Outcome);
            Assert.Equal(SevenDaysUndoOperationOutcome.Rejected, replay.Outcome);
            Assert.Equal(SevenDaysUndoOperationResult.AlreadyUndone, replay.ErrorCode);
            Assert.Equal(writesAfterFirst, fixture.Blobs.WriteCalls);
            Assert.Equal(appliesAfterFirst, fixture.ApplyCalls);
        }

        [Fact]
        public async Task Source_change_set_competition_after_capture_is_rejected_before_evidence_or_apply()
        {
            var fixture = new HandlerFixture(2);
            fixture.Metadata.BeforeRead = count =>
            {
                if (count == 2)
                    fixture.Metadata.SetSource(
                        fixture.Source with { AfterHash = new string('d', 64) });
            };

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                CancellationToken.None);

            Assert.Equal(SevenDaysUndoOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysUndoOperationResult.ChangeSetInvalid, result.ErrorCode);
            Assert.Equal(0, fixture.Blobs.WriteCalls);
            Assert.Equal(0, fixture.ApplyCalls);
        }

        [Fact]
        public async Task Cancellation_at_a_safe_boundary_rolls_back_and_is_interrupted()
        {
            using var cancellation = new CancellationTokenSource();
            var fixture = new HandlerFixture(257);
            fixture.AfterDispatch = () =>
            {
                if (fixture.AppliedBefore == 256) cancellation.Cancel();
            };

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                cancellation.Token);

            Assert.Equal(SevenDaysUndoOperationOutcome.Interrupted, result.Outcome);
            Assert.Equal("undo-change-set", result.ChangeSetId);
            Assert.Equal(256, result.Progress.Current);
            Assert.All(fixture.Current, block => Assert.Equal(2u, block.RawData));
        }

        [Fact]
        public async Task Cancellation_at_a_safe_boundary_with_failed_rollback_is_rollback_failed()
        {
            using var cancellation = new CancellationTokenSource();
            var fixture = new HandlerFixture(257, failRollback: true);
            fixture.AfterDispatch = () =>
            {
                if (fixture.AppliedBefore == 256) cancellation.Cancel();
            };

            var result = await fixture.Handler.HandleAsync(
                fixture.Execution(),
                cancellation.Token);

            Assert.Equal(SevenDaysUndoOperationOutcome.RollbackFailed, result.Outcome);
            Assert.Equal(SevenDaysUndoOperationResult.RollbackFailed, result.ErrorCode);
            Assert.DoesNotContain("exception", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Job_handler_persists_rollback_failure_before_returning_completion()
        {
            using var cancellation = new CancellationTokenSource();
            var fixture = new HandlerFixture(257, failRollback: true);
            fixture.AfterDispatch = () =>
            {
                if (fixture.AppliedBefore == 256) cancellation.Cancel();
            };
            var execution = fixture.Execution();
            var executions = new RecordingExecutionStore(execution);
            var root = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-undo-job-handler-" + Guid.NewGuid().ToString("N"));
            var staging = Path.Combine(root, "staging");
            var published = Path.Combine(root, "published");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(published);
            try
            {
                var handler = new WorldOperationJobHandler(
                    executions,
                    new SevenDaysMapWorldOperationHandler(staging),
                    new LocalMapResourcePublisher(staging, published),
                    new SevenDaysRegionOperationHandler(fixture.Metadata, fixture.Blobs),
                    new SevenDaysBlockPrefabOperationHandler(fixture.Metadata, fixture.Blobs),
                    new SevenDaysEntityMaintenanceHandler(),
                    new SevenDaysRuntimeMaintenanceHandler(),
                    fixture.Handler,
                    Utc);

                var result = await handler.ExecuteAsync(execution.JobId, cancellation.Token);

                Assert.Equal(WorldOperationStatus.RollbackFailed, result.Status);
                Assert.Equal(SevenDaysUndoOperationResult.RollbackFailed, result.ErrorCode);
                Assert.Equal(1, executions.RollbackFailureCalls);
                Assert.Equal(execution.JobId, executions.RollbackFailureJobId);
                Assert.Equal(SevenDaysUndoOperationResult.RollbackFailed, executions.RollbackFailureCode);
                Assert.Equal(Utc(), executions.RollbackFailedAtUtc);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static DateTimeOffset Utc() =>
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        private sealed class Fixture
        {
            internal Fixture()
            {
                var region = new WorldRegion(
                    new WorldCoordinate(1, 2, 3),
                    new WorldCoordinate(2, 3, 4));
                var content = new byte[] { 1, 2, 3 };
                Descriptor = new WorldChangeSetDescriptor(
                    "change-set-1",
                    "source-operation",
                    "world-1",
                    "world-v1",
                    region,
                    Hash(content),
                    new string('b', 64),
                    "wcs-11111111111111111111111111111111",
                    Utc().AddDays(-1),
                    Utc().AddDays(1));
                Metadata = new RecordingMetadataStore(Descriptor);
                Blobs = new RecordingBlobStore(Descriptor, content);
                Bridge = new RecordingBridge(SourceRecord(Descriptor));
                Preflight = new RecordingPreflightGateway(Descriptor.AfterHash);
                UseCase = new UndoWorldChangeSetUseCase(
                    Bridge,
                    Metadata,
                    Blobs,
                    Preflight);
            }

            internal WorldChangeSetDescriptor Descriptor { get; }
            internal RecordingBridge Bridge { get; }
            internal RecordingMetadataStore Metadata { get; }
            internal RecordingBlobStore Blobs { get; }
            internal RecordingPreflightGateway Preflight { get; }
            internal UndoWorldChangeSetUseCase UseCase { get; }

            internal UndoWorldChangeSetRequest Request() =>
                new UndoWorldChangeSetRequest(
                    "owner",
                    "source-operation",
                    "change-set-1",
                    "world-1",
                    "world-v1",
                    Descriptor.AfterHash,
                    "undo-correlation",
                    true,
                    true,
                    Utc());

            private static WorldOperationRecord SourceRecord(WorldChangeSetDescriptor descriptor) =>
                new WorldOperationRecord(
                    descriptor.SourceOperationId,
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "owner",
                    WorldOperationKind.ClearRegion,
                    descriptor.WorldId,
                    descriptor.WorldVersion,
                    null,
                    "source-correlation",
                    "Clear region",
                    true,
                    descriptor.ChangeSetId,
                    WorldOperationStatus.Succeeded,
                    new WorldOperationProgress(8, 8),
                    null,
                    Utc().AddDays(-1),
                    Utc().AddDays(-1),
                    Utc().AddDays(-1));
        }

        private sealed class RecordingPreflightGateway : IWorldChangeSetPreflightGateway
        {
            private readonly string currentHash;

            internal RecordingPreflightGateway(string currentHash) =>
                this.currentHash = currentHash;

            public Task<WorldChangeSetRuntimeHashResult> ReadCurrentRegionHashAsync(
                WorldChangeSetDescriptor descriptor,
                CancellationToken cancellationToken) =>
                Task.FromResult(WorldChangeSetRuntimeHashResult.Available(currentHash));
        }

        private sealed class RecordingBridge : IWorldOperationJobBridge
        {
            private readonly WorldOperationRecord source;

            internal RecordingBridge(WorldOperationRecord source) => this.source = source;

            internal WorldOperationIntent? Enqueued { get; private set; }

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                Enqueued = intent;
                return new WorldOperationReceipt(
                    "undo-operation",
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId)
            {
                Assert.Equal(source.OperationId, operationId);
                return source;
            }

            public WorldOperationPage Query(WorldOperationQuery query) =>
                new WorldOperationPage(Array.Empty<WorldOperationRecord>(), null);

            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }

        private sealed class RecordingMetadataStore : IWorldChangeSetMetadataStore
        {
            private readonly WorldChangeSetDescriptor descriptor;

            internal RecordingMetadataStore(WorldChangeSetDescriptor descriptor) =>
                this.descriptor = descriptor;

            internal int MarkAppliedCalls { get; private set; }

            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft) =>
                throw new NotSupportedException();

            public WorldChangeSetDescriptor Read(string changeSetId)
            {
                Assert.Equal(descriptor.ChangeSetId, changeSetId);
                return descriptor;
            }

            public void MarkApplied(string changeSetId, string afterHash) => MarkAppliedCalls++;
        }

        private sealed class RecordingBlobStore : IWorldChangeSetBlobStore
        {
            private readonly WorldChangeSetDescriptor descriptor;
            private readonly byte[] content;

            internal RecordingBlobStore(WorldChangeSetDescriptor descriptor, byte[] content)
            {
                this.descriptor = descriptor;
                this.content = content;
            }

            internal int WriteCalls { get; private set; }

            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft)
            {
                WriteCalls++;
                throw new NotSupportedException();
            }

            public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash) =>
                new WorldChangeSetBlobReadResult(storageResourceId, expectedHash, content);
        }

        private sealed class HandlerFixture
        {
            private readonly bool failRollback;

            internal HandlerFixture(int volume, bool failRollback = false)
            {
                this.failRollback = failRollback;
                Region = new WorldRegion(
                    new WorldCoordinate(0, 0, 0),
                    new WorldCoordinate(volume - 1, 0, 0));
                var before = Snapshot(Region, 1);
                Current = new SevenDaysRegionBlock[volume];
                for (var index = 0; index < Current.Length; index++)
                    Current[index] = new SevenDaysRegionBlock(2, 0, 0);
                var after = Snapshot(Region, 2);
                Source = new WorldChangeSetDescriptor(
                    "change-set-1",
                    "source-operation",
                    "world-1",
                    "world-v1",
                    Region,
                    Hash(before),
                    Hash(after),
                    "wcs-11111111111111111111111111111111",
                    Utc().AddDays(-1),
                    Utc().AddDays(1));
                Metadata = new HandlerMetadataStore(Source);
                Blobs = new HandlerBlobStore(Source, before);
                Handler = new SevenDaysUndoOperationHandler(
                    Dispatch,
                    _ => SevenDaysRegionOperationContext.Available(
                        "world-1",
                        "world-v1",
                        null,
                        0,
                        0,
                        0,
                        volume - 1,
                        0,
                        0,
                        true,
                        null,
                        null,
                        index => Current[checked((int)index)],
                        Apply),
                    Metadata,
                    Blobs,
                    Utc,
                    () => "wcs-22222222222222222222222222222222");
            }

            internal WorldRegion Region { get; }
            internal WorldChangeSetDescriptor Source { get; private set; }
            internal SevenDaysRegionBlock[] Current { get; }
            internal HandlerMetadataStore Metadata { get; }
            internal HandlerBlobStore Blobs { get; }
            internal SevenDaysUndoOperationHandler Handler { get; }
            internal int ApplyCalls { get; private set; }
            internal int AppliedBefore { get; private set; }
            internal Action? AfterDispatch { get; set; }

            internal void SetSource(WorldChangeSetDescriptor descriptor)
            {
                Source = descriptor;
                Metadata.SetSource(descriptor);
                Blobs.SetSource(descriptor);
            }

            internal WorldOperationExecutionRecord Execution() =>
                new WorldOperationExecutionRecord(
                    "undo-operation",
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    new WorldOperationIntent(
                        "owner",
                        WorldOperationKind.UndoChangeSet,
                        "world-1",
                        "world-v1",
                        null,
                        "undo-correlation",
                        "Undo change set change-set-1",
                        true,
                        new WorldRegionOperationTarget(
                            0, 0, 0, Current.Length - 1, 0, 0,
                            "change-set-1", null),
                        Utc()));

            private Task<WorldOperationBatchStepResult> Dispatch(
                string name,
                Func<WorldOperationBatchStepResult> action,
                TimeSpan timeout,
                CancellationToken cancellationToken)
            {
                var result = action();
                AfterDispatch?.Invoke();
                return Task.FromResult(result);
            }

            private bool Apply(long index, SevenDaysRegionBlock desired)
            {
                ApplyCalls++;
                if (desired.RawData == 1) AppliedBefore++;
                if (failRollback && desired.RawData == 2 && AppliedBefore != 0)
                    return false;
                Current[checked((int)index)] = desired;
                return true;
            }
        }

        private sealed class HandlerMetadataStore : IWorldChangeSetMetadataStore
        {
            private WorldChangeSetDescriptor source;
            private WorldChangeSetDescriptor? undo;
            private int readCalls;

            internal HandlerMetadataStore(WorldChangeSetDescriptor source) => this.source = source;

            internal List<(string ChangeSetId, string AfterHash)> Marked { get; } =
                new List<(string ChangeSetId, string AfterHash)>();
            internal Action<int>? BeforeRead { get; set; }

            internal void SetSource(WorldChangeSetDescriptor descriptor) => source = descriptor;

            public WorldChangeSetDescriptor Create(WorldChangeSetDraft draft)
            {
                undo = new WorldChangeSetDescriptor(
                    "undo-change-set",
                    draft.SourceOperationId,
                    draft.WorldId,
                    draft.WorldVersion,
                    draft.Region,
                    draft.BeforeHash,
                    draft.AfterHash,
                    draft.StorageResourceId,
                    draft.CreatedAtUtc,
                    draft.ExpiresAtUtc);
                return undo;
            }

            public WorldChangeSetDescriptor Read(string changeSetId)
            {
                BeforeRead?.Invoke(++readCalls);
                return changeSetId == source.ChangeSetId
                    ? source
                    : undo ?? throw new KeyNotFoundException();
            }

            public void MarkApplied(string changeSetId, string afterHash)
            {
                Marked.Add((changeSetId, afterHash));
                if (changeSetId == source.ChangeSetId) source = source with { AfterHash = afterHash };
                if (undo != null && changeSetId == undo.ChangeSetId) undo = undo with { AfterHash = afterHash };
            }
        }

        private sealed class HandlerBlobStore : IWorldChangeSetBlobStore
        {
            private WorldChangeSetDescriptor source;
            private readonly byte[] before;

            internal HandlerBlobStore(WorldChangeSetDescriptor source, byte[] before)
            {
                this.source = source;
                this.before = before;
            }

            internal bool CorruptRead { get; set; }
            internal string? ReadStorageResourceId { get; set; }
            internal string? ReadContentHash { get; set; }
            internal int WriteCalls { get; private set; }
            internal WorldChangeSetBlobDraft? LastDraft { get; private set; }

            internal void SetSource(WorldChangeSetDescriptor descriptor) => source = descriptor;

            public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft)
            {
                WriteCalls++;
                LastDraft = draft;
                return new WorldChangeSetBlobReceipt(
                    draft.StorageResourceId,
                    draft.ExpectedHash,
                    draft.Content.LongLength);
            }

            public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash) =>
                new WorldChangeSetBlobReadResult(
                    ReadStorageResourceId ?? source.StorageResourceId,
                    ReadContentHash ?? source.BeforeHash,
                    CorruptRead ? new byte[] { 9 } : before);
        }

        private sealed class RecordingExecutionStore : IWorldOperationExecutionStore
        {
            private readonly WorldOperationExecutionRecord execution;

            internal RecordingExecutionStore(WorldOperationExecutionRecord execution) =>
                this.execution = execution;

            internal int RollbackFailureCalls { get; private set; }
            internal Guid RollbackFailureJobId { get; private set; }
            internal string? RollbackFailureCode { get; private set; }
            internal DateTimeOffset RollbackFailedAtUtc { get; private set; }

            public WorldOperationExecutionRecord ReadForExecution(Guid jobId)
            {
                Assert.Equal(execution.JobId, jobId);
                return execution;
            }

            public void MarkRollbackFailed(
                Guid jobId,
                string errorCode,
                DateTimeOffset failedAtUtc)
            {
                RollbackFailureCalls++;
                RollbackFailureJobId = jobId;
                RollbackFailureCode = errorCode;
                RollbackFailedAtUtc = failedAtUtc;
            }
        }

        private static byte[] Snapshot(WorldRegion region, uint rawData)
        {
            var content = RegionSnapshot.Create(region);
            for (long index = 0; index < region.Volume; index++)
                RegionSnapshot.WriteBlock(content, index, new SevenDaysRegionBlock(rawData, 0, 0));
            return content;
        }

        private static string Hash(byte[] content)
        {
            using var algorithm = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(content)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
