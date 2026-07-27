using System;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed class RestoreResultReconciler
    {
        public const string ApplyFailedRolledBackError = "restore_apply_failed_rolled_back";
        public const string RollbackFailedError = "restore_rollback_failed";
        public const string ResultUnknownError = "restore_result_unknown";

        private readonly JsonPendingRestoreStore store;
        private readonly IRestoreResultMergeStore mergeStore;
        private readonly Func<DateTimeOffset> utcNow;

        public RestoreResultReconciler(
            JsonPendingRestoreStore store,
            IRestoreResultMergeStore mergeStore,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.mergeStore = mergeStore ?? throw new ArgumentNullException(nameof(mergeStore));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public bool Reconcile()
        {
            var receipt = store.ReadReceipt();
            if (receipt == null) return false;
            var (status, errorCode) = receipt.Stage switch
            {
                RestoreExecutionStage.Applied => (JobStatus.Succeeded, (string?)null),
                RestoreExecutionStage.RolledBack => (JobStatus.Failed, ApplyFailedRolledBackError),
                RestoreExecutionStage.RollbackFailed => (JobStatus.ResultUnknown, RollbackFailedError),
                RestoreExecutionStage.Prepared => (JobStatus.ResultUnknown, ResultUnknownError),
                _ => throw new RestoreStateException(JsonPendingRestoreStore.ReceiptInvalidError)
            };
            var completedAtUtc = utcNow();
            if (completedAtUtc.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("restore_reconciliation_clock_not_utc");
            mergeStore.MergeOnce(
                new RestoreMergeJobSnapshot(
                    receipt.JobSnapshot.JobId,
                    receipt.JobSnapshot.JobKind,
                    receipt.JobSnapshot.JobStatus,
                    receipt.JobSnapshot.ActorSubject,
                    receipt.JobSnapshot.IdempotencyKey,
                    receipt.JobSnapshot.CorrelationId,
                    receipt.JobSnapshot.CreatedAtUtc),
                new RestorePayload(
                    receipt.ArtifactId,
                    receipt.BackupKind,
                    true),
                status,
                new JobCompletion(completedAtUtc, null, errorCode));
            store.DeleteReceipt(receipt.JobSnapshot.JobId);
            return true;
        }
    }
}
