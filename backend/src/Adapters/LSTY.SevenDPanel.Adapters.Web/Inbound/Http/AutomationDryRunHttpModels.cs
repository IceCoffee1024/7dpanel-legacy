using System;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class AutomationDryRunHttpRequest : AutomationStrictHttpModel
    {
        public AutomationRuleHttpRequest? Rule { get; set; }
        public AutomationTriggerSnapshotHttpModel? Snapshot { get; set; }
    }

    public sealed class AutomationTriggerSnapshotHttpModel : AutomationStrictHttpModel
    {
        public string? TriggerId { get; set; }
        public AutomationTriggerHttpModel? Trigger { get; set; }
        public DateTimeOffset? OccurredAtUtc { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationTriggerActorHttpModel? Actor { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationChatTriggerHttpModel? Chat { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationCronTriggerHttpModel? Cron { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationBloodMoonTriggerHttpModel? BloodMoon { get; set; }

        public string[]? GapIds { get; set; }
    }

    public sealed class AutomationTriggerActorHttpModel : AutomationStrictHttpModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? CrossplatformId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? EntityId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Group { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PermissionLevel { get; set; }
    }

    public sealed class AutomationChatTriggerHttpModel : AutomationStrictHttpModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Text { get; set; }
    }

    public sealed class AutomationCronTriggerHttpModel : AutomationStrictHttpModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public DateTimeOffset? ScheduledForUtc { get; set; }
    }

    public sealed class AutomationBloodMoonTriggerHttpModel : AutomationStrictHttpModel
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Phase { get; set; }
    }

    public sealed class AutomationConditionTraceHttpResponse
    {
        public AutomationConditionTraceHttpResponse(
            string nodeId,
            string? fieldKey,
            string truth,
            bool isValueKnown)
        {
            NodeId = nodeId;
            FieldKey = fieldKey;
            Truth = truth;
            IsValueKnown = isValueKnown;
        }

        public string NodeId { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? FieldKey { get; }

        public string Truth { get; }
        public bool IsValueKnown { get; }
    }

    public sealed class AutomationConditionEvaluationHttpResponse
    {
        public AutomationConditionEvaluationHttpResponse(
            string truth,
            AutomationConditionTraceHttpResponse[] trace)
        {
            Truth = truth;
            Trace = trace;
        }

        public string Truth { get; }
        public AutomationConditionTraceHttpResponse[] Trace { get; }
    }

    public sealed class AutomationDependencyHttpResponse
    {
        public AutomationDependencyHttpResponse(string status, string? errorCode)
        {
            Status = status;
            ErrorCode = errorCode;
        }

        public string Status { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; }
    }

    public sealed class AutomationTargetResolutionHttpResponse
    {
        public AutomationTargetResolutionHttpResponse(bool isResolved, string? errorCode)
        {
            IsResolved = isResolved;
            ErrorCode = errorCode;
        }

        public bool IsResolved { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; }
    }

    public sealed class AutomationPlannedActionHttpResponse
    {
        public AutomationPlannedActionHttpResponse(
            int ordinal,
            string actionId,
            string actionType,
            AutomationDependencyHttpResponse dependency,
            AutomationTargetResolutionHttpResponse target,
            bool wouldExecute)
        {
            Ordinal = ordinal;
            ActionId = actionId;
            ActionType = actionType;
            Dependency = dependency;
            Target = target;
            WouldExecute = wouldExecute;
        }

        public int Ordinal { get; }
        public string ActionId { get; }
        public string ActionType { get; }
        public AutomationDependencyHttpResponse Dependency { get; }
        public AutomationTargetResolutionHttpResponse Target { get; }
        public bool WouldExecute { get; }
    }

    public sealed class AutomationDryRunHttpResponse
    {
        public AutomationDryRunHttpResponse(
            AutomationValidationHttpResponse validation,
            AutomationConditionEvaluationHttpResponse? evaluation,
            AutomationPlannedActionHttpResponse[] plannedActions)
        {
            Validation = validation;
            Evaluation = evaluation;
            PlannedActions = plannedActions;
        }

        public AutomationValidationHttpResponse Validation { get; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationConditionEvaluationHttpResponse? Evaluation { get; }

        public AutomationPlannedActionHttpResponse[] PlannedActions { get; }
    }
}
