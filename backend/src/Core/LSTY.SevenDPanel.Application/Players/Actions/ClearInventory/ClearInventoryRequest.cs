using System;

namespace LSTY.SevenDPanel.Application
{
    public enum ClearInventoryOperationStatus
    {
        Pending,
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class ClearInventoryRequest
    {
        public ClearInventoryRequest(
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
            ConfirmationSummary = new ClearInventoryConfirmationSummary(target);
        }

        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public string ClientRequestKey { get; }
        public string CorrelationId { get; }
        public bool DangerConfirmed { get; }
        public ClearInventoryConfirmationSummary ConfirmationSummary { get; }
    }

    public sealed class ClearInventoryConfirmationSummary
    {
        internal ClearInventoryConfirmationSummary(PlayerTargetStamp target)
        {
            TargetCrossplatformId = target.CrossplatformId;
            EntityId = target.EntityId;
            WorldId = target.WorldId;
        }

        public string TargetCrossplatformId { get; }
        public int EntityId { get; }
        public string WorldId { get; }
        public PlayerItemRemovalScope Scope => PlayerItemRemovalScope.BagOnly;
        public bool PreservesEquipment => true;
        public bool PreservesToolbelt => true;
        public bool PreservesOtherContainers => true;
    }

    public sealed class ClearInventoryResult
    {
        internal ClearInventoryResult(
            string operationId,
            ClearInventoryOperationStatus status,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            bool terminalPersisted,
            ClearInventoryConfirmationSummary? confirmationSummary)
        {
            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            Status = status;
            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = RequireOptionalId(
                beforeInventorySnapshotId,
                nameof(beforeInventorySnapshotId));
            AfterInventorySnapshotId = RequireOptionalId(
                afterInventorySnapshotId,
                nameof(afterInventorySnapshotId));
            TerminalPersisted = terminalPersisted;
            ConfirmationSummary = confirmationSummary;
        }

        public string OperationId { get; }
        public ClearInventoryOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool TerminalPersisted { get; }
        public ClearInventoryConfirmationSummary? ConfirmationSummary { get; }

        private static long? RequireOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }
    }

    internal static class ClearInventoryStatusMapping
    {
        internal static PlayerActionStatus ToPlayerActionStatus(this ClearInventoryOperationStatus status)
        {
            return status switch
            {
                ClearInventoryOperationStatus.Pending => PlayerActionStatus.Pending,
                ClearInventoryOperationStatus.Succeeded => PlayerActionStatus.Succeeded,
                ClearInventoryOperationStatus.Rejected => PlayerActionStatus.Rejected,
                ClearInventoryOperationStatus.Failed => PlayerActionStatus.Failed,
                ClearInventoryOperationStatus.Cancelled => PlayerActionStatus.Cancelled,
                ClearInventoryOperationStatus.ResultUnknown => PlayerActionStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        internal static ClearInventoryOperationStatus ToClearInventoryStatus(this PlayerActionStatus status)
        {
            return status switch
            {
                PlayerActionStatus.Pending => ClearInventoryOperationStatus.Pending,
                PlayerActionStatus.Succeeded => ClearInventoryOperationStatus.Succeeded,
                PlayerActionStatus.Rejected => ClearInventoryOperationStatus.Rejected,
                PlayerActionStatus.Failed => ClearInventoryOperationStatus.Failed,
                PlayerActionStatus.Cancelled => ClearInventoryOperationStatus.Cancelled,
                PlayerActionStatus.ResultUnknown => ClearInventoryOperationStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }
    }
}
