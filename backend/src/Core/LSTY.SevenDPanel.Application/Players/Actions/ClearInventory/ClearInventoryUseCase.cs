using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ClearInventoryUseCase
    {
        private static readonly TimeSpan DefaultTargetFreshness = TimeSpan.FromSeconds(30);
        private readonly IClearInventoryOperationStore store;
        private readonly IClearInventoryGateway gateway;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly TimeSpan targetFreshness;

        public ClearInventoryUseCase(
            IClearInventoryOperationStore store,
            IClearInventoryGateway gateway)
            : this(
                store,
                gateway,
                () => "clear-inventory-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow,
                DefaultTargetFreshness)
        {
        }

        internal ClearInventoryUseCase(
            IClearInventoryOperationStore store,
            IClearInventoryGateway gateway,
            Func<string> operationIdFactory,
            Func<DateTimeOffset> utcClock,
            TimeSpan targetFreshness)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            this.operationIdFactory = operationIdFactory ?? throw new ArgumentNullException(nameof(operationIdFactory));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            if (targetFreshness <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(targetFreshness));
            this.targetFreshness = targetFreshness;
        }

        public async Task<ClearInventoryResult> ExecuteAsync(
            ClearInventoryRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var createdAtUtc = UtcNow();
            var requestedOperationId = PlayerEvidenceValidation.RequireText(
                operationIdFactory(),
                "operationId");
            var operation = store.CreatePending(new ClearInventoryPendingIntent(
                requestedOperationId,
                request.OperatorId,
                request.Target,
                request.ClientRequestKey,
                request.CorrelationId,
                createdAtUtc,
                PlayerItemRemovalScope.BagOnly,
                request.DangerConfirmed));

            ValidateStoredOperation(operation);
            if (!string.Equals(operation.OperationId, requestedOperationId, StringComparison.Ordinal) ||
                operation.Status != PlayerActionStatus.Pending)
            {
                return FromStored(operation);
            }

            if (!request.DangerConfirmed)
                return Complete(operation.OperationId, request, ClearInventoryOperationStatus.Rejected,
                    "danger_confirmation_required", null, null);
            if (!IsFresh(request.Target, createdAtUtc))
                return Complete(operation.OperationId, request, ClearInventoryOperationStatus.Rejected,
                    "target_not_fresh", null, null);

            ClearInventoryPreparationResult preparation;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                preparation = await gateway.PrepareAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Complete(operation.OperationId, request, ClearInventoryOperationStatus.Cancelled,
                    "clear_inventory_cancelled", null, null);
            }
            catch
            {
                return Complete(operation.OperationId, request, ClearInventoryOperationStatus.Failed,
                    "before_inventory_snapshot_failed", null, null);
            }

            if (preparation.Status != ClearInventoryPreparationStatus.Ready)
            {
                var status = preparation.Status == ClearInventoryPreparationStatus.Rejected
                    ? ClearInventoryOperationStatus.Rejected
                    : ClearInventoryOperationStatus.Failed;
                return Complete(operation.OperationId, request, status,
                    preparation.FailureCode, null, null);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Complete(operation.OperationId, request, ClearInventoryOperationStatus.Cancelled,
                    "clear_inventory_cancelled", preparation.BeforeInventorySnapshotId, null);
            }

            if (!store.TryStart(operation.OperationId, UtcNow()))
            {
                return new ClearInventoryResult(
                    operation.OperationId,
                    ClearInventoryOperationStatus.Pending,
                    null,
                    preparation.BeforeInventorySnapshotId,
                    null,
                    false,
                    null);
            }

            ClearInventoryGatewayResult gatewayResult;
            try
            {
                gatewayResult = await gateway.ExecuteAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                gatewayResult = ClearInventoryGatewayResult.ResultUnknown("clear_inventory_result_unknown");
            }

            var terminalStatus = gatewayResult.Status switch
            {
                ClearInventoryGatewayStatus.Succeeded => ClearInventoryOperationStatus.Succeeded,
                ClearInventoryGatewayStatus.Rejected => ClearInventoryOperationStatus.Rejected,
                ClearInventoryGatewayStatus.Failed => ClearInventoryOperationStatus.Failed,
                ClearInventoryGatewayStatus.ResultUnknown => ClearInventoryOperationStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException()
            };
            return Complete(
                operation.OperationId,
                request,
                terminalStatus,
                gatewayResult.FailureCode,
                preparation.BeforeInventorySnapshotId,
                gatewayResult.AfterInventorySnapshotId);
        }

        private ClearInventoryResult Complete(
            string operationId,
            ClearInventoryRequest request,
            ClearInventoryOperationStatus status,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId)
        {
            var completion = new ClearInventoryOperationCompletion(
                operationId,
                status,
                UtcNow(),
                failureCode,
                beforeInventorySnapshotId,
                afterInventorySnapshotId);
            var persisted = false;
            try { persisted = store.TryComplete(completion); }
            catch { }
            return new ClearInventoryResult(
                operationId,
                status,
                failureCode,
                beforeInventorySnapshotId,
                afterInventorySnapshotId,
                persisted,
                status == ClearInventoryOperationStatus.Succeeded
                    ? request.ConfirmationSummary
                    : null);
        }

        private bool IsFresh(PlayerTargetStamp target, DateTimeOffset now)
        {
            var age = now - target.OnlineObservedAtUtc;
            return age >= TimeSpan.Zero && age <= targetFreshness;
        }

        private DateTimeOffset UtcNow() =>
            PlayerEvidenceValidation.RequireUtc(utcClock(), "utcNow");

        private static void ValidateStoredOperation(PlayerActionOperation operation)
        {
            if (operation == null) throw new InvalidOperationException("The operation store returned no operation.");
            if (!string.Equals(operation.OperationType, PlayerActionOperationTypes.ClearInventory, StringComparison.Ordinal))
                throw new InvalidOperationException("The operation store returned a different player action type.");
        }

        private static ClearInventoryResult FromStored(PlayerActionOperation operation)
        {
            var status = operation.Status.ToClearInventoryStatus();
            return new ClearInventoryResult(
                operation.OperationId,
                status,
                operation.FailureCode,
                operation.BeforeInventorySnapshotId,
                operation.AfterInventorySnapshotId,
                operation.CompletedAtUtc.HasValue,
                status == ClearInventoryOperationStatus.Succeeded
                    ? new ClearInventoryConfirmationSummary(operation.Target)
                    : null);
        }
    }
}
