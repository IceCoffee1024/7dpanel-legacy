using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class AutomationExecutionHttpResponse
    {
        public AutomationExecutionHttpResponse(
            AutomationExecutionRecord execution,
            System.Collections.Generic.IReadOnlyList<AutomationConditionExecutionResult> conditions,
            System.Collections.Generic.IReadOnlyList<AutomationActionExecutionResult> actions)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            ExecutionId = execution.ExecutionId;
            RuleId = execution.RuleId;
            TriggerId = execution.TriggerId;
            Status = execution.Status.ToString();
            CorrelationId = execution.CorrelationId;
            StartedAtUtc = execution.StartedAtUtc;
            CompletedAtUtc = execution.CompletedAtUtc;
            ErrorCode = execution.ErrorCode;
            Conditions = (conditions ?? throw new ArgumentNullException(nameof(conditions)))
                .Select(result => new AutomationConditionResultHttpResponse(
                    result.NodeId,
                    result.Truth.ToString()))
                .ToArray();
            Actions = (actions ?? throw new ArgumentNullException(nameof(actions)))
                .Select(result => new AutomationActionResultHttpResponse(
                    result.Ordinal,
                    result.ActionType,
                    result.Status.ToString(),
                    result.ErrorCode,
                    result.StartedAtUtc,
                    result.CompletedAtUtc))
                .ToArray();
        }

        public string ExecutionId { get; }
        public string RuleId { get; }
        public string TriggerId { get; }
        public string Status { get; }
        public string CorrelationId { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public string? ErrorCode { get; }
        public AutomationConditionResultHttpResponse[] Conditions { get; }
        public AutomationActionResultHttpResponse[] Actions { get; }
    }

    public sealed record AutomationConditionResultHttpResponse(
        string NodeId,
        string Truth);

    public sealed record AutomationActionResultHttpResponse(
        int Ordinal,
        string ActionType,
        string Status,
        string? ErrorCode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? CompletedAtUtc);

    public abstract class AutomationStrictHttpModel
    {
        [JsonExtensionData(ReadData = true, WriteData = false)]
        private readonly IDictionary<string, JToken> unknownProperties =
            new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

        internal bool HasUnknownProperties => unknownProperties.Count != 0;
    }

    public sealed class AutomationRuleHttpRequest : AutomationStrictHttpModel
    {
        public string? Id { get; set; }
        public long? ExpectedVersion { get; set; }
        public string? Name { get; set; }
        public bool? IsEnabled { get; set; }
        public AutomationTriggerHttpModel? Trigger { get; set; }
        public AutomationConditionHttpModel? Condition { get; set; }
        public AutomationActionHttpModel[]? Actions { get; set; }
        public long? CooldownSeconds { get; set; }
        public string? CooldownScope { get; set; }
        public string? ConcurrencyPolicy { get; set; }
        public string? FailurePolicy { get; set; }
    }

    public sealed class AutomationTriggerHttpModel : AutomationStrictHttpModel
    {
        public string? Type { get; set; }
    }

    public sealed class AutomationConditionHttpModel : AutomationStrictHttpModel
    {
        public string? NodeId { get; set; }
        public string? Kind { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationConditionPredicateHttpModel? Predicate { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationConditionHttpModel[]? Children { get; set; }
    }

    public sealed class AutomationConditionPredicateHttpModel : AutomationStrictHttpModel
    {
        public string? FieldKey { get; set; }
        public string? Operator { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ScalarValue { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? MinimumInclusive { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? MaximumInclusive { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string[]? SetValues { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationTimeWindowHttpModel? Window { get; set; }
    }

    public sealed class AutomationTimeWindowHttpModel : AutomationStrictHttpModel
    {
        public string? TimeZoneId { get; set; }
        public AutomationTimeOfDayHttpModel? StartInclusive { get; set; }
        public AutomationTimeOfDayHttpModel? EndInclusive { get; set; }
    }

    public sealed class AutomationTimeOfDayHttpModel : AutomationStrictHttpModel
    {
        public int? Hour { get; set; }
        public int? Minute { get; set; }
    }

    public sealed class AutomationTargetHttpModel : AutomationStrictHttpModel
    {
        public string? Kind { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? ReferenceId { get; set; }
    }

    public sealed class AutomationActionHttpModel : AutomationStrictHttpModel
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public AutomationTargetHttpModel? Target { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? BroadcastMessage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? PrivateMessage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? Announcement { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationGrantItemActionHttpModel? GrantItem { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationGrantRewardPackageActionHttpModel? GrantRewardPackage { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationAmountActionHttpModel? AdjustEconomy { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationReasonActionHttpModel? KickPlayer { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMutePlayerActionHttpModel? MutePlayer { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationRestrictedCommandActionHttpModel? RestrictedCommand { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public AutomationMessageActionHttpModel? DiscordMessage { get; set; }
    }

    public sealed class AutomationMessageActionHttpModel : AutomationStrictHttpModel
    {
        public string? Message { get; set; }
    }

    public sealed class AutomationGrantItemActionHttpModel : AutomationStrictHttpModel
    {
        public string? ResourceId { get; set; }
        public long? Amount { get; set; }
    }

    public sealed class AutomationGrantRewardPackageActionHttpModel : AutomationStrictHttpModel
    {
        public string? RewardPackageId { get; set; }
    }

    public sealed class AutomationAmountActionHttpModel : AutomationStrictHttpModel
    {
        public long? Amount { get; set; }
    }

    public sealed class AutomationReasonActionHttpModel : AutomationStrictHttpModel
    {
        public string? Reason { get; set; }
    }

    public sealed class AutomationMutePlayerActionHttpModel : AutomationStrictHttpModel
    {
        public long? DurationSeconds { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class AutomationRestrictedCommandActionHttpModel : AutomationStrictHttpModel
    {
        public string? CommandCatalogKey { get; set; }
    }

    public sealed class AutomationRuleHttpResponse
    {
        public AutomationRuleHttpResponse(
            string id,
            long version,
            string name,
            bool isEnabled,
            AutomationTriggerHttpModel trigger,
            AutomationConditionHttpModel condition,
            AutomationActionHttpModel[] actions,
            long cooldownSeconds,
            string cooldownScope,
            string concurrencyPolicy,
            string failurePolicy,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc)
        {
            Id = id;
            Version = version;
            Name = name;
            IsEnabled = isEnabled;
            Trigger = trigger;
            Condition = condition;
            Actions = actions;
            CooldownSeconds = cooldownSeconds;
            CooldownScope = cooldownScope;
            ConcurrencyPolicy = concurrencyPolicy;
            FailurePolicy = failurePolicy;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string Id { get; }
        public long Version { get; }
        public string Name { get; }
        public bool IsEnabled { get; }
        public AutomationTriggerHttpModel Trigger { get; }
        public AutomationConditionHttpModel Condition { get; }
        public AutomationActionHttpModel[] Actions { get; }
        public long CooldownSeconds { get; }
        public string CooldownScope { get; }
        public string ConcurrencyPolicy { get; }
        public string FailurePolicy { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }
    }

    public sealed class AutomationValidationIssueHttpResponse
    {
        public AutomationValidationIssueHttpResponse(string code, string path)
        {
            Code = code;
            Path = path;
        }

        public string Code { get; }
        public string Path { get; }
    }

    public sealed class AutomationValidationHttpResponse
    {
        public AutomationValidationHttpResponse(
            bool isValid,
            AutomationValidationIssueHttpResponse[] issues)
        {
            IsValid = isValid;
            Issues = issues;
        }

        public bool IsValid { get; }
        public AutomationValidationIssueHttpResponse[] Issues { get; }
    }

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

    internal static class AutomationHttpMapper
    {
        internal static bool TryToDraft(
            AutomationRuleHttpRequest? request,
            out AutomationRuleDraft? draft)
        {
            draft = null;
            if (request == null || request.HasUnknownProperties ||
                !IsSafeIdentifier(request.Id) || request.Name == null ||
                !request.ExpectedVersion.HasValue || !request.IsEnabled.HasValue ||
                !request.CooldownSeconds.HasValue || request.CooldownSeconds.Value < 0 ||
                request.Trigger == null || request.Trigger.HasUnknownProperties ||
                !TryEnum(request.Trigger?.Type, out AutomationTriggerType triggerType) ||
                !TryEnum(request.CooldownScope, out AutomationCooldownScope cooldownScope) ||
                !TryEnum(request.ConcurrencyPolicy, out AutomationConcurrencyPolicy concurrencyPolicy) ||
                !TryEnum(request.FailurePolicy, out AutomationFailurePolicy failurePolicy) ||
                !TryCondition(request.Condition, out var condition) ||
                request.Actions == null)
            {
                return false;
            }

            var actions = new List<AutomationActionDraft>(request.Actions.Length);
            foreach (var action in request.Actions)
            {
                if (!TryAction(action, out var mapped)) return false;
                actions.Add(mapped!);
            }

            try
            {
                draft = new AutomationRuleDraft(
                    request.Id!,
                    request.ExpectedVersion.Value,
                    request.Name,
                    request.IsEnabled.Value,
                    triggerType,
                    condition!,
                    actions,
                    TimeSpan.FromSeconds(request.CooldownSeconds.Value),
                    cooldownScope,
                    concurrencyPolicy,
                    failurePolicy);
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is OverflowException)
            {
                return false;
            }
        }

        internal static bool TryToSnapshot(
            AutomationTriggerSnapshotHttpModel? request,
            out AutomationTriggerSnapshot? snapshot)
        {
            snapshot = null;
            if (request == null || request.HasUnknownProperties ||
                !Bounded(request.TriggerId) ||
                !request.OccurredAtUtc.HasValue || !Utc(request.OccurredAtUtc.Value) ||
                request.Trigger == null || request.Trigger.HasUnknownProperties ||
                !TryEnum(request.Trigger?.Type, out AutomationTriggerType triggerType) ||
                request.GapIds == null || request.GapIds.Length > AutomationCondition.MaxNodeCount ||
                request.GapIds.Any(value => !Bounded(value)) ||
                !TryActor(request.Actor) || !TryChat(request.Chat) ||
                !TryCron(request.Cron) || !TryBloodMoon(request.BloodMoon) ||
                !SnapshotShapeMatches(request, triggerType))
            {
                return false;
            }

            snapshot = new AutomationTriggerSnapshot(
                request.TriggerId!,
                triggerType.ToString(),
                request.OccurredAtUtc.Value,
                request.Actor?.CrossplatformId,
                request.Actor?.EntityId,
                request.Actor?.Group,
                request.Actor?.PermissionLevel,
                request.Chat?.Text,
                request.Cron?.ScheduledForUtc,
                request.BloodMoon?.Phase,
                request.GapIds);
            return true;
        }

        internal static AutomationRuleHttpResponse ToResponse(AutomationRule rule) =>
            new(
                rule.Id,
                rule.Version,
                rule.Name,
                rule.IsEnabled,
                new AutomationTriggerHttpModel { Type = SafeTriggerType(rule.Trigger.Type) },
                ToCondition(rule.ConditionRoot),
                rule.Actions.Select(ToAction).ToArray(),
                checked((long)rule.CooldownDuration.TotalSeconds),
                rule.CooldownScope.ToString(),
                rule.ConcurrencyPolicy.ToString(),
                rule.FailurePolicy.ToString(),
                rule.CreatedAtUtc,
                rule.UpdatedAtUtc);

        internal static AutomationValidationHttpResponse ToResponse(
            AutomationValidationResult result) =>
            new(
                result.IsValid,
                result.Issues.Select(issue =>
                    new AutomationValidationIssueHttpResponse(issue.Code, issue.Path)).ToArray());

        internal static AutomationDryRunHttpResponse ToResponse(AutomationDryRunResult result) =>
            new(
                ToResponse(result.Validation),
                result.Evaluation == null
                    ? null
                    : new AutomationConditionEvaluationHttpResponse(
                        result.Evaluation.Truth.ToString(),
                        result.Evaluation.Trace.Select(trace =>
                            new AutomationConditionTraceHttpResponse(
                                trace.NodeId,
                                trace.FieldKey,
                                trace.Truth.ToString(),
                                trace.IsValueKnown)).ToArray()),
                result.PlannedActions.Select(action =>
                    new AutomationPlannedActionHttpResponse(
                        action.Ordinal,
                        action.ActionId,
                        SafeActionType(action.ActionType),
                        new AutomationDependencyHttpResponse(
                            SafeDependencyStatus(action.Dependency.Status),
                            SafeCode(action.Dependency.ErrorCode)),
                        new AutomationTargetResolutionHttpResponse(
                            action.Target.IsResolved,
                            SafeCode(action.Target.ErrorCode)),
                        action.WouldExecute)).ToArray());

        internal static bool IsSafeIdentifier(string? value) =>
            Bounded(value) && value!.All(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-' ||
                character == '.' || character == ':');

        private static bool TryCondition(
            AutomationConditionHttpModel? request,
            out AutomationCondition? condition)
        {
            condition = null;
            if (request == null || request.HasUnknownProperties || !Bounded(request.NodeId) ||
                !TryEnum(request.Kind, out AutomationConditionKind kind))
            {
                return false;
            }

            try
            {
                if (kind == AutomationConditionKind.Predicate)
                {
                    if (request.Children != null ||
                        !TryPredicate(request.NodeId!, request.Predicate, out condition))
                    {
                        return false;
                    }
                    return true;
                }

                if (request.Predicate != null || request.Children == null)
                    return false;
                var children = new List<AutomationCondition>(request.Children.Length);
                foreach (var child in request.Children)
                {
                    if (!TryCondition(child, out var mapped)) return false;
                    children.Add(mapped!);
                }

                condition = kind switch
                {
                    AutomationConditionKind.All => AutomationCondition.All(
                        request.NodeId!, children.ToArray()),
                    AutomationConditionKind.Any => AutomationCondition.Any(
                        request.NodeId!, children.ToArray()),
                    AutomationConditionKind.Not when children.Count == 1 => AutomationCondition.Not(
                        request.NodeId!, children[0]),
                    _ => null
                };
                return condition != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryPredicate(
            string nodeId,
            AutomationConditionPredicateHttpModel? request,
            out AutomationCondition? condition)
        {
            condition = null;
            if (request == null || request.HasUnknownProperties || !Bounded(request.FieldKey) ||
                !TryEnum(request.Operator, out AutomationConditionOperator @operator))
            {
                return false;
            }

            var hasScalar = request.ScalarValue != null;
            var hasRange = request.MinimumInclusive.HasValue || request.MaximumInclusive.HasValue;
            var hasSet = request.SetValues != null;
            var hasWindow = request.Window != null;
            try
            {
                switch (@operator)
                {
                    case AutomationConditionOperator.Equals:
                        if (!hasScalar || hasRange || hasSet || hasWindow) return false;
                        condition = AutomationCondition.TextEquals(
                            nodeId, request.FieldKey!, request.ScalarValue!);
                        break;
                    case AutomationConditionOperator.NotEquals:
                        if (!hasScalar || hasRange || hasSet || hasWindow) return false;
                        condition = AutomationCondition.TextNotEquals(
                            nodeId, request.FieldKey!, request.ScalarValue!);
                        break;
                    case AutomationConditionOperator.InSet:
                        if (hasScalar || hasRange || !hasSet || hasWindow) return false;
                        condition = AutomationCondition.InSet(
                            nodeId, request.FieldKey!, request.SetValues!);
                        break;
                    case AutomationConditionOperator.NumberRange:
                        if (hasScalar || !request.MinimumInclusive.HasValue ||
                            !request.MaximumInclusive.HasValue || hasSet || hasWindow)
                            return false;
                        condition = AutomationCondition.NumberRange(
                            nodeId,
                            request.FieldKey!,
                            request.MinimumInclusive.Value,
                            request.MaximumInclusive.Value);
                        break;
                    case AutomationConditionOperator.TimeWindow:
                        if (hasScalar || hasRange || hasSet ||
                            !TryWindow(request.Window, out var window)) return false;
                        condition = AutomationCondition.TimeWindow(
                            nodeId, request.FieldKey!, window!);
                        break;
                    case AutomationConditionOperator.PlayerGroup:
                        if (!hasScalar || hasRange || hasSet || hasWindow) return false;
                        condition = AutomationCondition.PlayerGroup(
                            nodeId, request.FieldKey!, request.ScalarValue!);
                        break;
                    case AutomationConditionOperator.Permission:
                        if (!hasScalar || hasRange || hasSet || hasWindow) return false;
                        condition = AutomationCondition.Permission(
                            nodeId, request.FieldKey!, request.ScalarValue!);
                        break;
                    case AutomationConditionOperator.Cooldown:
                        if (hasScalar || hasRange || hasSet || hasWindow) return false;
                        condition = AutomationCondition.Cooldown(nodeId, request.FieldKey!);
                        break;
                }
                return condition != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryWindow(
            AutomationTimeWindowHttpModel? request,
            out AutomationTimeWindow? window)
        {
            window = null;
            if (request == null || request.HasUnknownProperties || !Bounded(request.TimeZoneId) ||
                !TryTime(request.StartInclusive, out var start) ||
                !TryTime(request.EndInclusive, out var end))
            {
                return false;
            }

            try
            {
                window = new AutomationTimeWindow(request.TimeZoneId!, start!, end!);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryTime(
            AutomationTimeOfDayHttpModel? request,
            out AutomationTimeOfDay? time)
        {
            time = null;
            if (request == null || request.HasUnknownProperties ||
                !request.Hour.HasValue || !request.Minute.HasValue)
            {
                return false;
            }
            try
            {
                time = new AutomationTimeOfDay(request.Hour.Value, request.Minute.Value);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static bool TryAction(
            AutomationActionHttpModel? request,
            out AutomationActionDraft? action)
        {
            action = null;
            if (request == null || request.HasUnknownProperties || !Bounded(request.Id) ||
                !TryTarget(request.Target, out var target) ||
                !KnownActionType(request.Type) || !HasOnlyMatchingActionBody(request))
            {
                return false;
            }

            try
            {
                switch (request.Type)
                {
                    case "BroadcastMessage" when target!.Kind == AutomationTargetKind.Global:
                        action = new BroadcastMessageActionDraft(
                            request.Id!, request.BroadcastMessage!.Message!);
                        break;
                    case "PrivateMessage":
                        action = new PrivateMessageActionDraft(
                            request.Id!, target!, request.PrivateMessage!.Message!);
                        break;
                    case "Announcement" when target!.Kind == AutomationTargetKind.Global:
                        action = new AnnouncementActionDraft(
                            request.Id!, request.Announcement!.Message!);
                        break;
                    case "GrantItem":
                        action = new GrantItemActionDraft(
                            request.Id!,
                            target!,
                            request.GrantItem!.ResourceId!,
                            request.GrantItem.Amount!.Value);
                        break;
                    case "GrantRewardPackage":
                        action = new GrantRewardPackageActionDraft(
                            request.Id!,
                            target!,
                            request.GrantRewardPackage!.RewardPackageId!);
                        break;
                    case "AdjustEconomy":
                        action = new AdjustEconomyActionDraft(
                            request.Id!, target!, request.AdjustEconomy!.Amount!.Value);
                        break;
                    case "KickPlayer":
                        action = new KickPlayerActionDraft(
                            request.Id!, target!, request.KickPlayer!.Reason!);
                        break;
                    case "MutePlayer":
                        action = new MutePlayerActionDraft(
                            request.Id!,
                            target!,
                            TimeSpan.FromSeconds(request.MutePlayer!.DurationSeconds!.Value),
                            request.MutePlayer.Reason!);
                        break;
                    case "RestrictedCommand":
                        action = new RestrictedCommandActionDraft(
                            request.Id!, target!, request.RestrictedCommand!.CommandCatalogKey!);
                        break;
                    case "DiscordMessage":
                        action = new DiscordMessageActionDraft(
                            request.Id!, target!, request.DiscordMessage!.Message!);
                        break;
                }
                return action != null;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is OverflowException)
            {
                return false;
            }
        }

        private static bool TryTarget(
            AutomationTargetHttpModel? request,
            out AutomationTarget? target)
        {
            target = null;
            if (request == null || request.HasUnknownProperties ||
                !TryEnum(request.Kind, out AutomationTargetKind kind))
            {
                return false;
            }

            try
            {
                target = kind switch
                {
                    AutomationTargetKind.Global when request.ReferenceId == null =>
                        AutomationTarget.Global,
                    AutomationTargetKind.TriggerPlayer when request.ReferenceId == null =>
                        AutomationTarget.TriggerPlayer,
                    AutomationTargetKind.StablePlayer when request.ReferenceId != null =>
                        AutomationTarget.StablePlayer(request.ReferenceId),
                    AutomationTargetKind.DiscordTarget when request.ReferenceId != null =>
                        AutomationTarget.DiscordTarget(request.ReferenceId),
                    _ => null
                };
                return target != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool HasOnlyMatchingActionBody(AutomationActionHttpModel request)
        {
            var bodies = new AutomationStrictHttpModel?[]
            {
                request.BroadcastMessage,
                request.PrivateMessage,
                request.Announcement,
                request.GrantItem,
                request.GrantRewardPackage,
                request.AdjustEconomy,
                request.KickPlayer,
                request.MutePlayer,
                request.RestrictedCommand,
                request.DiscordMessage
            };
            if (bodies.Count(body => body != null) != 1 ||
                bodies.Any(body => body?.HasUnknownProperties == true))
            {
                return false;
            }

            return request.Type switch
            {
                "BroadcastMessage" => ValidMessage(request.BroadcastMessage),
                "PrivateMessage" => ValidMessage(request.PrivateMessage),
                "Announcement" => ValidMessage(request.Announcement),
                "GrantItem" => request.GrantItem?.ResourceId != null &&
                    request.GrantItem.Amount.HasValue,
                "GrantRewardPackage" => request.GrantRewardPackage?.RewardPackageId != null,
                "AdjustEconomy" => request.AdjustEconomy?.Amount.HasValue == true,
                "KickPlayer" => request.KickPlayer?.Reason != null,
                "MutePlayer" => request.MutePlayer?.DurationSeconds.HasValue == true &&
                    request.MutePlayer.Reason != null,
                "RestrictedCommand" => request.RestrictedCommand?.CommandCatalogKey != null,
                "DiscordMessage" => ValidMessage(request.DiscordMessage),
                _ => false
            };
        }

        private static bool ValidMessage(AutomationMessageActionHttpModel? value) =>
            value?.Message != null;

        private static bool TryActor(AutomationTriggerActorHttpModel? actor) =>
            actor == null ||
            !actor.HasUnknownProperties &&
            (actor.CrossplatformId == null || Bounded(actor.CrossplatformId)) &&
            (actor.Group == null || Bounded(actor.Group));

        private static bool TryChat(AutomationChatTriggerHttpModel? chat) =>
            chat == null ||
            !chat.HasUnknownProperties &&
            (chat.Text == null || chat.Text.Length <= AutomationAction.MaxTextLength);

        private static bool TryCron(AutomationCronTriggerHttpModel? cron) =>
            cron == null ||
            !cron.HasUnknownProperties &&
            (!cron.ScheduledForUtc.HasValue || Utc(cron.ScheduledForUtc.Value));

        private static bool TryBloodMoon(AutomationBloodMoonTriggerHttpModel? bloodMoon) =>
            bloodMoon == null ||
            !bloodMoon.HasUnknownProperties &&
            (bloodMoon.Phase == null || Bounded(bloodMoon.Phase));

        private static bool SnapshotShapeMatches(
            AutomationTriggerSnapshotHttpModel request,
            AutomationTriggerType triggerType)
        {
            switch (triggerType)
            {
                case AutomationTriggerType.PlayerJoined:
                case AutomationTriggerType.PlayerLeft:
                    return request.Chat == null && request.Cron == null && request.BloodMoon == null;
                case AutomationTriggerType.ChatMessage:
                    return request.Cron == null && request.BloodMoon == null;
                case AutomationTriggerType.Cron:
                    return request.Actor == null && request.Chat == null && request.BloodMoon == null;
                case AutomationTriggerType.BloodMoonPhaseEntered:
                    return request.Actor == null && request.Chat == null && request.Cron == null;
                default:
                    return false;
            }
        }

        private static AutomationConditionHttpModel ToCondition(AutomationCondition condition)
        {
            var response = new AutomationConditionHttpModel
            {
                NodeId = condition.NodeId,
                Kind = condition.Kind.ToString()
            };
            if (condition.Kind != AutomationConditionKind.Predicate)
            {
                response.Children = condition.Children.Select(ToCondition).ToArray();
                return response;
            }

            response.Predicate = new AutomationConditionPredicateHttpModel
            {
                FieldKey = condition.FieldKey,
                Operator = condition.Operator?.ToString(),
                ScalarValue = condition.ScalarValue,
                MinimumInclusive = condition.MinimumInclusive,
                MaximumInclusive = condition.MaximumInclusive,
                SetValues = condition.Operator == AutomationConditionOperator.InSet
                    ? condition.SetValues.ToArray()
                    : null,
                Window = condition.Window == null
                    ? null
                    : new AutomationTimeWindowHttpModel
                    {
                        TimeZoneId = condition.Window.TimeZoneId,
                        StartInclusive = new AutomationTimeOfDayHttpModel
                        {
                            Hour = condition.Window.StartInclusive.Hour,
                            Minute = condition.Window.StartInclusive.Minute
                        },
                        EndInclusive = new AutomationTimeOfDayHttpModel
                        {
                            Hour = condition.Window.EndInclusive.Hour,
                            Minute = condition.Window.EndInclusive.Minute
                        }
                    }
            };
            return response;
        }

        private static AutomationActionHttpModel ToAction(AutomationAction action)
        {
            var response = new AutomationActionHttpModel
            {
                Id = action.Id,
                Type = SafeActionType(action.Type),
                Target = ToTarget(action)
            };
            switch (response.Type)
            {
                case "BroadcastMessage":
                    response.BroadcastMessage = Message(action.TextValue);
                    break;
                case "PrivateMessage":
                    response.PrivateMessage = Message(action.TextValue);
                    break;
                case "Announcement":
                    response.Announcement = Message(action.TextValue);
                    break;
                case "GrantItem":
                    response.GrantItem = new AutomationGrantItemActionHttpModel
                    {
                        ResourceId = action.TextValue,
                        Amount = action.Amount
                    };
                    break;
                case "GrantRewardPackage":
                    response.GrantRewardPackage = new AutomationGrantRewardPackageActionHttpModel
                    {
                        RewardPackageId = action.TextValue
                    };
                    break;
                case "AdjustEconomy":
                    response.AdjustEconomy = new AutomationAmountActionHttpModel
                    {
                        Amount = action.Amount
                    };
                    break;
                case "KickPlayer":
                    response.KickPlayer = new AutomationReasonActionHttpModel
                    {
                        Reason = action.TextValue
                    };
                    break;
                case "MutePlayer":
                    response.MutePlayer = new AutomationMutePlayerActionHttpModel
                    {
                        DurationSeconds = action.Duration.HasValue
                            ? checked((long)action.Duration.Value.TotalSeconds)
                            : null,
                        Reason = action.TextValue
                    };
                    break;
                case "RestrictedCommand":
                    response.RestrictedCommand = new AutomationRestrictedCommandActionHttpModel
                    {
                        CommandCatalogKey = action.TextValue
                    };
                    break;
                case "DiscordMessage":
                    response.DiscordMessage = Message(action.TextValue);
                    break;
            }
            return response;
        }

        private static AutomationMessageActionHttpModel Message(string? value) =>
            new() { Message = value };

        private static AutomationTargetHttpModel ToTarget(AutomationAction action)
        {
            if (!TryEnum(action.TargetKind, out AutomationTargetKind kind))
                return new AutomationTargetHttpModel { Kind = "Unsupported" };
            return new AutomationTargetHttpModel
            {
                Kind = kind.ToString(),
                ReferenceId = kind == AutomationTargetKind.StablePlayer ||
                    kind == AutomationTargetKind.DiscordTarget
                        ? action.ReferenceId
                        : null
            };
        }

        private static string SafeTriggerType(string value) =>
            TryEnum(value, out AutomationTriggerType type) ? type.ToString() : "Unsupported";

        private static string SafeActionType(string value) =>
            KnownActionType(value) ? value : "Unsupported";

        private static string SafeDependencyStatus(AutomationDependencyStatus status) =>
            Enum.IsDefined(typeof(AutomationDependencyStatus), status)
                ? status.ToString()
                : AutomationDependencyStatus.Unavailable.ToString();

        private static string? SafeCode(string? value) =>
            value != null && value.Length <= 128 && value.All(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-' || character == '.')
                    ? value
                    : null;

        private static bool KnownActionType(string? value) =>
            value == "BroadcastMessage" || value == "PrivateMessage" ||
            value == "Announcement" || value == "GrantItem" ||
            value == "GrantRewardPackage" || value == "AdjustEconomy" ||
            value == "KickPlayer" || value == "MutePlayer" ||
            value == "RestrictedCommand" || value == "DiscordMessage";

        private static bool TryEnum<T>(string? value, out T result)
            where T : struct, Enum
        {
            result = default;
            return value != null &&
                Enum.GetNames(typeof(T)).Contains(value, StringComparer.Ordinal) &&
                Enum.TryParse(value, false, out result) &&
                Enum.IsDefined(typeof(T), result);
        }

        private static bool Bounded(string? value) =>
            value != null && !string.IsNullOrWhiteSpace(value) &&
            value.Length <= AutomationCondition.MaxStringLength;

        private static bool Utc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;
    }
}
