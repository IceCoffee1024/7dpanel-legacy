using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application.Rewards
{
    public interface IRewardDeliveryJournal
    {
        void RecordDeliveryOperation(
            string grantOperationId,
            string operationEntryId,
            string deliveryOperationId,
            DateTimeOffset occurredAtUtc);
    }

    public interface IRewardStore : IRewardDeliveryJournal
    {
        RewardPackageSnapshot SavePackage(
            RewardPackageDraft package,
            DateTimeOffset occurredAtUtc);

        RewardPackageSnapshot GetPackage(string packageId);

        GrantCreationResult GetOrCreateGrant(GrantOperationDraft operation);

        GrantOperationSnapshot GetGrant(string operationId);

        bool TryStartDispatch(
            string operationId,
            long expectedRowVersion,
            DateTimeOffset occurredAtUtc);

        bool TryResolveDispatch(GrantDispatchResolution resolution);

        bool TryMarkPendingReconciliation(
            string operationId,
            string? errorCode,
            DateTimeOffset occurredAtUtc);

        IReadOnlyList<GrantOperationSnapshot> ListPendingReconciliation(int take);

        bool TryConfirmReconciled(
            string operationId,
            long expectedRowVersion,
            string actorId,
            string correlationId,
            string? ledgerTransactionId,
            DateTimeOffset occurredAtUtc);

        bool TryMarkRefunded(
            string operationId,
            long expectedRowVersion,
            string refundLedgerTransactionId,
            string correlationId,
            DateTimeOffset occurredAtUtc);
    }
}
