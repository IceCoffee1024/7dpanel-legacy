using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AutomationExecutionTests
    {
        [Fact]
        public async Task Duplicate_trigger_creates_one_execution_with_stable_consumer_keys()
        {
            var store = new RecordingStore(Rule(
                actions: new[]
                {
                    Action("first", "BroadcastMessage", "Global", "one"),
                    Action("second", "Announcement", "Global", "two")
                }));
            var dispatcher = new RecordingDispatcher();
            var engine = Engine(store, dispatcher);
            var trigger = Trigger("trigger-1");

            var first = await engine.ExecuteAsync(trigger, TestContext.Current.CancellationToken);
            var duplicate = await engine.ExecuteAsync(trigger, TestContext.Current.CancellationToken);

            var expectedExecutionId = HexSha256("rule-1\ntrigger-1");
            Assert.Equal(expectedExecutionId, first.Single().ExecutionId);
            Assert.True(first.Single().WasCreated);
            Assert.False(duplicate.Single().WasCreated);
            Assert.Single(store.Executions);
            Assert.Equal(new[] { 0, 1 }, dispatcher.Calls.Select(call => call.Ordinal));
            Assert.Equal(
                new[] { expectedExecutionId + ":0", expectedExecutionId + ":1" },
                dispatcher.Calls.Select(call => call.ConsumerIdempotencyKey));
        }

        [Fact]
        public async Task Actions_are_ordered_and_failure_policy_controls_only_later_actions()
        {
            var actions = new[]
            {
                Action("first", "BroadcastMessage", "Global", "one"),
                Action("second", "Announcement", "Global", "two"),
                Action("third", "DiscordMessage", "DiscordTarget", "three", "ops")
            };
            var continueDispatcher = new RecordingDispatcher
            {
                ResultByOrdinal =
                {
                    [1] = AutomationDispatchResult.Failed("consumer_rejected", consumerStarted: true)
                }
            };
            var continueStore = new RecordingStore(Rule(
                actions: actions,
                failurePolicy: AutomationFailurePolicy.Continue));

            var continued = await Engine(continueStore, continueDispatcher).ExecuteAsync(
                Trigger("continue"),
                TestContext.Current.CancellationToken);

            Assert.Equal(AutomationExecutionStatus.Failed, continued.Single().Status);
            Assert.Equal(new[] { 0, 1, 2 }, continueDispatcher.Calls.Select(call => call.Ordinal));
            Assert.Equal(AutomationActionResultStatus.Succeeded, continueStore.ActionResults[(continued.Single().ExecutionId, 0)].Status);
            Assert.Equal(AutomationActionResultStatus.Failed, continueStore.ActionResults[(continued.Single().ExecutionId, 1)].Status);
            Assert.Equal(AutomationActionResultStatus.Succeeded, continueStore.ActionResults[(continued.Single().ExecutionId, 2)].Status);

            var stopDispatcher = new RecordingDispatcher
            {
                ResultByOrdinal =
                {
                    [1] = AutomationDispatchResult.Failed("consumer_rejected", consumerStarted: true)
                }
            };
            var stopStore = new RecordingStore(Rule(
                id: "rule-stop",
                actions: actions,
                failurePolicy: AutomationFailurePolicy.StopOnFailure));

            await Engine(stopStore, stopDispatcher).ExecuteAsync(
                Trigger("stop"),
                TestContext.Current.CancellationToken);

            Assert.Equal(new[] { 0, 1 }, stopDispatcher.Calls.Select(call => call.Ordinal));
        }

        [Fact]
        public async Task Cooldown_and_both_concurrency_policies_are_enforced()
        {
            var now = Utc(10);
            var cooldownStore = new RecordingStore(Rule(
                cooldown: TimeSpan.FromMinutes(5),
                cooldownScope: AutomationCooldownScope.RulePlayer));
            var cooldownEngine = Engine(cooldownStore, new RecordingDispatcher(), () => now);

            var first = await cooldownEngine.ExecuteAsync(
                Trigger("cooldown-1"),
                TestContext.Current.CancellationToken);
            now = Utc(11);
            var second = await cooldownEngine.ExecuteAsync(
                Trigger("cooldown-2"),
                TestContext.Current.CancellationToken);

            Assert.Equal(AutomationExecutionStatus.Succeeded, first.Single().Status);
            Assert.Equal(AutomationExecutionStatus.Skipped, second.Single().Status);
            Assert.Equal("automation_cooldown_active", second.Single().ErrorCode);

            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var skipDispatcher = new RecordingDispatcher
            {
                BeforeDispatch = async _ =>
                {
                    entered.TrySetResult(true);
                    await gate.Task;
                }
            };
            var skipStore = new RecordingStore(Rule(
                id: "rule-skip",
                concurrency: AutomationConcurrencyPolicy.SkipIfRunning));
            var skipEngine = Engine(skipStore, skipDispatcher);
            var running = skipEngine.ExecuteAsync(Trigger("running"), CancellationToken.None);
            await entered.Task;
            var skipped = await skipEngine.ExecuteAsync(Trigger("skipped"), CancellationToken.None);
            Assert.Equal(AutomationExecutionStatus.Skipped, skipped.Single().Status);
            Assert.Equal("automation_rule_running", skipped.Single().ErrorCode);
            gate.TrySetResult(true);
            await running;

            gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queueDispatcher = new RecordingDispatcher
            {
                BeforeDispatch = async context =>
                {
                    if (context.Trigger.TriggerId == "queue-running")
                    {
                        entered.TrySetResult(true);
                        await gate.Task;
                    }
                }
            };
            var queueStore = new RecordingStore(Rule(
                id: "rule-queue",
                concurrency: AutomationConcurrencyPolicy.QueueOne));
            var queueEngine = Engine(queueStore, queueDispatcher);
            var queueRunning = queueEngine.ExecuteAsync(Trigger("queue-running"), CancellationToken.None);
            await entered.Task;
            var queued = queueEngine.ExecuteAsync(Trigger("queue-one"), CancellationToken.None);
            var queueSkipped = await queueEngine.ExecuteAsync(Trigger("queue-two"), CancellationToken.None);
            Assert.Equal(AutomationExecutionStatus.Skipped, queueSkipped.Single().Status);
            Assert.Equal("automation_rule_queue_full", queueSkipped.Single().ErrorCode);
            gate.TrySetResult(true);
            await Task.WhenAll(queueRunning, queued);
            Assert.Contains(queueDispatcher.Calls, call => call.Trigger.TriggerId == "queue-one");
        }

        [Fact]
        public async Task Recovery_replays_only_idempotent_actions_that_never_started()
        {
            var rule = Rule(actions: new[]
            {
                Action("done", "BroadcastMessage", "Global", "done"),
                Action("safe", "GrantRewardPackage", "TriggerPlayer", "starter"),
                Action("unsafe", "KickPlayer", "TriggerPlayer", "bye")
            });
            var store = new RecordingStore(rule);
            var trigger = Trigger("recovery");
            var executionId = AutomationExecutionEngine.CreateExecutionId(rule.Id, trigger.TriggerId);
            var execution = new AutomationExecutionRecord(
                executionId,
                rule.Id,
                trigger.TriggerId,
                AutomationExecutionStatus.Running,
                executionId,
                Utc(9),
                null,
                null);
            store.SeedExecution(execution);
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                0,
                "BroadcastMessage",
                AutomationActionResultStatus.Succeeded,
                executionId + ":0",
                null,
                Utc(9),
                Utc(9)));
            var dispatcher = new RecordingDispatcher();
            dispatcher.IdempotentTypes.Add("GrantRewardPackage");
            var engine = Engine(store, dispatcher);

            var outcome = await engine.RecoverAsync(
                rule,
                trigger,
                execution,
                TestContext.Current.CancellationToken);

            Assert.Equal(AutomationExecutionStatus.ResultUnknown, outcome.Status);
            Assert.Equal(new[] { 1 }, dispatcher.Calls.Select(call => call.Ordinal));
            Assert.Equal(AutomationActionResultStatus.Succeeded, store.ActionResults[(executionId, 0)].Status);
            Assert.Equal(AutomationActionResultStatus.Succeeded, store.ActionResults[(executionId, 1)].Status);
            Assert.Equal(AutomationActionResultStatus.ResultUnknown, store.ActionResults[(executionId, 2)].Status);
            Assert.Equal("automation_recovery_non_idempotent_review_required", store.ActionResults[(executionId, 2)].ErrorCode);
        }

        [Fact]
        public void Startup_recovery_calls_the_real_engine_and_replays_only_not_started_idempotent_actions()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteAutomationStore(database.ConnectionFactory);
            var rule = Rule(actions: new[]
            {
                Action("done", "BroadcastMessage", "Global", "done"),
                Action("safe", "GrantRewardPackage", "TriggerPlayer", "starter"),
                Action("started", "Announcement", "Global", "started"),
                Action("unsafe", "KickPlayer", "TriggerPlayer", "unsafe"),
                Action("unknown", "DiscordMessage", "DiscordTarget", "unknown", "ops")
            });
            store.SaveRule(rule, expectedVersion: 0);
            var trigger = Trigger("startup-recovery");
            store.SaveTrigger(trigger);
            var executionId = AutomationExecutionEngine.CreateExecutionId(rule.Id, trigger.TriggerId);
            var execution = new AutomationExecutionRecord(
                executionId,
                rule.Id,
                trigger.TriggerId,
                AutomationExecutionStatus.Running,
                executionId,
                Utc(8),
                null,
                null);
            store.TryStartExecution(execution);
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                0,
                "BroadcastMessage",
                AutomationActionResultStatus.Succeeded,
                executionId + ":0",
                null,
                Utc(8),
                Utc(9)));
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                1,
                "GrantRewardPackage",
                AutomationActionResultStatus.Pending,
                executionId + ":1",
                null,
                Utc(8),
                null));
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                2,
                "Announcement",
                AutomationActionResultStatus.Running,
                executionId + ":2",
                null,
                Utc(8),
                null));
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                3,
                "KickPlayer",
                AutomationActionResultStatus.Pending,
                executionId + ":3",
                null,
                Utc(8),
                null));
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                4,
                "DiscordMessage",
                AutomationActionResultStatus.ResultUnknown,
                executionId + ":4",
                "already_unknown",
                Utc(8),
                Utc(9)));

            var dispatcher = new RecordingDispatcher();
            dispatcher.IdempotentTypes.Add("GrantRewardPackage");
            dispatcher.IdempotentTypes.Add("Announcement");
            dispatcher.IdempotentTypes.Add("DiscordMessage");
            var engine = Engine(store, dispatcher, () => Utc(10));

            var report = new AutomationExecutionRecoveryService(
                store,
                engine,
                () => Utc(10),
                batchSize: 16).Recover();

            Assert.Equal(1, report.ExaminedCount);
            Assert.Equal(1, report.MarkedResultUnknownCount);
            Assert.Empty(report.UnchangedExecutionIds);
            Assert.Equal(new[] { 1 }, dispatcher.Calls.Select(call => call.Ordinal));
            var actionResults = store.ListActionResults(executionId).ToDictionary(item => item.Ordinal);
            Assert.Equal(AutomationActionResultStatus.Succeeded, actionResults[0].Status);
            Assert.Equal(AutomationActionResultStatus.Succeeded, actionResults[1].Status);
            Assert.Equal(AutomationActionResultStatus.ResultUnknown, actionResults[2].Status);
            Assert.Equal("automation_recovery_started_result_unknown", actionResults[2].ErrorCode);
            Assert.Equal(AutomationActionResultStatus.ResultUnknown, actionResults[3].Status);
            Assert.Equal("automation_recovery_non_idempotent_review_required", actionResults[3].ErrorCode);
            Assert.Equal(AutomationActionResultStatus.ResultUnknown, actionResults[4].Status);
            Assert.Equal("already_unknown", actionResults[4].ErrorCode);
            var completed = Assert.IsType<AutomationExecutionRecord>(store.FindExecution(executionId));
            Assert.Equal(AutomationExecutionStatus.ResultUnknown, completed.Status);
            Assert.Equal("automation_recovery_review_required", completed.ErrorCode);
            Assert.Equal(Utc(10), completed.CompletedAtUtc);
        }

        [Fact]
        public async Task Startup_rebuilds_cooldown_from_sqlite_execution_and_trigger_evidence()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteAutomationStore(database.ConnectionFactory);
            var rule = Rule(
                id: "rule-persisted-cooldown",
                cooldown: TimeSpan.FromMinutes(5),
                cooldownScope: AutomationCooldownScope.RulePlayer);
            store.SaveRule(rule, expectedVersion: 0);
            var now = Utc(10);
            var first = await Engine(store, new RecordingDispatcher(), () => now).ExecuteAsync(
                Trigger("before-restart"),
                TestContext.Current.CancellationToken);
            Assert.Equal(AutomationExecutionStatus.Succeeded, first.Single().Status);

            now = Utc(11);
            var restartedEngine = Engine(store, new RecordingDispatcher(), () => now);
            var report = new AutomationExecutionRecoveryService(
                store,
                restartedEngine,
                () => now,
                batchSize: 16).Recover();
            var afterRestart = await restartedEngine.ExecuteAsync(
                Trigger("after-restart"),
                TestContext.Current.CancellationToken);

            Assert.Equal(0, report.ExaminedCount);
            Assert.Equal(AutomationExecutionStatus.Skipped, afterRestart.Single().Status);
            Assert.Equal("automation_cooldown_active", afterRestart.Single().ErrorCode);
        }

        private static AutomationExecutionEngine Engine(
            IAutomationStore store,
            RecordingDispatcher dispatcher,
            Func<DateTimeOffset>? clock = null) =>
            new(
                store,
                new AutomationConditionEvaluator(TimeZoneInfo.Utc),
                new ReadyDependencies(),
                new ResolvedTargets(),
                dispatcher,
                clock ?? (() => Utc(10)));

        private static AutomationRule Rule(
            string id = "rule-1",
            IReadOnlyList<AutomationAction>? actions = null,
            TimeSpan? cooldown = null,
            AutomationCooldownScope cooldownScope = AutomationCooldownScope.Rule,
            AutomationConcurrencyPolicy concurrency = AutomationConcurrencyPolicy.SkipIfRunning,
            AutomationFailurePolicy failurePolicy = AutomationFailurePolicy.Continue) =>
            new(
                id,
                1,
                id,
                true,
                new AutomationTrigger("PlayerJoined"),
                AutomationCondition.TextEquals(
                    "actor",
                    AutomationFieldKeys.ActorCrossplatformId,
                    "player-1"),
                actions ?? new[] { Action("message", "BroadcastMessage", "Global", "hello") },
                cooldown ?? TimeSpan.Zero,
                cooldownScope,
                concurrency,
                failurePolicy,
                Utc(0),
                Utc(0));

        private static AutomationAction Action(
            string id,
            string type,
            string targetKind,
            string? text = null,
            string? reference = null,
            long? amount = null,
            TimeSpan? duration = null) =>
            new(id, type, targetKind, text, reference, amount, duration);

        private static AutomationTriggerSnapshot Trigger(string id) =>
            new(
                id,
                "PlayerJoined",
                Utc(10),
                "player-1",
                7,
                "member",
                10,
                null,
                null,
                null,
                Array.Empty<string>());

        private static DateTimeOffset Utc(int minute) =>
            new(2026, 7, 27, 0, minute, 0, TimeSpan.Zero);

        private static string HexSha256(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return string.Concat(bytes.Select(item => item.ToString("x2")));
        }

        private sealed class ReadyDependencies : IAutomationDependencyCatalog
        {
            public AutomationDependencyState Resolve(AutomationAction action) =>
                AutomationDependencyState.Ready;
        }

        private sealed class ResolvedTargets : IAutomationTargetResolver
        {
            public AutomationTargetResolution Resolve(
                AutomationAction action,
                AutomationTriggerSnapshot snapshot) =>
                AutomationTargetResolution.Resolved(
                    action.TargetKind == "Global"
                        ? "global"
                        : action.ReferenceId ?? snapshot.ActorCrossplatformId ?? "missing");
        }

        private sealed class RecordingDispatcher : IAutomationActionDispatcher
        {
            public List<AutomationActionDispatchContext> Calls { get; } = new();
            public Dictionary<int, AutomationDispatchResult> ResultByOrdinal { get; } = new();
            public HashSet<string> IdempotentTypes { get; } = new(StringComparer.Ordinal);
            public Func<AutomationActionDispatchContext, Task>? BeforeDispatch { get; set; }

            public bool IsConsumerIdempotent(AutomationAction action) =>
                IdempotentTypes.Contains(action.Type);

            public async Task<AutomationDispatchResult> DispatchAsync(
                AutomationAction action,
                AutomationActionDispatchContext context,
                CancellationToken cancellationToken)
            {
                Calls.Add(context);
                if (BeforeDispatch != null) await BeforeDispatch(context);
                return ResultByOrdinal.TryGetValue(context.Ordinal, out var result)
                    ? result
                    : AutomationDispatchResult.Succeeded(IsConsumerIdempotent(action));
            }
        }

        private sealed class RecordingStore : IAutomationStore
        {
            private readonly IReadOnlyList<AutomationRule> rules;
            private readonly Dictionary<(string RuleId, string TriggerId), AutomationExecutionRecord> executions = new();

            public RecordingStore(params AutomationRule[] rules) => this.rules = rules;

            public IReadOnlyCollection<AutomationExecutionRecord> Executions => executions.Values;
            public Dictionary<(string ExecutionId, int Ordinal), AutomationActionExecutionResult> ActionResults { get; } = new();
            public List<AutomationConditionExecutionResult> ConditionResults { get; } = new();

            public IReadOnlyList<AutomationRule> ListRules() => rules;
            public AutomationRule? FindRule(string ruleId) => rules.SingleOrDefault(rule => rule.Id == ruleId);
            public void SaveRule(AutomationRule rule, long expectedVersion) => throw new NotSupportedException();
            public void DeleteRule(string ruleId, long expectedVersion, DateTimeOffset deletedAtUtc) => throw new NotSupportedException();
            public void SaveTrigger(AutomationTriggerSnapshot trigger) { }

            public AutomationExecutionStartResult TryStartExecution(AutomationExecutionRecord execution)
            {
                var key = (execution.RuleId, execution.TriggerId);
                if (executions.TryGetValue(key, out var existing))
                    return new AutomationExecutionStartResult(existing, false);
                executions.Add(key, execution);
                return new AutomationExecutionStartResult(execution, true);
            }

            public void SeedExecution(AutomationExecutionRecord execution) =>
                executions[(execution.RuleId, execution.TriggerId)] = execution;

            public void RecordConditionResult(AutomationConditionExecutionResult result) =>
                ConditionResults.Add(result);

            public void RecordActionResult(AutomationActionExecutionResult result) =>
                ActionResults[(result.ExecutionId, result.Ordinal)] = result;

            public IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(string executionId) =>
                ConditionResults.Where(result => result.ExecutionId == executionId).ToArray();

            public IReadOnlyList<AutomationActionExecutionResult> ListActionResults(string executionId) =>
                ActionResults.Values
                    .Where(result => result.ExecutionId == executionId)
                    .OrderBy(result => result.Ordinal)
                    .ToArray();
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-automation-recovery-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

    }
}
