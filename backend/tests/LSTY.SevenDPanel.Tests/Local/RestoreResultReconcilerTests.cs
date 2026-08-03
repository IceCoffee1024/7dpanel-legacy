using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Local")]
    public sealed class RestoreResultReconcilerTests
    {
        [Theory]
        [InlineData(RestoreExecutionStage.Applied, JobStatus.Succeeded, null)]
        [InlineData(RestoreExecutionStage.RolledBack, JobStatus.Failed, RestoreResultReconciler.ApplyFailedRolledBackError)]
        [InlineData(RestoreExecutionStage.RollbackFailed, JobStatus.ResultUnknown, RestoreResultReconciler.RollbackFailedError)]
        [InlineData(RestoreExecutionStage.Prepared, JobStatus.ResultUnknown, RestoreResultReconciler.ResultUnknownError)]
        public void Receipt_stage_maps_to_one_terminal_merge(
            RestoreExecutionStage stage,
            JobStatus expectedStatus,
            string? expectedError)
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            var marker = JsonPendingRestoreStoreTests.CreateMarker();
            store.WriteReceipt(RestoreResultReceipt.FromMarker(marker, stage));
            var mergeStore = new RecordingMergeStore();
            var completedAt = new DateTimeOffset(2026, 7, 27, 4, 5, 6, TimeSpan.Zero);
            var reconciler = new RestoreResultReconciler(store, mergeStore, () => completedAt);

            Assert.True(reconciler.Reconcile());

            var merged = Assert.Single(mergeStore.Items);
            Assert.Equal(marker.JobSnapshot.JobId, merged.Snapshot.JobId);
            Assert.Equal(marker.ArtifactId, merged.Payload.BackupId);
            Assert.Equal(marker.BackupKind, merged.Payload.BackupKind);
            Assert.True(merged.Payload.RestartAfterStage);
            Assert.Equal(expectedStatus, merged.Status);
            Assert.Equal(completedAt, merged.Completion.CompletedAtUtc);
            Assert.Equal(expectedError, merged.Completion.ErrorCode);
            Assert.Null(store.ReadReceipt());
            Assert.False(reconciler.Reconcile());
            Assert.Single(mergeStore.Items);
        }

        [Fact]
        public void Failed_merge_keeps_the_receipt_for_an_idempotent_retry()
        {
            using var directories = new TestDirectories();
            var store = new JsonPendingRestoreStore(directories.CreateRoots());
            var marker = JsonPendingRestoreStoreTests.CreateMarker();
            store.WriteReceipt(RestoreResultReceipt.FromMarker(marker, RestoreExecutionStage.Applied));
            var mergeStore = new RecordingMergeStore { Failure = new InvalidOperationException("database unavailable") };
            var reconciler = new RestoreResultReconciler(
                store,
                mergeStore,
                () => new DateTimeOffset(2026, 7, 27, 4, 5, 6, TimeSpan.Zero));

            Assert.Throws<InvalidOperationException>(() => reconciler.Reconcile());

            Assert.NotNull(store.ReadReceipt());
            mergeStore.Failure = null;
            Assert.True(reconciler.Reconcile());
            Assert.Null(store.ReadReceipt());
            Assert.Equal(2, mergeStore.Calls);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingMergeStore : IRestoreResultMergeStore
        {
            public List<MergedResult> Items { get; } = new List<MergedResult>();
            public Exception? Failure { get; set; }
            public int Calls { get; private set; }

            public void MergeOnce(
                RestoreMergeJobSnapshot snapshot,
                RestorePayload payload,
                JobStatus status,
                JobCompletion completion)
            {
                Calls++;
                if (Failure != null) throw Failure;
                Items.Add(new MergedResult(snapshot, payload, status, completion));
            }
        }

        private sealed record MergedResult(
            RestoreMergeJobSnapshot Snapshot,
            RestorePayload Payload,
            JobStatus Status,
            JobCompletion Completion);
    }
}
