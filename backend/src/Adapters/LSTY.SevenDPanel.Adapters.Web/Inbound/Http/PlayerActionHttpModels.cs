using System;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class PlayerActionTargetHttpRequest
    {
        public string CrossplatformId { get; set; } = null!;
        public int EntityId { get; set; }
        public DateTimeOffset OnlineObservedAtUtc { get; set; }
        public string WorldId { get; set; } = null!;

        internal PlayerTargetStamp ToTargetStamp()
        {
            if (string.IsNullOrWhiteSpace(CrossplatformId) ||
                string.IsNullOrWhiteSpace(WorldId) ||
                OnlineObservedAtUtc == default)
            {
                throw new ArgumentException("A complete fixed player target is required.");
            }
            return new PlayerTargetStamp(
                CrossplatformId!,
                EntityId,
                OnlineObservedAtUtc,
                WorldId!);
        }
    }

    public sealed class GrantItemHttpRequest
    {
        public PlayerActionTargetHttpRequest Target { get; set; } = null!;
        public string CatalogVersion { get; set; } = null!;
        public string ResourceId { get; set; } = null!;
        public int Quantity { get; set; }
        public int? Quality { get; set; }
        public bool HiddenItemConfirmed { get; set; }
        public string ClientRequestKey { get; set; } = null!;
    }

    public sealed class RemoveItemHttpRequest
    {
        public PlayerActionTargetHttpRequest Target { get; set; } = null!;
        public string CatalogVersion { get; set; } = null!;
        public string ResourceId { get; set; } = null!;
        public int Quantity { get; set; }
        public int? Quality { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerItemRemovalScope RemovalScope { get; set; } = PlayerItemRemovalScope.BagOnly;
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerItemRemovalMode RemovalMode { get; set; } = PlayerItemRemovalMode.Exact;
        public string ClientRequestKey { get; set; } = null!;
    }

    public sealed class ResetSkillsHttpRequest
    {
        public PlayerActionTargetHttpRequest Target { get; set; } = null!;
        public string ClientRequestKey { get; set; } = null!;
        public bool DangerConfirmed { get; set; }
    }

    public sealed class ClearInventoryHttpRequest
    {
        public PlayerActionTargetHttpRequest Target { get; set; } = null!;
        public string ClientRequestKey { get; set; } = null!;
        public bool DangerConfirmed { get; set; }
    }

    public sealed class ResetPlayerDataHttpRequest
    {
        public PlayerActionTargetHttpRequest Target { get; set; } = null!;
        public string ClientRequestKey { get; set; } = null!;
        public bool DangerConfirmed { get; set; }
    }

    public sealed class PlayerActionTargetHttpResponse
    {
        public PlayerActionTargetHttpResponse(PlayerTargetStamp value)
        {
            CrossplatformId = value.CrossplatformId;
            EntityId = value.EntityId;
            OnlineObservedAtUtc = value.OnlineObservedAtUtc;
            WorldId = value.WorldId;
        }

        public string CrossplatformId { get; }
        public int EntityId { get; }
        public DateTimeOffset OnlineObservedAtUtc { get; }
        public string WorldId { get; }
    }

    public sealed class GrantItemHttpResponse
    {
        public GrantItemHttpResponse(GrantItemResult value, string correlationId)
        {
            OperationId = value.OperationId;
            CorrelationId = correlationId;
            Status = value.Status;
            FailureCode = value.FailureCode;
            ActualQuantity = value.ActualQuantity;
            BeforeInventorySnapshotId = value.BeforeInventorySnapshotId;
            AfterInventorySnapshotId = value.AfterInventorySnapshotId;
            Reused = value.Reused;
            TerminalStatePersisted = value.TerminalStatePersisted;
        }

        public string OperationId { get; }
        public string CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerActionStatus Status { get; }
        public string? FailureCode { get; }
        public int? ActualQuantity { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool Reused { get; }
        public bool TerminalStatePersisted { get; }
    }

    public sealed class RemoveItemHttpResponse
    {
        public RemoveItemHttpResponse(RemoveItemResult value, string correlationId)
        {
            OperationId = value.OperationId;
            CorrelationId = correlationId;
            Status = value.Status;
            FailureCode = value.FailureCode;
            ActualQuantity = value.ActualQuantity;
            BeforeInventorySnapshotId = value.BeforeInventorySnapshotId;
            AfterInventorySnapshotId = value.AfterInventorySnapshotId;
            Reused = value.Reused;
            TerminalStatePersisted = value.TerminalStatePersisted;
        }

        public string OperationId { get; }
        public string CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerActionStatus Status { get; }
        public string? FailureCode { get; }
        public int? ActualQuantity { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool Reused { get; }
        public bool TerminalStatePersisted { get; }
    }

    public sealed class ResetSkillsHttpResponse
    {
        public ResetSkillsHttpResponse(ResetSkillsResult value, string correlationId)
        {
            OperationId = value.OperationId;
            CorrelationId = correlationId;
            Status = value.Status;
            FailureCode = value.FailureCode;
            BeforeSkillSnapshotId = value.BeforeSkillSnapshotId;
            AfterSkillSnapshotId = value.AfterSkillSnapshotId;
            TerminalStatePersisted = value.TerminalPersisted;
            ConfirmationSummary = value.ConfirmationSummary;
        }

        public string OperationId { get; }
        public string CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ResetSkillsOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
        public bool TerminalStatePersisted { get; }
        public ResetSkillsConfirmationSummary? ConfirmationSummary { get; }
    }

    public sealed class ClearInventoryHttpResponse
    {
        public ClearInventoryHttpResponse(ClearInventoryResult value, string correlationId)
        {
            OperationId = value.OperationId;
            CorrelationId = correlationId;
            Status = value.Status;
            FailureCode = value.FailureCode;
            BeforeInventorySnapshotId = value.BeforeInventorySnapshotId;
            AfterInventorySnapshotId = value.AfterInventorySnapshotId;
            TerminalStatePersisted = value.TerminalPersisted;
            ConfirmationSummary = value.ConfirmationSummary;
        }

        public string OperationId { get; }
        public string CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ClearInventoryOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public bool TerminalStatePersisted { get; }
        public ClearInventoryConfirmationSummary? ConfirmationSummary { get; }
    }

    public sealed class ResetPlayerDataHttpResponse
    {
        public ResetPlayerDataHttpResponse(ResetPlayerDataResult value, string correlationId)
        {
            OperationId = value.OperationId;
            CorrelationId = correlationId;
            Status = value.Status;
            FailureCode = value.FailureCode;
            BeforeInventorySnapshotId = value.BeforeInventorySnapshotId;
            BeforeSkillSnapshotId = value.BeforeSkillSnapshotId;
            TerminalStatePersisted = value.TerminalPersisted;
            ConfirmationSummary = value.ConfirmationSummary;
            ManualVerificationRequired = value.ManualVerificationRequired;
            ManualVerificationCode = value.ManualVerificationCode;
        }

        public string OperationId { get; }
        public string CorrelationId { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public ResetPlayerDataOperationStatus Status { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public bool TerminalStatePersisted { get; }
        public ResetPlayerDataConfirmationSummary? ConfirmationSummary { get; }
        public bool ManualVerificationRequired { get; }
        public string? ManualVerificationCode { get; }
    }

    public sealed class PlayerActionOperationHttpResponse
    {
        public PlayerActionOperationHttpResponse(PlayerActionOperation value)
        {
            OperationId = value.OperationId;
            CorrelationId = value.CorrelationId;
            OperationType = value.OperationType;
            OperatorId = value.OperatorId;
            Target = new PlayerActionTargetHttpResponse(value.Target);
            Status = value.Status;
            CreatedAtUtc = value.CreatedAtUtc;
            StartedAtUtc = value.StartedAtUtc;
            CompletedAtUtc = value.CompletedAtUtc;
            FailureCode = value.FailureCode;
            BeforeInventorySnapshotId = value.BeforeInventorySnapshotId;
            AfterInventorySnapshotId = value.AfterInventorySnapshotId;
            BeforeSkillSnapshotId = value.BeforeSkillSnapshotId;
            AfterSkillSnapshotId = value.AfterSkillSnapshotId;
        }

        public string OperationId { get; }
        public string? CorrelationId { get; }
        public string OperationType { get; }
        public string OperatorId { get; }
        public PlayerActionTargetHttpResponse Target { get; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PlayerActionStatus Status { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public string? FailureCode { get; }
        public long? BeforeInventorySnapshotId { get; }
        public long? AfterInventorySnapshotId { get; }
        public long? BeforeSkillSnapshotId { get; }
        public long? AfterSkillSnapshotId { get; }
    }
}
