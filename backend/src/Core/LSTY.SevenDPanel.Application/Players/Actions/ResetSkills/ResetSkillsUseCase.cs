using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class ResetSkillsUseCase
    {
        private static readonly TimeSpan DefaultTargetFreshness = TimeSpan.FromSeconds(30);
        private readonly IResetSkillsOperationStore store;
        private readonly IResetSkillsGateway gateway;
        private readonly Func<string> operationIdFactory;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly TimeSpan targetFreshness;

        public ResetSkillsUseCase(
            IResetSkillsOperationStore store,
            IResetSkillsGateway gateway)
            : this(
                store,
                gateway,
                () => "reset-skills-" + Guid.NewGuid().ToString("N"),
                () => DateTimeOffset.UtcNow,
                DefaultTargetFreshness)
        {
        }

        internal ResetSkillsUseCase(
            IResetSkillsOperationStore store,
            IResetSkillsGateway gateway,
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

        public async Task<ResetSkillsResult> ExecuteAsync(
            ResetSkillsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var createdAtUtc = UtcNow();
            var requestedOperationId = PlayerEvidenceValidation.RequireText(
                operationIdFactory(),
                "operationId");
            var operation = store.CreatePending(new ResetSkillsPendingIntent(
                requestedOperationId,
                request.OperatorId,
                request.Target,
                request.ClientRequestKey,
                request.CorrelationId,
                createdAtUtc,
                request.DangerConfirmed));

            ValidateStoredOperation(operation);
            if (!string.Equals(operation.OperationId, requestedOperationId, StringComparison.Ordinal) ||
                operation.Status != PlayerActionStatus.Pending)
            {
                return FromStored(operation);
            }

            if (!request.DangerConfirmed)
                return Complete(operation.OperationId, request, ResetSkillsOperationStatus.Rejected,
                    "danger_confirmation_required", null, null);
            if (!IsFresh(request.Target, createdAtUtc))
                return Complete(operation.OperationId, request, ResetSkillsOperationStatus.Rejected,
                    "target_not_fresh", null, null);

            ResetSkillsPreparationResult preparation;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                preparation = await gateway.PrepareAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Complete(operation.OperationId, request, ResetSkillsOperationStatus.Cancelled,
                    "reset_skills_cancelled", null, null);
            }
            catch
            {
                return Complete(operation.OperationId, request, ResetSkillsOperationStatus.Failed,
                    "before_skill_snapshot_failed", null, null);
            }

            if (preparation.Status != ResetSkillsPreparationStatus.Ready)
            {
                var status = preparation.Status == ResetSkillsPreparationStatus.Rejected
                    ? ResetSkillsOperationStatus.Rejected
                    : ResetSkillsOperationStatus.Failed;
                return Complete(operation.OperationId, request, status,
                    preparation.FailureCode, null, null);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                return Complete(operation.OperationId, request, ResetSkillsOperationStatus.Cancelled,
                    "reset_skills_cancelled", preparation.BeforeSkillSnapshotId, null);
            }

            if (!store.TryStart(operation.OperationId, UtcNow()))
            {
                return new ResetSkillsResult(
                    operation.OperationId,
                    ResetSkillsOperationStatus.Pending,
                    null,
                    preparation.BeforeSkillSnapshotId,
                    null,
                    false,
                    null);
            }

            ResetSkillsGatewayResult gatewayResult;
            try
            {
                gatewayResult = await gateway.ExecuteAsync(request.Target, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                gatewayResult = ResetSkillsGatewayResult.ResultUnknown("reset_skills_result_unknown");
            }

            var terminalStatus = gatewayResult.Status switch
            {
                ResetSkillsGatewayStatus.Succeeded => ResetSkillsOperationStatus.Succeeded,
                ResetSkillsGatewayStatus.Rejected => ResetSkillsOperationStatus.Rejected,
                ResetSkillsGatewayStatus.Failed => ResetSkillsOperationStatus.Failed,
                ResetSkillsGatewayStatus.ResultUnknown => ResetSkillsOperationStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException()
            };
            return Complete(
                operation.OperationId,
                request,
                terminalStatus,
                gatewayResult.FailureCode,
                preparation.BeforeSkillSnapshotId,
                gatewayResult.AfterSkillSnapshotId);
        }

        private ResetSkillsResult Complete(
            string operationId,
            ResetSkillsRequest request,
            ResetSkillsOperationStatus status,
            string? failureCode,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId)
        {
            var completion = new ResetSkillsOperationCompletion(
                operationId,
                status,
                UtcNow(),
                failureCode,
                beforeSkillSnapshotId,
                afterSkillSnapshotId);
            var persisted = false;
            try { persisted = store.TryComplete(completion); }
            catch { }
            return new ResetSkillsResult(
                operationId,
                status,
                failureCode,
                beforeSkillSnapshotId,
                afterSkillSnapshotId,
                persisted,
                status == ResetSkillsOperationStatus.Succeeded
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
            if (!string.Equals(operation.OperationType, PlayerActionOperationTypes.ResetSkills, StringComparison.Ordinal))
                throw new InvalidOperationException("The operation store returned a different player action type.");
        }

        private static ResetSkillsResult FromStored(PlayerActionOperation operation)
        {
            var status = operation.Status.ToResetSkillsStatus();
            return new ResetSkillsResult(
                operation.OperationId,
                status,
                operation.FailureCode,
                operation.BeforeSkillSnapshotId,
                operation.AfterSkillSnapshotId,
                operation.CompletedAtUtc.HasValue,
                status == ResetSkillsOperationStatus.Succeeded
                    ? new ResetSkillsConfirmationSummary(operation.Target)
                    : null);
        }
    }
}
