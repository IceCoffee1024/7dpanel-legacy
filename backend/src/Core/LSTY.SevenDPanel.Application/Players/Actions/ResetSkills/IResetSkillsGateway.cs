using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IResetSkillsGateway
    {
        Task<ResetSkillsPreparationResult> PrepareAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);

        Task<ResetSkillsGatewayResult> ExecuteAsync(
            PlayerTargetStamp target,
            CancellationToken cancellationToken);
    }

    public enum ResetSkillsPreparationStatus
    {
        Ready,
        Rejected,
        Failed
    }

    public sealed class ResetSkillsPreparationResult
    {
        private ResetSkillsPreparationResult(
            ResetSkillsPreparationStatus status,
            long? beforeSkillSnapshotId,
            string? failureCode)
        {
            Status = status;
            BeforeSkillSnapshotId = beforeSkillSnapshotId;
            FailureCode = failureCode;
        }

        public ResetSkillsPreparationStatus Status { get; }
        public long? BeforeSkillSnapshotId { get; }
        public string? FailureCode { get; }

        public static ResetSkillsPreparationResult Ready(long beforeSkillSnapshotId)
        {
            if (beforeSkillSnapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(beforeSkillSnapshotId));
            return new ResetSkillsPreparationResult(
                ResetSkillsPreparationStatus.Ready,
                beforeSkillSnapshotId,
                null);
        }

        public static ResetSkillsPreparationResult Rejected(string failureCode) =>
            Failure(ResetSkillsPreparationStatus.Rejected, failureCode);

        public static ResetSkillsPreparationResult Failed(string failureCode) =>
            Failure(ResetSkillsPreparationStatus.Failed, failureCode);

        private static ResetSkillsPreparationResult Failure(
            ResetSkillsPreparationStatus status,
            string failureCode) =>
            new ResetSkillsPreparationResult(
                status,
                null,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }

    public enum ResetSkillsGatewayStatus
    {
        Succeeded,
        Rejected,
        Failed,
        ResultUnknown
    }

    public sealed class ResetSkillsGatewayResult
    {
        private ResetSkillsGatewayResult(
            ResetSkillsGatewayStatus status,
            long? afterSkillSnapshotId,
            string? failureCode)
        {
            Status = status;
            AfterSkillSnapshotId = afterSkillSnapshotId;
            FailureCode = failureCode;
        }

        public ResetSkillsGatewayStatus Status { get; }
        public long? AfterSkillSnapshotId { get; }
        public string? FailureCode { get; }

        public static ResetSkillsGatewayResult Succeeded(long afterSkillSnapshotId)
        {
            if (afterSkillSnapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(afterSkillSnapshotId));
            return new ResetSkillsGatewayResult(
                ResetSkillsGatewayStatus.Succeeded,
                afterSkillSnapshotId,
                null);
        }

        public static ResetSkillsGatewayResult Rejected(string failureCode) =>
            Failure(ResetSkillsGatewayStatus.Rejected, failureCode);

        public static ResetSkillsGatewayResult Failed(string failureCode) =>
            Failure(ResetSkillsGatewayStatus.Failed, failureCode);

        public static ResetSkillsGatewayResult ResultUnknown(string failureCode) =>
            Failure(ResetSkillsGatewayStatus.ResultUnknown, failureCode);

        private static ResetSkillsGatewayResult Failure(
            ResetSkillsGatewayStatus status,
            string failureCode) =>
            new ResetSkillsGatewayResult(
                status,
                null,
                PlayerEvidenceValidation.RequireText(failureCode, nameof(failureCode)));
    }
}
