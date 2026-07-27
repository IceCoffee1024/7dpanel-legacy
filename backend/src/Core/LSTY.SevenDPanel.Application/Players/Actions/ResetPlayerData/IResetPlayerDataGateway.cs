using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IResetPlayerDataGateway
    {
        Task<ResetPlayerDataPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);

        Task<ResetPlayerDataGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);
    }

    public enum ResetPlayerDataPreparationStatus
    {
        Ready,
        Rejected,
        Failed
    }

    public sealed class ResetPlayerDataPreparationResult
    {
        private ResetPlayerDataPreparationResult(
            ResetPlayerDataPreparationStatus status,
            long? beforeInventorySnapshotId,
            long? beforeSkillSnapshotId,
            string? failureCode)
        {
            Status = status;
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            BeforeSkillSnapshotId = beforeSkillSnapshotId;
            FailureCode = failureCode;
        }

        public ResetPlayerDataPreparationStatus Status { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public string? FailureCode { get; }

        public static ResetPlayerDataPreparationResult Ready(
            long beforeInventorySnapshotId,
            long beforeSkillSnapshotId)
        {
            if (beforeInventorySnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeInventorySnapshotId));
            if (beforeSkillSnapshotId <= 0)
                throw new ArgumentOutOfRangeException(nameof(beforeSkillSnapshotId));
            return new ResetPlayerDataPreparationResult(
                ResetPlayerDataPreparationStatus.Ready,
                beforeInventorySnapshotId,
                beforeSkillSnapshotId,
                null);
        }

        public static ResetPlayerDataPreparationResult Rejected(string failureCode) =>
            Failure(ResetPlayerDataPreparationStatus.Rejected, failureCode);

        public static ResetPlayerDataPreparationResult Failed(string failureCode) =>
            Failure(ResetPlayerDataPreparationStatus.Failed, failureCode);

        private static ResetPlayerDataPreparationResult Failure(
            ResetPlayerDataPreparationStatus status,
            string failureCode) =>
            new ResetPlayerDataPreparationResult(
                status,
                null,
                null,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }

    public enum ResetPlayerDataGatewayStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class ResetPlayerDataGatewayResult
    {
        private ResetPlayerDataGatewayResult(
            ResetPlayerDataGatewayStatus status,
            string? failureCode)
        {
            Status = status;
            FailureCode = failureCode;
        }

        public ResetPlayerDataGatewayStatus Status { get; }
        public string? FailureCode { get; }

        public static ResetPlayerDataGatewayResult Succeeded() =>
            new ResetPlayerDataGatewayResult(ResetPlayerDataGatewayStatus.Succeeded, null);

        public static ResetPlayerDataGatewayResult Rejected(string failureCode) =>
            Failure(ResetPlayerDataGatewayStatus.Rejected, failureCode);

        public static ResetPlayerDataGatewayResult Failed(string failureCode) =>
            Failure(ResetPlayerDataGatewayStatus.Failed, failureCode);

        public static ResetPlayerDataGatewayResult ResultUnknown(string failureCode) =>
            Failure(ResetPlayerDataGatewayStatus.ResultUnknown, failureCode);

        private static ResetPlayerDataGatewayResult Failure(
            ResetPlayerDataGatewayStatus status,
            string failureCode) =>
            new ResetPlayerDataGatewayResult(
                status,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }
}
