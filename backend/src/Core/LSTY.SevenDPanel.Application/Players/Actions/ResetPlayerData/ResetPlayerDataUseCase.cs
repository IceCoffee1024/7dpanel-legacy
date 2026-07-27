using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ResetPlayerDataUseCase
    {
        private static readonly TimeSpan DefaultTargetFreshness = TimeSpan.FromSeconds(30);
        private readonly IResetPlayerDataOperationStore store;
        private readonly IResetPlayerDataGateway gateway;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly TimeSpan targetFreshness;

        public ResetPlayerDataUseCase(
            IResetPlayerDataOperationStore store,
            IResetPlayerDataGateway gateway)
            : this(
                store,
                gateway,
                () => "reset-player-data-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow,
                DefaultTargetFreshness)
        {
        }

        internal ResetPlayerDataUseCase(
            IResetPlayerDataOperationStore store,
            IResetPlayerDataGateway gateway,
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

        public async Task<ResetPlayerDataResult> ExecuteAsync(
            ResetPlayerDataRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var createdAtUtc = UtcNow();
            var requestedOperationId = PlayerEvidenceValidation.RequireText(
                operationIdFactory(),
                "operationId");

            if (!request.DangerConfirmed)
            {
                return CreatePendingAndComplete(
                    requestedOperationId,
                    request,
                    createdAtUtc,
                    null,
                    null,
                    ResetPlayerDataOperationStatus.Rejected,
                    "strong_confirmation_required");
            }
            if (!IsFresh(request.Target, createdAtUtc))
            {
                return CreatePendingAndComplete(
                    requestedOperationId,
                    request,
                    createdAtUtc,
                    null,
                    null,
                    ResetPlayerDataOperationStatus.Rejected,
                    "target_not_fresh");
            }

            ResetPlayerDataPreparationResult preparation;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                preparation = await gateway.PrepareAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return CreatePendingAndComplete(
                    requestedOperationId,
                    request,
                    createdAtUtc,
                    null,
                    null,
                    ResetPlayerDataOperationStatus.Cancelled,
                    "reset_player_data_cancelled");
            }
            catch
            {
                return CreatePendingAndComplete(
                    requestedOperationId,
                    request,
                    createdAtUtc,
                    null,
                    null,
                    ResetPlayerDataOperationStatus.Failed,
                    "complete_before_snapshot_failed");
            }

            var intent = PendingIntent(
                requestedOperationId,
                request,
                createdAtUtc,
                preparation.BeforeInventorySnapshotId,
                preparation.BeforeSkillSnapshotId);
            var operation = store.CreatePending(intent);
            ValidateStoredOperation(operation);
            if (!string.Equals(operation.OperationId, requestedOperationId, StringComparison.Ordinal) ||
                operation.Status != PlayerActionStatus.Pending)
            {
                return FromStored(operation);
            }

            if (preparation.Status != ResetPlayerDataPreparationStatus.Ready)
            {
                var status = preparation.Status == ResetPlayerDataPreparationStatus.Rejected
                    ? ResetPlayerDataOperationStatus.Rejected
                    : ResetPlayerDataOperationStatus.Failed;
                return Complete(
                    operation.OperationId,
                    request,
                    status,
                    preparation.FailureCode,
                    intent.BeforeInventorySnapshotId,
                    intent.BeforeSkillSnapshotId);
            }
            if (!intent.HasCompletePreparationEvidence)
            {
                return Complete(
                    operation.OperationId,
                    request,
                    ResetPlayerDataOperationStatus.Rejected,
                    "complete_before_snapshot_unavailable",
                    intent.BeforeInventorySnapshotId,
                    intent.BeforeSkillSnapshotId);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Complete(
                    operation.OperationId,
                    request,
                    ResetPlayerDataOperationStatus.Cancelled,
                    "reset_player_data_cancelled",
                    intent.BeforeInventorySnapshotId,
                    intent.BeforeSkillSnapshotId);
            }

            if (!store.TryStart(operation.OperationId, UtcNow()))
            {
                return new ResetPlayerDataResult(
                    operation.OperationId,
                    ResetPlayerDataOperationStatus.Pending,
                    null,
                    intent.BeforeInventorySnapshotId,
                    intent.BeforeSkillSnapshotId,
                    false,
                    null);
            }

            ResetPlayerDataGatewayResult gatewayResult;
            try
            {
                gatewayResult = await gateway.ExecuteAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                gatewayResult = ResetPlayerDataGatewayResult.ResultUnknown(
                    "reset_player_data_result_unknown");
            }

            var terminalStatus = gatewayResult.Status switch
            {
                ResetPlayerDataGatewayStatus.Succeeded => ResetPlayerDataOperationStatus.Succeeded,
                ResetPlayerDataGatewayStatus.Rejected => ResetPlayerDataOperationStatus.Rejected,
                ResetPlayerDataGatewayStatus.Failed => ResetPlayerDataOperationStatus.Failed,
                ResetPlayerDataGatewayStatus.ResultUnknown => ResetPlayerDataOperationStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException()
            };
            return Complete(
                operation.OperationId,
                request,
                terminalStatus,
                gatewayResult.FailureCode,
                intent.BeforeInventorySnapshotId,
                intent.BeforeSkillSnapshotId);
        }

        private ResetPlayerDataResult CreatePendingAndComplete(
            string operationId,
            ResetPlayerDataRequest request,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            ResetPlayerDataOperationStatus status,
            string failureCode)
        {
            var operation = store.CreatePending(PendingIntent(
                operationId,
                request,
                createdAtUtc,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId));
            ValidateStoredOperation(operation);
            if (!string.Equals(operation.OperationId, operationId, StringComparison.Ordinal) ||
                operation.Status != PlayerActionStatus.Pending)
            {
                return FromStored(operation);
            }
            return Complete(
                operation.OperationId,
                request,
                status,
                failureCode,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId);
        }

        private static ResetPlayerDataPendingIntent PendingIntent(
            string operationId,
            ResetPlayerDataRequest request,
            DateTimeOffset createdAtUtc,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId) =>
            new ResetPlayerDataPendingIntent(
                operationId,
                request.OperatorId,
                request.Target,
                request.ClientRequestKey,
                request.CorrelationId,
                createdAtUtc,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId,
                request.DangerConfirmed);

        private ResetPlayerDataResult Complete(
            string operationId,
            ResetPlayerDataRequest request,
            ResetPlayerDataOperationStatus status,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId)
        {
            var completion = new ResetPlayerDataOperationCompletion(
                operationId,
                status,
                UtcNow(),
                failureCode,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId);
            var persisted = false;
            try { persisted = store.TryComplete(completion); }
            catch { }
            return new ResetPlayerDataResult(
                operationId,
                status,
                failureCode,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId,
                persisted,
                status == ResetPlayerDataOperationStatus.Succeeded
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
            if (operation == null)
                throw new InvalidOperationException("The operation store returned no operation.");
            if (!string.Equals(
                    operation.OperationType,
                    PlayerActionOperationTypes.ResetPlayerData,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The operation store returned a different player action type.");
            }
        }

        private static ResetPlayerDataResult FromStored(PlayerActionOperation operation)
        {
            var status = operation.Status.ToResetPlayerDataStatus();
            return new ResetPlayerDataResult(
                operation.OperationId,
                status,
                operation.FailureCode,
                operation.BeforeInventorySnapshotId,
                operation.BeforeSkillSnapshotId,
                operation.CompletedAtUtc.HasValue,
                status == ResetPlayerDataOperationStatus.Succeeded
                    ? new ResetPlayerDataConfirmationSummary(operation.Target)
                    : null);
        }
    }
}
