using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed class AutomationRuleValidator
    {
        public const int MaxConditionDepth = 5;
        public const int MaxConditionNodes = 64;
        public const int MaxConditionStringLength = 256;
        public const int MaxConditionSetValues = 50;

        private static readonly ISet<string> ActionTypes =
            new HashSet<string>(new[]
            {
                "BroadcastMessage",
                "PrivateMessage",
                "Announcement",
                "GrantItem",
                "GrantRewardPackage",
                "AdjustEconomy",
                "KickPlayer",
                "MutePlayer",
                "RestrictedCommand",
                "DiscordMessage"
            }, StringComparer.Ordinal);

        private readonly AutomationFieldCatalog fieldCatalog;
        private readonly IAutomationDependencyCatalog dependencyCatalog;

        public AutomationRuleValidator(
            AutomationFieldCatalog fieldCatalog,
            IAutomationDependencyCatalog dependencyCatalog)
        {
            this.fieldCatalog = fieldCatalog ?? throw new ArgumentNullException(nameof(fieldCatalog));
            this.dependencyCatalog = dependencyCatalog ??
                throw new ArgumentNullException(nameof(dependencyCatalog));
        }

        public AutomationValidationResult Validate(
            AutomationRuleDraft rule,
            AuthenticatedActor actor)
        {
            AutomationAuthorization.RequireOwner(actor);
            if (rule == null) throw new ArgumentNullException(nameof(rule));

            var issues = new List<AutomationValidationIssue>();
            if (string.IsNullOrWhiteSpace(rule.Id) ||
                rule.Id.Length > AutomationCondition.MaxStringLength)
            {
                issues.Add(Issue("automation_rule_id_invalid", "id"));
            }
            if (rule.ExpectedVersion < 0)
                issues.Add(Issue("automation_rule_version_invalid", "expectedVersion"));
            if (string.IsNullOrWhiteSpace(rule.Name) ||
                rule.Name.Length > AutomationRule.MaxNameLength)
            {
                issues.Add(Issue("automation_rule_name_invalid", "name"));
            }
            if (!Enum.IsDefined(typeof(AutomationTriggerType), rule.TriggerType))
                issues.Add(Issue("automation_trigger_type_invalid", "triggerType"));
            if (rule.CooldownDuration < TimeSpan.Zero ||
                rule.CooldownDuration.Ticks % TimeSpan.TicksPerSecond != 0)
            {
                issues.Add(Issue("automation_cooldown_invalid", "cooldownDuration"));
            }
            if (!Enum.IsDefined(typeof(AutomationCooldownScope), rule.CooldownScope))
                issues.Add(Issue("automation_cooldown_scope_invalid", "cooldownScope"));
            if (!Enum.IsDefined(typeof(AutomationConcurrencyPolicy), rule.ConcurrencyPolicy))
                issues.Add(Issue("automation_concurrency_policy_invalid", "concurrencyPolicy"));
            if (!Enum.IsDefined(typeof(AutomationFailurePolicy), rule.FailurePolicy))
                issues.Add(Issue("automation_failure_policy_invalid", "failurePolicy"));

            ValidateCondition(rule, issues);
            ValidateActions(rule, issues);
            return issues.Count == 0
                ? AutomationValidationResult.Valid
                : new AutomationValidationResult(issues);
        }

        private void ValidateCondition(
            AutomationRuleDraft rule,
            ICollection<AutomationValidationIssue> issues)
        {
            if (rule.ConditionRoot.Depth > MaxConditionDepth)
            {
                issues.Add(Issue(
                    "automation_condition_tree_too_deep",
                    "condition"));
            }
            if (rule.ConditionRoot.NodeCount > MaxConditionNodes)
            {
                issues.Add(Issue(
                    "automation_condition_tree_too_large",
                    "condition"));
            }

            Visit(rule.ConditionRoot, condition =>
            {
                if (condition.Kind != AutomationConditionKind.Predicate)
                    return;
                var path = "condition." + condition.NodeId;
                var definition = condition.FieldKey == null
                    ? null
                    : fieldCatalog.Find(rule.TriggerType, condition.FieldKey);
                if (definition == null)
                {
                    issues.Add(Issue("automation_trigger_field_not_allowed", path));
                    return;
                }
                if (!condition.Operator.HasValue ||
                    !definition.AllowedOperators.Contains(condition.Operator.Value))
                {
                    issues.Add(Issue("automation_condition_operator_not_allowed", path));
                }
                if (condition.ScalarValue != null &&
                    condition.ScalarValue.Length > MaxConditionStringLength)
                {
                    issues.Add(Issue("automation_condition_string_too_long", path));
                }
                if (condition.SetValues.Count > MaxConditionSetValues)
                    issues.Add(Issue("automation_condition_set_too_large", path));
            });
        }

        private void ValidateActions(
            AutomationRuleDraft rule,
            ICollection<AutomationValidationIssue> issues)
        {
            if (rule.Actions.Count == 0 || rule.Actions.Count > AutomationRule.MaxActionCount)
            {
                issues.Add(Issue("automation_action_count_invalid", "actions"));
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < rule.Actions.Count; index++)
            {
                var draft = rule.Actions[index];
                var path = "actions[" + index + "]";
                if (!ids.Add(draft.Id))
                    issues.Add(Issue("automation_action_id_duplicate", path));
                if (!ActionTypes.Contains(draft.Type))
                {
                    issues.Add(Issue("automation_action_type_not_allowed", path));
                    continue;
                }

                var action = draft.ToDomainAction();
                var dependency = dependencyCatalog.Resolve(action) ??
                    throw new InvalidOperationException("automation_dependency_state_missing");
                if (!dependency.IsReady)
                {
                    issues.Add(Issue(
                        dependency.ErrorCode ?? "automation_dependency_unavailable",
                        path));
                }
            }
        }

        private static void Visit(
            AutomationCondition condition,
            Action<AutomationCondition> visitor)
        {
            visitor(condition);
            foreach (var child in condition.Children)
                Visit(child, visitor);
        }

        private static AutomationValidationIssue Issue(string code, string path) =>
            new(code, path);
    }
}
