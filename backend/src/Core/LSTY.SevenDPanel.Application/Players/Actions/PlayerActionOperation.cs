using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application
{
    public enum PlayerActionStatus
    {
        Pending,
        Succeeded,
        Rejected,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public enum PlayerItemRemovalMode
    {
        Exact,
        UpToAvailable
    }

    public enum PlayerItemRemovalScope
    {
        BagOnly
    }

    public sealed record PlayerTargetStamp
    {
        public PlayerTargetStamp(
            string crossplatformId,
            int entityId,
            DateTimeOffset onlineObservedAtUtc,
            string worldId)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            CrossplatformId = PlayerEvidenceValidation.RequireText(crossplatformId, nameof(crossplatformId));
            EntityId = entityId;
            OnlineObservedAtUtc = PlayerEvidenceValidation.RequireUtc(
                onlineObservedAtUtc,
                nameof(onlineObservedAtUtc));
            WorldId = PlayerEvidenceValidation.RequireText(worldId, nameof(worldId));
        }

        public string CrossplatformId { get; }
        public int EntityId { get; }
        public DateTimeOffset OnlineObservedAtUtc { get; }
        public string WorldId { get; }
    }

    public static class PlayerActionOperationTypes
    {
        public const string GrantItem = "GrantItem";
        public const string RemoveItem = "RemoveItem";
        public const string ResetSkills = "ResetSkills";
        public const string ClearInventory = "ClearInventory";
        public const string ResetPlayerData = "ResetPlayerData";

        private static readonly HashSet<string> Values = new HashSet<string>(StringComparer.Ordinal)
        {
            GrantItem,
            RemoveItem,
            ResetSkills,
            ClearInventory,
            ResetPlayerData
        };

        internal static string RequireKnown(string? value, string parameterName)
        {
            if (value == null || !Values.Contains(value))
                throw new ArgumentException("A fixed player action operation type is required.", parameterName);
            return value;
        }
    }

    public sealed class PlayerActionOperation
    {
        public PlayerActionOperation(
            string operationId,
            string operationType,
            string operatorId,
            PlayerTargetStamp target,
            PlayerActionStatus status,
            DateTimeOffset createdAtUtc,
            DateTimeOffset? startedAtUtc,
            DateTimeOffset? completedAtUtc,
            string? failureCode,
            long? beforeInventorySnapshotId,
            long? afterInventorySnapshotId,
            long? beforeSkillSnapshotId,
            long? afterSkillSnapshotId,
            string? correlationId)
        {
            PlayerEvidenceValidation.RequireDefined(status, nameof(status));
            RequirePositiveOptionalId(beforeInventorySnapshotId, nameof(beforeInventorySnapshotId));
            RequirePositiveOptionalId(afterInventorySnapshotId, nameof(afterInventorySnapshotId));
            RequirePositiveOptionalId(beforeSkillSnapshotId, nameof(beforeSkillSnapshotId));
            RequirePositiveOptionalId(afterSkillSnapshotId, nameof(afterSkillSnapshotId));

            OperationId = PlayerEvidenceValidation.RequireText(operationId, nameof(operationId));
            OperationType = PlayerActionOperationTypes.RequireKnown(operationType, nameof(operationType));
            OperatorId = PlayerEvidenceValidation.RequireText(operatorId, nameof(operatorId));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Status = status;
            CreatedAtUtc = PlayerEvidenceValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            if (startedAtUtc.HasValue)
                StartedAtUtc = PlayerEvidenceValidation.RequireUtc(startedAtUtc.Value, nameof(startedAtUtc));
            if (completedAtUtc.HasValue)
                CompletedAtUtc = PlayerEvidenceValidation.RequireUtc(completedAtUtc.Value, nameof(completedAtUtc));
            if (StartedAtUtc.HasValue && StartedAtUtc.Value < CreatedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(startedAtUtc));
            if (CompletedAtUtc.HasValue && CompletedAtUtc.Value < (StartedAtUtc ?? CreatedAtUtc))
                throw new ArgumentOutOfRangeException(nameof(completedAtUtc));

            FailureCode = PlayerEvidenceValidation.OptionalText(failureCode, nameof(failureCode));
            BeforeInventorySnapshotId = beforeInventorySnapshotId;
            AfterInventorySnapshotId = afterInventorySnapshotId;
            BeforeSkillSnapshotId = beforeSkillSnapshotId;
            AfterSkillSnapshotId = afterSkillSnapshotId;
            CorrelationId = PlayerEvidenceValidation.OptionalText(correlationId, nameof(correlationId));
        }

        public string OperationId { get; }
        public string OperationType { get; }
        public string OperatorId { get; }
        public PlayerTargetStamp Target { get; }
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
        public string? CorrelationId { get; }

        private static void RequirePositiveOptionalId(long? value, string parameterName)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public interface IPlayerActionOperationQuery
    {
        PlayerActionOperation? Get(string operationId);
    }
}
