using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Domain.Automations;

namespace LSTY.SevenDPanel.Application.Automations
{
    public sealed record AutomationExecutionOutcome(
        string ExecutionId,
        string RuleId,
        string TriggerId,
        AutomationExecutionStatus Status,
        string? ErrorCode,
        bool WasCreated);

    public sealed class AutomationExecutionEngine
    {
        private readonly IAutomationStore store;
        private readonly IAutomationExecutionStateStore? executionStates;
        private readonly AutomationConditionEvaluator evaluator;
        private readonly IAutomationDependencyCatalog dependencies;
        private readonly IAutomationTargetResolver targets;
        private readonly IAutomationActionDispatcher dispatcher;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly object statesSync = new();
        private readonly Dictionary<string, RuleRuntimeState> states =
            new(StringComparer.Ordinal);

        public AutomationExecutionEngine(
            IAutomationStore store,
            AutomationConditionEvaluator evaluator,
            IAutomationDependencyCatalog dependencies,
            IAutomationTargetResolver targets,
            IAutomationActionDispatcher dispatcher,
            Func<DateTimeOffset>? utcNow = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            executionStates = store as IAutomationExecutionStateStore;
            this.evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            this.targets = targets ?? throw new ArgumentNullException(nameof(targets));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }

        public async Task<IReadOnlyList<AutomationExecutionOutcome>> ExecuteAsync(
            AutomationTriggerSnapshot trigger,
            CancellationToken cancellationToken)
        {
            ValidateTrigger(trigger);
            store.SaveTrigger(trigger);
            if (!Enum.TryParse<AutomationTriggerType>(trigger.TriggerType, false, out var triggerType) ||
                !Enum.IsDefined(typeof(AutomationTriggerType), triggerType))
            {
                throw new InvalidOperationException("automation_trigger_type_invalid");
            }

            var rules = store.ListRules()
                .Where(rule => rule.IsEnabled &&
                    string.Equals(rule.Trigger.Type, trigger.TriggerType, StringComparison.Ordinal))
                .ToArray();
            var outcomes = new List<AutomationExecutionOutcome>(rules.Length);
            foreach (var rule in rules)
            {
                outcomes.Add(await ExecuteRuleAsync(
                    rule,
                    triggerType,
                    trigger,
                    cancellationToken).ConfigureAwait(false));
            }
            return outcomes;
        }

        public void RestoreCooldowns(IReadOnlyList<AutomationCooldownEvidence> evidence)
        {
            if (evidence == null) throw new ArgumentNullException(nameof(evidence));
            var now = UtcNow();
            var rules = store.ListRules().ToDictionary(rule => rule.Id, StringComparer.Ordinal);
            foreach (var item in evidence)
            {
                if (item == null)
                    throw new ArgumentException("Cooldown evidence cannot contain null.", nameof(evidence));
                if (item.StartedAtUtc.Offset != TimeSpan.Zero)
                    throw new ArgumentException("Cooldown evidence must use UTC.", nameof(evidence));
                if (!rules.TryGetValue(item.RuleId, out var rule) ||
                    rule.CooldownDuration == TimeSpan.Zero)
                {
                    continue;
                }

                AutomationCooldownKey cooldownKey;
                try
                {
                    cooldownKey = AutomationCooldownKey.Create(
                        rule.CooldownScope,
                        rule.Id,
                        item.ActorCrossplatformId);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                var until = item.StartedAtUtc.Add(rule.CooldownDuration);
                if (until <= now) continue;
                var state = StateFor(rule.Id);
                lock (state.Sync)
                {
                    if (!state.Cooldowns.TryGetValue(cooldownKey.Value, out var existing) ||
                        until > existing)
                    {
                        state.Cooldowns[cooldownKey.Value] = until;
                    }
                }
            }
        }

        public async Task<AutomationExecutionOutcome> RecoverAsync(
            AutomationRule rule,
            AutomationTriggerSnapshot trigger,
            AutomationExecutionRecord execution,
            CancellationToken cancellationToken)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            ValidateTrigger(trigger);
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            var expectedId = CreateExecutionId(rule.Id, trigger.TriggerId);
            if (!string.Equals(execution.ExecutionId, expectedId, StringComparison.Ordinal) ||
                !string.Equals(execution.RuleId, rule.Id, StringComparison.Ordinal) ||
                !string.Equals(execution.TriggerId, trigger.TriggerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("automation_recovery_identity_mismatch");
            }

            var existing = store.ListActionResults(execution.ExecutionId)
                .ToDictionary(result => result.Ordinal);
            var sawFailure = false;
            var sawUnknown = false;
            for (var ordinal = 0; ordinal < rule.Actions.Count; ordinal++)
            {
                var action = rule.Actions[ordinal];
                if (existing.TryGetValue(ordinal, out var result))
                {
                    if (result.Status == AutomationActionResultStatus.Succeeded)
                        continue;
                    if (result.Status == AutomationActionResultStatus.Running)
                    {
                        RecordRecoveryUnknown(
                            execution.ExecutionId,
                            ordinal,
                            action,
                            result.StartedAtUtc,
                            "automation_recovery_started_result_unknown");
                        sawUnknown = true;
                    }
                    else if (result.Status == AutomationActionResultStatus.ResultUnknown)
                    {
                        sawUnknown = true;
                    }
                    else if (result.Status == AutomationActionResultStatus.Failed)
                    {
                        sawFailure = true;
                    }
                    else if (result.Status == AutomationActionResultStatus.Pending)
                    {
                        var recovered = await RecoverNotStartedAsync(
                            execution.ExecutionId,
                            ordinal,
                            rule,
                            action,
                            trigger,
                            result.StartedAtUtc,
                            cancellationToken).ConfigureAwait(false);
                        sawFailure |= recovered == AutomationActionResultStatus.Failed;
                        sawUnknown |= recovered == AutomationActionResultStatus.ResultUnknown;
                    }
                }
                else
                {
                    var recovered = await RecoverNotStartedAsync(
                        execution.ExecutionId,
                        ordinal,
                        rule,
                        action,
                        trigger,
                        UtcNow(),
                        cancellationToken).ConfigureAwait(false);
                    sawFailure |= recovered == AutomationActionResultStatus.Failed;
                    sawUnknown |= recovered == AutomationActionResultStatus.ResultUnknown;
                }

                if ((sawFailure || sawUnknown) &&
                    rule.FailurePolicy == AutomationFailurePolicy.StopOnFailure)
                {
                    break;
                }
            }

            return CompleteOutcome(
                execution.ExecutionId,
                rule,
                trigger,
                sawUnknown
                    ? AutomationExecutionStatus.ResultUnknown
                    : sawFailure
                        ? AutomationExecutionStatus.Failed
                        : AutomationExecutionStatus.Succeeded,
                sawUnknown ? "automation_recovery_review_required" : null,
                true,
                execution.Status);
        }

        public static string CreateExecutionId(string ruleId, string triggerId)
        {
            RequireStableId(ruleId, nameof(ruleId));
            RequireStableId(triggerId, nameof(triggerId));
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(ruleId + "\n" + triggerId));
            var text = new StringBuilder(hash.Length * 2);
            foreach (var value in hash) text.Append(value.ToString("x2"));
            return text.ToString();
        }

        public static string CreateConsumerIdempotencyKey(string executionId, int actionOrdinal)
        {
            RequireStableId(executionId, nameof(executionId));
            if (actionOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(actionOrdinal));
            return executionId + ":" + actionOrdinal;
        }

        private async Task<AutomationExecutionOutcome> ExecuteRuleAsync(
            AutomationRule rule,
            AutomationTriggerType triggerType,
            AutomationTriggerSnapshot trigger,
            CancellationToken cancellationToken)
        {
            var executionId = CreateExecutionId(rule.Id, trigger.TriggerId);
            var start = store.TryStartExecution(new AutomationExecutionRecord(
                executionId,
                rule.Id,
                trigger.TriggerId,
                AutomationExecutionStatus.Pending,
                executionId,
                null,
                null,
                null));
            if (!start.WasCreated)
            {
                return Outcome(
                    start.Execution.ExecutionId,
                    rule,
                    trigger,
                    start.Execution.Status,
                    start.Execution.ErrorCode,
                    false);
            }

            var evaluation = evaluator.Evaluate(rule.ConditionRoot, triggerType, trigger);
            foreach (var trace in evaluation.Trace)
            {
                store.RecordConditionResult(new AutomationConditionExecutionResult(
                    executionId,
                    trace.NodeId,
                    trace.Truth,
                    trace.IsValueKnown ? "known" : "unknown"));
            }
            if (evaluation.Truth != AutomationTruth.Matched)
            {
                return CompleteOutcome(
                    executionId,
                    rule,
                    trigger,
                    AutomationExecutionStatus.Skipped,
                    evaluation.Truth == AutomationTruth.Unknown
                        ? "automation_condition_unknown"
                        : "automation_condition_not_matched",
                    true,
                    AutomationExecutionStatus.Pending);
            }

            var acquisition = await AcquireAsync(rule, trigger).ConfigureAwait(false);
            if (acquisition.Lease == null)
            {
                return CompleteOutcome(
                    executionId,
                    rule,
                    trigger,
                    AutomationExecutionStatus.Skipped,
                    acquisition.ErrorCode,
                    true,
                    AutomationExecutionStatus.Pending);
            }

            using (acquisition.Lease)
            {
                if (executionStates != null && !executionStates.TryMarkExecutionRunning(
                        executionId,
                        AutomationExecutionStatus.Pending,
                        UtcNow()))
                {
                    throw new InvalidOperationException("automation_execution_state_conflict");
                }
                if (cancellationToken.IsCancellationRequested)
                {
                    return CompleteOutcome(
                        executionId,
                        rule,
                        trigger,
                        AutomationExecutionStatus.Failed,
                        "automation_execution_cancelled",
                        true,
                        executionStates == null
                            ? (AutomationExecutionStatus?)null
                            : AutomationExecutionStatus.Running);
                }
                var status = await ExecuteActionsAsync(
                    executionId,
                    rule,
                    trigger,
                    cancellationToken).ConfigureAwait(false);
                return CompleteOutcome(
                    executionId,
                    rule,
                    trigger,
                    status,
                    status == AutomationExecutionStatus.ResultUnknown
                        ? "automation_action_result_unknown"
                        : status == AutomationExecutionStatus.Failed
                            ? "automation_action_failed"
                            : null,
                    true,
                    executionStates == null
                        ? (AutomationExecutionStatus?)null
                        : AutomationExecutionStatus.Running);
            }
        }

        private async Task<AutomationExecutionStatus> ExecuteActionsAsync(
            string executionId,
            AutomationRule rule,
            AutomationTriggerSnapshot trigger,
            CancellationToken cancellationToken)
        {
            var sawFailure = false;
            var sawUnknown = false;
            for (var ordinal = 0; ordinal < rule.Actions.Count; ordinal++)
            {
                var status = await ExecuteActionAsync(
                    executionId,
                    ordinal,
                    rule,
                    rule.Actions[ordinal],
                    trigger,
                    cancellationToken).ConfigureAwait(false);
                sawFailure |= status == AutomationActionResultStatus.Failed;
                sawUnknown |= status == AutomationActionResultStatus.ResultUnknown;
                if (!AutomationExecutionPolicy.ShouldContinueAfterAction(
                        rule.FailurePolicy,
                        status == AutomationActionResultStatus.Succeeded))
                {
                    break;
                }
            }
            return sawUnknown
                ? AutomationExecutionStatus.ResultUnknown
                : sawFailure
                    ? AutomationExecutionStatus.Failed
                    : AutomationExecutionStatus.Succeeded;
        }

        private async Task<AutomationActionResultStatus> ExecuteActionAsync(
            string executionId,
            int ordinal,
            AutomationRule rule,
            AutomationAction action,
            AutomationTriggerSnapshot trigger,
            CancellationToken cancellationToken)
        {
            var key = CreateConsumerIdempotencyKey(executionId, ordinal);
            var dependency = dependencies.Resolve(action);
            if (!dependency.IsReady)
            {
                RecordTerminal(
                    executionId,
                    ordinal,
                    action,
                    key,
                    AutomationActionResultStatus.Failed,
                    dependency.ErrorCode ?? "automation_dependency_unavailable",
                    UtcNow());
                return AutomationActionResultStatus.Failed;
            }

            var target = targets.Resolve(action, trigger);
            if (!target.IsResolved || target.ResolvedId == null)
            {
                RecordTerminal(
                    executionId,
                    ordinal,
                    action,
                    key,
                    AutomationActionResultStatus.Failed,
                    target.ErrorCode ?? "automation_target_invalid",
                    UtcNow());
                return AutomationActionResultStatus.Failed;
            }

            var startedAt = UtcNow();
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                ordinal,
                action.Type,
                AutomationActionResultStatus.Running,
                key,
                null,
                startedAt,
                null));
            AutomationDispatchResult dispatched;
            try
            {
                dispatched = await dispatcher.DispatchAsync(
                    action,
                    new AutomationActionDispatchContext(
                        rule.Id,
                        executionId,
                        ordinal,
                        key,
                        target.ResolvedId,
                        trigger,
                        startedAt),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                dispatched = AutomationDispatchResult.ResultUnknown(
                    "automation_consumer_threw",
                    dispatcher.IsConsumerIdempotent(action),
                    consumerStarted: true);
            }

            var status = dispatched.Status switch
            {
                AutomationDispatchStatus.Succeeded => AutomationActionResultStatus.Succeeded,
                AutomationDispatchStatus.ResultUnknown => AutomationActionResultStatus.ResultUnknown,
                AutomationDispatchStatus.Failed => AutomationActionResultStatus.Failed,
                AutomationDispatchStatus.Unavailable => AutomationActionResultStatus.Failed,
                _ => throw new InvalidOperationException("automation_dispatch_status_invalid")
            };
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                ordinal,
                action.Type,
                status,
                key,
                dispatched.ErrorCode,
                startedAt,
                UtcNow()));
            return status;
        }

        private async Task<AutomationActionResultStatus> RecoverNotStartedAsync(
            string executionId,
            int ordinal,
            AutomationRule rule,
            AutomationAction action,
            AutomationTriggerSnapshot trigger,
            DateTimeOffset evidenceStartedAt,
            CancellationToken cancellationToken)
        {
            if (!dispatcher.IsConsumerIdempotent(action))
            {
                RecordRecoveryUnknown(
                    executionId,
                    ordinal,
                    action,
                    evidenceStartedAt,
                    "automation_recovery_non_idempotent_review_required");
                return AutomationActionResultStatus.ResultUnknown;
            }
            return await ExecuteActionAsync(
                executionId,
                ordinal,
                rule,
                action,
                trigger,
                cancellationToken).ConfigureAwait(false);
        }

        private void RecordRecoveryUnknown(
            string executionId,
            int ordinal,
            AutomationAction action,
            DateTimeOffset startedAt,
            string errorCode) =>
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                ordinal,
                action.Type,
                AutomationActionResultStatus.ResultUnknown,
                CreateConsumerIdempotencyKey(executionId, ordinal),
                errorCode,
                startedAt,
                UtcNow()));

        private void RecordTerminal(
            string executionId,
            int ordinal,
            AutomationAction action,
            string key,
            AutomationActionResultStatus status,
            string errorCode,
            DateTimeOffset at) =>
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                ordinal,
                action.Type,
                status,
                key,
                errorCode,
                at,
                at));

        private async Task<RuleAcquisition> AcquireAsync(
            AutomationRule rule,
            AutomationTriggerSnapshot trigger)
        {
            var state = StateFor(rule.Id);
            Task? queuedWait = null;
            lock (state.Sync)
            {
                var now = UtcNow();
                var cooldownError = CheckCooldown(state, rule, trigger, now, out var cooldownKey);
                if (cooldownError != null) return RuleAcquisition.Skipped(cooldownError);
                var decision = AutomationExecutionPolicy.DecideConcurrency(
                    rule.ConcurrencyPolicy,
                    state.IsRunning,
                    state.Queued != null);
                switch (decision)
                {
                    case AutomationConcurrencyDecision.Start:
                        state.IsRunning = true;
                        StartCooldown(state, rule, cooldownKey, now);
                        return RuleAcquisition.Acquired(new RuleLease(state));
                    case AutomationConcurrencyDecision.Skip:
                        return RuleAcquisition.Skipped(
                            state.Queued != null
                                ? "automation_rule_queue_full"
                                : "automation_rule_running");
                    case AutomationConcurrencyDecision.Queue:
                        state.Queued = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        queuedWait = state.Queued.Task;
                        break;
                    default:
                        throw new InvalidOperationException("automation_concurrency_decision_invalid");
                }
            }

            await queuedWait!.ConfigureAwait(false);
            lock (state.Sync)
            {
                var now = UtcNow();
                var cooldownError = CheckCooldown(state, rule, trigger, now, out var cooldownKey);
                if (cooldownError != null)
                {
                    RuleLease.ReleaseState(state);
                    return RuleAcquisition.Skipped(cooldownError);
                }
                StartCooldown(state, rule, cooldownKey, now);
                return RuleAcquisition.Acquired(new RuleLease(state));
            }
        }

        private static string? CheckCooldown(
            RuleRuntimeState state,
            AutomationRule rule,
            AutomationTriggerSnapshot trigger,
            DateTimeOffset now,
            out string? key)
        {
            key = null;
            if (rule.CooldownDuration == TimeSpan.Zero) return null;
            try
            {
                key = AutomationCooldownKey.Create(
                    rule.CooldownScope,
                    rule.Id,
                    trigger.ActorCrossplatformId).Value;
            }
            catch (ArgumentException)
            {
                return "automation_cooldown_player_missing";
            }
            if (state.Cooldowns.TryGetValue(key, out var until) && until > now)
                return "automation_cooldown_active";
            return null;
        }

        private static void StartCooldown(
            RuleRuntimeState state,
            AutomationRule rule,
            string? key,
            DateTimeOffset now)
        {
            if (key != null) state.Cooldowns[key] = now.Add(rule.CooldownDuration);
        }

        private RuleRuntimeState StateFor(string ruleId)
        {
            lock (statesSync)
            {
                if (!states.TryGetValue(ruleId, out var state))
                {
                    state = new RuleRuntimeState();
                    states.Add(ruleId, state);
                }
                return state;
            }
        }

        private DateTimeOffset UtcNow()
        {
            var value = utcNow();
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("automation_clock_must_be_utc");
            return value;
        }

        private static AutomationExecutionOutcome Outcome(
            string executionId,
            AutomationRule rule,
            AutomationTriggerSnapshot trigger,
            AutomationExecutionStatus status,
            string? errorCode,
            bool wasCreated) =>
            new(executionId, rule.Id, trigger.TriggerId, status, errorCode, wasCreated);

        private AutomationExecutionOutcome CompleteOutcome(
            string executionId,
            AutomationRule rule,
            AutomationTriggerSnapshot trigger,
            AutomationExecutionStatus status,
            string? errorCode,
            bool wasCreated,
            AutomationExecutionStatus? expectedStatus)
        {
            if (executionStates != null && expectedStatus.HasValue &&
                !executionStates.TryCompleteExecution(
                    executionId,
                    expectedStatus.Value,
                    status,
                    UtcNow(),
                    errorCode))
            {
                throw new InvalidOperationException("automation_execution_state_conflict");
            }
            return Outcome(executionId, rule, trigger, status, errorCode, wasCreated);
        }

        private static void ValidateTrigger(AutomationTriggerSnapshot trigger)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            RequireStableId(trigger.TriggerId, nameof(trigger));
            RequireStableId(trigger.TriggerType, nameof(trigger));
            if (trigger.OccurredAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC trigger time is required.", nameof(trigger));
            if (trigger.GapIds == null)
                throw new ArgumentException("Trigger gap IDs are required.", nameof(trigger));
        }

        private static void RequireStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value.Length > AutomationCondition.MaxStringLength)
            {
                throw new ArgumentException("A bounded stable ID is required.", parameterName);
            }
        }

        private sealed class RuleRuntimeState
        {
            public object Sync { get; } = new();
            public bool IsRunning { get; set; }
            public TaskCompletionSource<bool>? Queued { get; set; }
            public Dictionary<string, DateTimeOffset> Cooldowns { get; } =
                new(StringComparer.Ordinal);
        }

        private sealed class RuleLease : IDisposable
        {
            private RuleRuntimeState? state;
            public RuleLease(RuleRuntimeState state) => this.state = state;

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref state, null);
                if (current != null) ReleaseState(current);
            }

            internal static void ReleaseState(RuleRuntimeState state)
            {
                TaskCompletionSource<bool>? next;
                lock (state.Sync)
                {
                    next = state.Queued;
                    state.Queued = null;
                    if (next == null) state.IsRunning = false;
                }
                next?.TrySetResult(true);
            }
        }

        private sealed record RuleAcquisition(RuleLease? Lease, string? ErrorCode)
        {
            public static RuleAcquisition Acquired(RuleLease lease) => new(lease, null);
            public static RuleAcquisition Skipped(string errorCode) => new(null, errorCode);
        }
    }
}
