using System;

namespace LSTY.SevenDPanel.Domain.Automations
{
    public enum AutomationCooldownScope
    {
        Rule,
        RulePlayer
    }

    public enum AutomationConcurrencyPolicy
    {
        SkipIfRunning,
        QueueOne
    }

    public enum AutomationFailurePolicy
    {
        StopOnFailure,
        Continue
    }

    public enum AutomationConcurrencyDecision
    {
        Start,
        Skip,
        Queue
    }

    public sealed class AutomationCooldownKey
    {
        private const int MaxKeyPartLength = 128;

        private AutomationCooldownKey(
            AutomationCooldownScope scope,
            string ruleId,
            string? stablePlayerId,
            string value)
        {
            Scope = scope;
            RuleId = ruleId;
            StablePlayerId = stablePlayerId;
            Value = value;
        }

        public AutomationCooldownScope Scope { get; }
        public string RuleId { get; }
        public string? StablePlayerId { get; }
        public string Value { get; }

        public static AutomationCooldownKey Create(
            AutomationCooldownScope scope,
            string ruleId,
            string? stablePlayerId = null)
        {
            RequireDefined(scope, nameof(scope));
            ruleId = RequireKeyPart(ruleId, nameof(ruleId));
            if (scope == AutomationCooldownScope.Rule)
            {
                return new AutomationCooldownKey(
                    scope,
                    ruleId,
                    null,
                    "rule|" + ruleId.Length + "|" + ruleId);
            }

            var requiredPlayerId = RequireKeyPart(stablePlayerId, nameof(stablePlayerId));
            return new AutomationCooldownKey(
                scope,
                ruleId,
                requiredPlayerId,
                "rule-player|" + ruleId.Length + "|" + ruleId + "|" +
                requiredPlayerId.Length + "|" + requiredPlayerId);
        }

        private static string RequireKeyPart(string? value, string parameterName)
        {
            if (value == null ||
                string.IsNullOrWhiteSpace(value) ||
                value.Length > MaxKeyPartLength)
                throw new ArgumentException("A bounded stable key value is required.", parameterName);
            return value!;
        }

        private static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public static class AutomationExecutionPolicy
    {
        public static AutomationConcurrencyDecision DecideConcurrency(
            AutomationConcurrencyPolicy policy,
            bool isRunning,
            bool hasQueued)
        {
            RequireDefined(policy, nameof(policy));
            if (hasQueued) return AutomationConcurrencyDecision.Skip;
            if (!isRunning) return AutomationConcurrencyDecision.Start;
            return policy == AutomationConcurrencyPolicy.SkipIfRunning
                ? AutomationConcurrencyDecision.Skip
                : AutomationConcurrencyDecision.Queue;
        }

        public static bool ShouldContinueAfterAction(
            AutomationFailurePolicy policy,
            bool actionSucceeded)
        {
            RequireDefined(policy, nameof(policy));
            return actionSucceeded || policy == AutomationFailurePolicy.Continue;
        }

        private static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
