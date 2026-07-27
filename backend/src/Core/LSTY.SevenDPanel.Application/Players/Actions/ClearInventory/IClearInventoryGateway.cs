using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IClearInventoryGateway
    {
        Task<ClearInventoryPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);

        Task<ClearInventoryGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);
    }

    public enum ClearInventoryPreparationStatus
    {
        Ready,
        Rejected,
        Failed
    }

    public sealed class ClearInventoryPreparationResult
    {
        private ClearInventoryPreparationResult(
            ClearInventoryPreparationStatus status,
            long? beforeInventorySnapshotId,
            string? failureCode)
        {
            Status = status;
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            FailureCode = failureCode;
        }

        public ClearInventoryPreparationStatus Status { get; }
        public long? BeforeInventorySnapshotId { get; }
        public string? FailureCode { get; }

        public static ClearInventoryPreparationResult Ready(long beforeInventorySnapshotId)
        {
            if (beforeInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeInventorySnapshotId));
            return new ClearInventoryPreparationResult(
                ClearInventoryPreparationStatus.Ready,
                beforeInventorySnapshotId,
                null);
        }

        public static ClearInventoryPreparationResult Rejected(string failureCode) =>
            Failure(ClearInventoryPreparationStatus.Rejected, failureCode);

        public static ClearInventoryPreparationResult Failed(string failureCode) =>
            Failure(ClearInventoryPreparationStatus.Failed, failureCode);

        private static ClearInventoryPreparationResult Failure(
            ClearInventoryPreparationStatus status,
            string failureCode) =>
            new ClearInventoryPreparationResult(
                status,
                null,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }

    public enum ClearInventoryGatewayStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class ClearInventoryGatewayResult
    {
        private ClearInventoryGatewayResult(
            ClearInventoryGatewayStatus status,
            long? afterInventorySnapshotId,
            string? failureCode)
        {
            Status = status;
            AfterInventorySnapshotId = afterInventorySnapshotId;
            FailureCode = failureCode;
        }

        public ClearInventoryGatewayStatus Status { get; }
        public long? AfterInventorySnapshotId { get; }
        public string? FailureCode { get; }

        public static ClearInventoryGatewayResult Succeeded(long afterInventorySnapshotId)
        {
            if (afterInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(afterInventorySnapshotId));
            return new ClearInventoryGatewayResult(
                ClearInventoryGatewayStatus.Succeeded,
                afterInventorySnapshotId,
                null);
        }

        public static ClearInventoryGatewayResult Rejected(string failureCode) =>
            Failure(ClearInventoryGatewayStatus.Rejected, failureCode);

        public static ClearInventoryGatewayResult Failed(string failureCode) =>
            Failure(ClearInventoryGatewayStatus.Failed, failureCode);

        public static ClearInventoryGatewayResult ResultUnknown(string failureCode) =>
            Failure(ClearInventoryGatewayStatus.ResultUnknown, failureCode);

        private static ClearInventoryGatewayResult Failure(
            ClearInventoryGatewayStatus status,
            string failureCode) =>
            new ClearInventoryGatewayResult(
                status,
                null,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }
}
