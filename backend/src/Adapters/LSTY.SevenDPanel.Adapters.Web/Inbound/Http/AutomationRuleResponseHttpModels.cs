using System;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
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
}
