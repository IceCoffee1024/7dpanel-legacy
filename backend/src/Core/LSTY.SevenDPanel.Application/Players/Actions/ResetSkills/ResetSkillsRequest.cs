using System;

namespace LSTY.SevenDPanel.Application
{
    public enum ResetSkillsOperationStatus
    {
        Pending,
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class ResetSkillsRequest
    {
        public ResetSkillsRequest(
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
            ConfirmationSummary = new ResetSkillsConfirmationSummary(target);
        }

        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public bool DangerConfirmed { get; }
        public ResetSkillsConfirmationSummary ConfirmationSummary { get; }
    }

    public sealed class ResetSkillsConfirmationSummary
    {
        internal ResetSkillsConfirmationSummary(PlayerTargetStamp target)
        {
            TargetCrossplatformId = target.CrossplatformId;
            EntityId = target.EntityId;
            WorldId = target.WorldId;
        }

        public string TargetCrossplatformId { get; }
        public int EntityId { get; }
        public string WorldId { get; }
        public string Scope => "CurrentVersionProgressionOnly";
        public bool PreservesIdentity => true;
        public bool PreservesPosition => true;
        public bool PreservesInventory => true;
    }

    public sealed class ResetSkillsResult
    {
        internal ResetSkillsResult(
            string operationId,
            ResetSkillsOperationStatus status,
            string? failureCode,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId,
            bool terminalPersisted,
            ResetSkillsConfirmationSummary? confirmationSummary)
        {
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeSkillSnapshotId = RequireOptionalId(
                beforeSkillSnapshotId,
                nameof(beforeSkillSnapshotId));
            AfterSkillSnapshotId = RequireOptionalId(
                afterSkillSnapshotId,
                nameof(afterSkillSnapshotId));
            TerminalPersisted = terminalPersisted;
            ConfirmationSummary = confirmationSummary;
        }

        public string OperationId { get; }
        public ResetSkillsOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
        public bool TerminalPersisted { get; }
        public ResetSkillsConfirmationSummary? ConfirmationSummary { get; }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    internal static class ResetSkillsStatusMapping
    {
        internal static PlayerActionStatus ToPlayerActionStatus(this ResetSkillsOperationStatus status)
        {
            return status switch
            {
                ResetSkillsOperationStatus.Pending => PlayerActionStatus.Pending,
                ResetSkillsOperationStatus.Succeeded => PlayerActionStatus.Succeeded,
                ResetSkillsOperationStatus.Rejected => PlayerActionStatus.Rejected,
                ResetSkillsOperationStatus.Failed => PlayerActionStatus.Failed,
                ResetSkillsOperationStatus.Cancelled => PlayerActionStatus.Cancelled,
                ResetSkillsOperationStatus.ResultUnknown => PlayerActionStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        internal static ResetSkillsOperationStatus ToResetSkillsStatus(this PlayerActionStatus status)
        {
            return status switch
            {
                PlayerActionStatus.Pending => ResetSkillsOperationStatus.Pending,
                PlayerActionStatus.Succeeded => ResetSkillsOperationStatus.Succeeded,
                PlayerActionStatus.Rejected => ResetSkillsOperationStatus.Rejected,
                PlayerActionStatus.Failed => ResetSkillsOperationStatus.Failed,
                PlayerActionStatus.Cancelled => ResetSkillsOperationStatus.Cancelled,
                PlayerActionStatus.ResultUnknown => ResetSkillsOperationStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }
    }
}
