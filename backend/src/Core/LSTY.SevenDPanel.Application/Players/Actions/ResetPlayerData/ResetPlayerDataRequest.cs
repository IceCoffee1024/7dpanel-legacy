using System;

namespace LSTY.SevenDPanel.Application
{
    public enum ResetPlayerDataOperationStatus
    {
        Pending,
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class ResetPlayerDataRequest
    {
        public ResetPlayerDataRequest(
            string operatorId,
            PlayerTargetStamp target,
            string clientRequestKey,
            string correlationId,
            bool dangerConfirmed)
        {
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            ClientRequestKey = PlayerEvidenceValidation.RequireText(
                clientRequestKey,
                nameof(clientRequestKey));
            CorrelationId = PlayerEvidenceValidation.RequireText(correlationId, nameof(correlationId));
            DangerConfirmed = dangerConfirmed;
            ConfirmationSummary = new ResetPlayerDataConfirmationSummary(target);
        }

        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public bool DangerConfirmed { get; }
        public ResetPlayerDataConfirmationSummary ConfirmationSummary { get; }
    }

    public sealed class ResetPlayerDataConfirmationSummary
    {
        internal ResetPlayerDataConfirmationSummary(PlayerTargetStamp target)
        {
            TargetCrossplatformId = target.CrossplatformId;
            EntityId = target.EntityId;
            WorldId = target.WorldId;
        }

        public string TargetCrossplatformId { get; }
        public int EntityId { get; }
        public string WorldId { get; }
        public string Scope => "CompletePlayerData";
        public bool RequiresStrongConfirmation => true;
        public bool PreservesStableIdentity => true;
        public bool PreservesWorld => true;
    }

    public sealed class ResetPlayerDataResult
    {
        internal ResetPlayerDataResult(
            string operationId,
            ResetPlayerDataOperationStatus status,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            bool terminalPersisted,
            ResetPlayerDataConfirmationSummary? confirmationSummary)
        {
            if (!Enum.IsDefined(typeof(ResetPlayerDataOperationStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            BeforeSkillSnapshotId = RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            TerminalPersisted = terminalPersisted;
            ConfirmationSummary = confirmationSummary;
        }

        public string OperationId { get; }
        public ResetPlayerDataOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public bool TerminalPersisted { get; }
        public ResetPlayerDataConfirmationSummary? ConfirmationSummary { get; }
        public bool ManualVerificationRequired => Status == ResetPlayerDataOperationStatus.ResultUnknown;
        public string? ManualVerificationCode => ManualVerificationRequired
            ? "verify_player_state_before_retry"
            : null;

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    internal static class ResetPlayerDataStatusMapping
    {
        internal static PlayerActionStatus ToPlayerActionStatus(
            this ResetPlayerDataOperationStatus status) => status switch
        {
            ResetPlayerDataOperationStatus.Pending => PlayerActionStatus.Pending,
            ResetPlayerDataOperationStatus.Succeeded => PlayerActionStatus.Succeeded,
            ResetPlayerDataOperationStatus.Rejected => PlayerActionStatus.Rejected,
            ResetPlayerDataOperationStatus.Failed => PlayerActionStatus.Failed,
            ResetPlayerDataOperationStatus.Cancelled => PlayerActionStatus.Cancelled,
            ResetPlayerDataOperationStatus.ResultUnknown => PlayerActionStatus.ResultUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        internal static ResetPlayerDataOperationStatus ToResetPlayerDataStatus(
            this PlayerActionStatus status) => status switch
        {
            PlayerActionStatus.Pending => ResetPlayerDataOperationStatus.Pending,
            PlayerActionStatus.Succeeded => ResetPlayerDataOperationStatus.Succeeded,
            PlayerActionStatus.Rejected => ResetPlayerDataOperationStatus.Rejected,
            PlayerActionStatus.Failed => ResetPlayerDataOperationStatus.Failed,
            PlayerActionStatus.Cancelled => ResetPlayerDataOperationStatus.Cancelled,
            PlayerActionStatus.ResultUnknown => ResetPlayerDataOperationStatus.ResultUnknown,
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }
}
