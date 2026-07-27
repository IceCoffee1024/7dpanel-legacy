using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed class DryRunAutomationRuleUseCase
    {
        private readonly AutomationRuleValidator validator;
        private readonly AutomationConditionEvaluator evaluator;
        private readonly IAutomationDependencyCatalog dependencyCatalog;
        private readonly IAutomationTargetResolver targetResolver;

        public DryRunAutomationRuleUseCase(
            AutomationRuleValidator validator,
            AutomationConditionEvaluator evaluator,
            IAutomationDependencyCatalog dependencyCatalog,
            IAutomationTargetResolver targetResolver)
        {
            this.validator = validator ?? throw new ArgumentNullException(nameof(validator));
            this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            this.dependencyCatalog = dependencyCatalog ??
                throw new ArgumentNullException(nameof(dependencyCatalog));
            this.targetResolver = targetResolver ??
                throw new ArgumentNullException(nameof(targetResolver));
        }

        public AutomationDryRunResult Execute(
            AutomationRuleDraft rule,
            AutomationTriggerSnapshot snapshot,
            AuthenticatedActor actor)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var validation = validator.Validate(rule, actor);
            if (!string.Equals(
                snapshot.TriggerType,
                rule.TriggerType.ToString(),
                StringComparison.Ordinal))
            {
                validation = Append(
                    validation,
                    new AutomationValidationIssue(
                        "automation_snapshot_trigger_mismatch",
                        "snapshot.triggerType"));
            }
            if (!validation.IsValid)
            {
                return new AutomationDryRunResult(
                    validation,
                    null,
                    Array.Empty<AutomationPlannedAction>());
            }

            var evaluation = evaluator.Evaluate(
                rule.ConditionRoot,
                rule.TriggerType,
                snapshot);
            var actions = rule.ToDomainActions();
            var planned = new List<AutomationPlannedAction>(actions.Count);
            for (var index = 0; index < actions.Count; index++)
            {
                var action = actions[index];
                var dependency = dependencyCatalog.Resolve(action) ??
                    throw new InvalidOperationException("automation_dependency_state_missing");
                var target = targetResolver.Resolve(action, snapshot) ??
                    throw new InvalidOperationException("automation_target_resolution_missing");
                planned.Add(new AutomationPlannedAction(
                    index,
                    action.Id,
                    action.Type,
                    dependency,
                    target,
                    evaluation.Truth == AutomationTruth.Matched &&
                    dependency.IsReady &&
                    target.IsResolved));
            }

            return new AutomationDryRunResult(
                validation,
                evaluation,
                new ReadOnlyCollection<AutomationPlannedAction>(planned));
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
