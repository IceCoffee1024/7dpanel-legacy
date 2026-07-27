using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed class AutomationRuleUseCases
    {
        private readonly IAutomationStore store;
        private readonly AutomationRuleValidator validator;
        private readonly Func<DateTimeOffset> utcNow;

        public AutomationRuleUseCases(
            IAutomationStore store,
            AutomationRuleValidator validator)
            : this(store, validator, () => DateTimeOffset.UtcNow)
        {
        }

        public AutomationRuleUseCases(
            IAutomationStore store,
            AutomationRuleValidator validator,
            Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public IReadOnlyList<AutomationRule> List(AuthenticatedActor actor)
        {
            AutomationAuthorization.RequireOwner(actor);
            return store.ListRules();
        }

        public AutomationRule? Find(string ruleId, AuthenticatedActor actor)
        {
            AutomationAuthorization.RequireOwner(actor);
            if (string.IsNullOrWhiteSpace(ruleId))
                throw new ArgumentException("A rule ID is required.", nameof(ruleId));
            return store.FindRule(ruleId);
        }

        public AutomationValidationResult Validate(
            AutomationRuleDraft rule,
            AuthenticatedActor actor) =>
            validator.Validate(rule, actor);

        public AutomationRule Create(
            AutomationRuleDraft rule,
            AuthenticatedActor actor)
        {
            var validation = validator.Validate(rule, actor);
            if (rule.ExpectedVersion != 0)
            {
                validation = Append(
                    validation,
                    new AutomationValidationIssue(
                        "automation_create_version_invalid",
                        "expectedVersion"));
            }
            ThrowIfInvalid(validation);

            var now = GetUtcNow();
            var created = Build(rule, 1, now, now);
            store.SaveRule(created, 0);
            return created;
        }

        public AutomationRule Update(
            AutomationRuleDraft rule,
            AuthenticatedActor actor)
        {
            var validation = validator.Validate(rule, actor);
            if (rule.ExpectedVersion <= 0)
            {
                validation = Append(
                    validation,
                    new AutomationValidationIssue(
                        "automation_update_version_invalid",
                        "expectedVersion"));
            }
            ThrowIfInvalid(validation);

            var current = store.FindRule(rule.Id);
            if (current == null || current.Version != rule.ExpectedVersion)
                throw new AutomationVersionConflictException();
            var now = GetUtcNow();
            if (now < current.CreatedAtUtc)
                throw new InvalidOperationException("automation_clock_moved_before_creation");

            var updated = Build(
                rule,
                checked(rule.ExpectedVersion + 1),
                current.CreatedAtUtc,
                now);
            store.SaveRule(updated, rule.ExpectedVersion);
            return updated;
        }

        public void Delete(
            string ruleId,
            long expectedVersion,
            AuthenticatedActor actor)
        {
            AutomationAuthorization.RequireOwner(actor);
            if (string.IsNullOrWhiteSpace(ruleId) ||
                ruleId.Length > AutomationCondition.MaxStringLength)
            {
                throw new ArgumentException("A bounded rule ID is required.", nameof(ruleId));
            }
            if (expectedVersion <= 0 || expectedVersion == long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(expectedVersion));

            var current = store.FindRule(ruleId);
            if (current == null || current.Version != expectedVersion)
                throw new AutomationVersionConflictException();
            var deletedAtUtc = GetUtcNow();
            if (deletedAtUtc < current.CreatedAtUtc)
                throw new InvalidOperationException("automation_clock_moved_before_creation");
            store.DeleteRule(ruleId, expectedVersion, deletedAtUtc);
        }

        private DateTimeOffset GetUtcNow()
        {
            var value = utcNow();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("automation_clock_must_be_utc");
            return value;
        }

        private static AutomationRule Build(
            AutomationRuleDraft draft,
            long version,
            DateTimeOffset createdAtUtc,
            DateTimeOffset updatedAtUtc) =>
            new(
                draft.Id,
                version,
                draft.Name,
                draft.IsEnabled,
                new AutomationTrigger(draft.TriggerType.ToString()),
                draft.ConditionRoot,
                draft.ToDomainActions(),
                draft.CooldownDuration,
                draft.CooldownScope,
                draft.ConcurrencyPolicy,
                draft.FailurePolicy,
                createdAtUtc,
                updatedAtUtc);

        private static void ThrowIfInvalid(AutomationValidationResult validation)
        {
            if (!validation.IsValid)
                throw new AutomationRuleValidationException(validation);
        }

        private static AutomationValidationResult Append(
            AutomationValidationResult validation,
            AutomationValidationIssue issue)
        {
            var issues = new List<AutomationValidationIssue>(validation.Issues)
            {
                issue
            };
            return new AutomationValidationResult(issues);
        }
    }
}
