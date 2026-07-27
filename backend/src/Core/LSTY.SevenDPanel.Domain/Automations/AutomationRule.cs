using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Domain.Automations
{
    public sealed class AutomationTrigger
    {
        public AutomationTrigger(string type)
        {
            Type = AutomationRule.RequireString(type, nameof(type));
        }

        public string Type { get; }
    }

    public sealed class AutomationAction
    {
        public const int MaxTextLength = 512;

        public AutomationAction(
            string id,
            string type,
            string targetKind,
            string? textValue = null,
            string? referenceId = null,
            long? amount = null,
            TimeSpan? duration = null)
        {
            Id = AutomationRule.RequireString(id, nameof(id));
            Type = AutomationRule.RequireString(type, nameof(type));
            TargetKind = AutomationRule.RequireString(targetKind, nameof(targetKind));
            if (textValue != null && textValue.Length > MaxTextLength)
                throw new ArgumentException("The action text is too long.", nameof(textValue));
            if (referenceId != null)
                referenceId = AutomationRule.RequireString(referenceId, nameof(referenceId));
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));

            TextValue = textValue;
            ReferenceId = referenceId;
            Amount = amount;
            Duration = duration;
        }

        public string Id { get; }
        public string Type { get; }
        public string TargetKind { get; }
        public string? TextValue { get; }
        public string? ReferenceId { get; }
        public long? Amount { get; }
        public TimeSpan? Duration { get; }
    }

    public sealed class AutomationRule
    {
        public const int MaxNameLength = 128;
        public const int MaxActionCount = 32;

        public AutomationRule(
            string id,
            long version,
            string name,
            bool isEnabled,
            AutomationTrigger trigger,
            AutomationCondition conditionRoot,
            IEnumerable<AutomationAction> actions,
            TimeSpan cooldownDuration,
            AutomationCooldownScope cooldownScope,
            AutomationConcurrencyPolicy concurrencyPolicy,
            AutomationFailurePolicy failurePolicy,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc)
        {
            Id = RequireString(id, nameof(id));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
                throw new ArgumentException("A bounded rule name is required.", nameof(name));
            Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
            ConditionRoot = conditionRoot ?? throw new ArgumentNullException(nameof(conditionRoot));
            if (actions == null) throw new ArgumentNullException(nameof(actions));

            var copiedActions = actions.ToArray();
            if (copiedActions.Length == 0 || copiedActions.Length > MaxActionCount)
                throw new ArgumentException("The rule has an invalid action count.", nameof(actions));
            if (copiedActions.Any(action => action == null))
                throw new ArgumentException("Rule actions cannot be null.", nameof(actions));
            if (copiedActions.Select(action => action.Id).Distinct(StringComparer.Ordinal).Count() !=
                copiedActions.Length)
            {
                throw new ArgumentException("Rule action IDs must be unique.", nameof(actions));
            }

            if (cooldownDuration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cooldownDuration));
            RequireDefined(cooldownScope, nameof(cooldownScope));
            RequireDefined(concurrencyPolicy, nameof(concurrencyPolicy));
            RequireDefined(failurePolicy, nameof(failurePolicy));
            RequireUtc(createdAtUtc, nameof(createdAtUtc));
            RequireUtc(updatedAtUtc, nameof(updatedAtUtc));
            if (updatedAtUtc < createdAtUtc)
                throw new ArgumentException("Updated UTC must not precede created UTC.", nameof(updatedAtUtc));

            Version = version;
            Name = name;
            IsEnabled = isEnabled;
            Actions = new ReadOnlyCollection<AutomationAction>(copiedActions);
            CooldownDuration = cooldownDuration;
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
        public AutomationTrigger Trigger { get; }
        public AutomationCondition ConditionRoot { get; }
        public IReadOnlyList<AutomationAction> Actions { get; }
        public TimeSpan CooldownDuration { get; }
        public AutomationCooldownScope CooldownScope { get; }
        public AutomationConcurrencyPolicy ConcurrencyPolicy { get; }
        public AutomationFailurePolicy FailurePolicy { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; }

        internal static string RequireString(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > AutomationCondition.MaxStringLength)
            {
                throw new ArgumentException("A bounded value is required.", parameterName);
            }
            return value;
        }

        private static void RequireDefined<T>(T value, string parameterName)
            where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("The timestamp must use UTC.", parameterName);
        }
    }
}
