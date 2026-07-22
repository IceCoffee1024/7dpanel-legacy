using System;

namespace LSTY.SevenDPanel.Application
{
    public enum PlayerActionAuditStatus
    {
        Pending,
        Succeeded,
        Failed,
        Unknown
    }

    public sealed class PlayerActionAuditIntent
    {
        public PlayerActionAuditIntent(
            string operationId,
            string actorSubject,
            int targetEntityId,
            PlayerPlatformIdentity targetPlatformIdentity,
            string reason,
            DateTimeOffset requestedAtUtc)
        {
            OperationId = operationId;
            ActionType = "kick";
            ActorSubject = actorSubject;
            TargetEntityId = targetEntityId;
            TargetPlatformIdentity = targetPlatformIdentity;
            Reason = reason;
            RequestedAtUtc = requestedAtUtc;
        }

        public string OperationId { get; }

        public string ActionType { get; }

        public string ActorSubject { get; }

        public int TargetEntityId { get; }

        public PlayerPlatformIdentity TargetPlatformIdentity { get; }

        public string Reason { get; }

        public DateTimeOffset RequestedAtUtc { get; }
    }

    public sealed class PlayerActionAuditCompletion
    {
        private PlayerActionAuditCompletion(
            string operationId,
            PlayerActionAuditStatus status,
            DateTimeOffset completedAtUtc,
            string? targetName,
            string? failureCode)
        {
            OperationId = operationId;
            Status = status;
            CompletedAtUtc = completedAtUtc;
            TargetName = targetName;
            FailureCode = failureCode;
        }

        public string OperationId { get; }

        public PlayerActionAuditStatus Status { get; }

        public DateTimeOffset CompletedAtUtc { get; }

        public string? TargetName { get; }

        public string? FailureCode { get; }

        public static PlayerActionAuditCompletion Succeeded(
            string operationId,
            DateTimeOffset completedAtUtc,
            string targetName)
        {
            return new PlayerActionAuditCompletion(
                operationId,
                PlayerActionAuditStatus.Succeeded,
                completedAtUtc,
                targetName,
                null);
        }

        public static PlayerActionAuditCompletion Failed(
            string operationId,
            DateTimeOffset completedAtUtc,
            string? targetName,
            string failureCode)
        {
            return new PlayerActionAuditCompletion(
                operationId,
                PlayerActionAuditStatus.Failed,
                completedAtUtc,
                targetName,
                failureCode);
        }

        public static PlayerActionAuditCompletion Unknown(
            string operationId,
            DateTimeOffset completedAtUtc,
            string failureCode)
        {
            return new PlayerActionAuditCompletion(
                operationId,
                PlayerActionAuditStatus.Unknown,
                completedAtUtc,
                null,
                failureCode);
        }
    }
}