using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LSTY.SevenDPanel.Domain.Automations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AutomationDomainTests
    {
        [Fact]
        public void Condition_limits_match_the_approved_product_contract()
        {
            Assert.Equal(5, AutomationCondition.MaxDepth);
            Assert.Equal(64, AutomationCondition.MaxNodeCount);
            Assert.Equal(256, AutomationCondition.MaxStringLength);
            Assert.Equal(50, AutomationCondition.MaxSetElementCount);

            var maximumText = new string('x', AutomationCondition.MaxStringLength);
            Assert.Equal(
                maximumText,
                AutomationCondition.TextEquals("max-text", "field", maximumText).ScalarValue);
            Assert.Equal(
                AutomationCondition.MaxSetElementCount,
                AutomationCondition.InSet(
                    "max-set",
                    "field",
                    Enumerable.Range(0, AutomationCondition.MaxSetElementCount)
                        .Select(index => "value-" + index)).SetValues.Count);
        }

        [Fact]
        public void Condition_tree_enforces_small_explicit_depth_and_node_limits()
        {
            var condition = AutomationCondition.TextEquals("leaf", "field", "value");
            for (var index = 1; index < AutomationCondition.MaxDepth; index++)
                condition = AutomationCondition.Not("not-" + index, condition);

            Assert.Equal(AutomationCondition.MaxDepth, condition.Depth);
            Assert.Throws<ArgumentException>(() =>
                AutomationCondition.Not("too-deep", condition));

            var maximumChildren = Enumerable
                .Range(0, AutomationCondition.MaxNodeCount - 1)
                .Select(index => AutomationCondition.TextEquals(
                    "leaf-" + index,
                    "field",
                    "value"))
                .ToArray();
            var maximumTree = AutomationCondition.All("root", maximumChildren);
            Assert.Equal(AutomationCondition.MaxNodeCount, maximumTree.NodeCount);

            Assert.Throws<ArgumentException>(() => AutomationCondition.All(
                "too-many",
                maximumChildren.Concat(new[]
                {
                    AutomationCondition.TextEquals("extra", "field", "value")
                }).ToArray()));
        }

        [Fact]
        public void Condition_strings_and_sets_are_bounded_and_defensively_copied()
        {
            Assert.Throws<ArgumentException>(() => AutomationCondition.TextEquals(
                "node",
                "field",
                new string('x', AutomationCondition.MaxStringLength + 1)));

            var source = new List<string> { "alpha", "beta" };
            var condition = AutomationCondition.InSet("node", "field", source);
            source[0] = "changed";
            source.Add("late");

            Assert.Equal(new[] { "alpha", "beta" }, condition.SetValues);
            Assert.Throws<ArgumentException>(() => AutomationCondition.InSet(
                "too-many",
                "field",
                Enumerable.Range(0, AutomationCondition.MaxSetElementCount + 1)
                    .Select(index => "value-" + index)));
        }

        [Fact]
        public void Unknown_propagates_through_boolean_trees_without_becoming_false_data()
        {
            var matched = AutomationCondition.TextEquals("matched", "matched", "yes");
            var unknown = AutomationCondition.TextEquals("unknown", "unknown", "yes");
            var notMatched = AutomationCondition.TextEquals("not-matched", "not-matched", "yes");
            AutomationConditionValue? Resolve(string field) => field switch
            {
                "matched" => AutomationConditionValue.Text("yes"),
                "not-matched" => AutomationConditionValue.Text("no"),
                _ => null
            };

            Assert.Equal(
                AutomationTruth.Unknown,
                AutomationCondition.All("all-unknown", matched, unknown).Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.NotMatched,
                AutomationCondition.All("all-false", notMatched, unknown).Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Unknown,
                AutomationCondition.Any("any-unknown", notMatched, unknown).Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Matched,
                AutomationCondition.Any("any-true", matched, unknown).Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Unknown,
                AutomationCondition.Not("not-unknown", unknown).Evaluate(Resolve));
        }

        [Fact]
        public void Number_range_is_closed_and_missing_numbers_are_unknown()
        {
            var condition = AutomationCondition.NumberRange("range", "amount", 10, 20);

            Assert.Equal(AutomationTruth.Matched, condition.Evaluate(_ => AutomationConditionValue.Number(10)));
            Assert.Equal(AutomationTruth.Matched, condition.Evaluate(_ => AutomationConditionValue.Number(20)));
            Assert.Equal(AutomationTruth.NotMatched, condition.Evaluate(_ => AutomationConditionValue.Number(9)));
            Assert.Equal(AutomationTruth.Unknown, condition.Evaluate(_ => null));
        }

        [Fact]
        public void Structured_scalar_predicates_keep_membership_and_cooldown_typed()
        {
            var groups = new List<string> { "member" };
            var groupValue = AutomationConditionValue.Set(groups);
            groups[0] = "changed";
            var values = new Dictionary<string, AutomationConditionValue>(StringComparer.Ordinal)
            {
                ["name"] = AutomationConditionValue.Text("alpha"),
                ["group"] = groupValue,
                ["permission"] = AutomationConditionValue.Set(new[] { "moderator" }),
                ["cooldown"] = AutomationConditionValue.Truth(AutomationTruth.NotMatched)
            };
            AutomationConditionValue? Resolve(string field) =>
                values.TryGetValue(field, out var value) ? value : null;

            Assert.Equal(
                AutomationTruth.Matched,
                AutomationCondition.TextNotEquals("not-equals", "name", "beta").Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Matched,
                AutomationCondition.InSet("in-set", "name", new[] { "alpha", "beta" }).Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Matched,
                AutomationCondition.PlayerGroup("group-node", "group", "member").Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.Matched,
                AutomationCondition.Permission(
                    "permission-node",
                    "permission",
                    "moderator").Evaluate(Resolve));
            Assert.Equal(
                AutomationTruth.NotMatched,
                AutomationCondition.Cooldown("cooldown-node", "cooldown").Evaluate(Resolve));
        }

        [Fact]
        public void Time_window_uses_product_scalars_and_handles_cross_midnight_inclusively()
        {
            var window = new AutomationTimeWindow(
                "server-local",
                new AutomationTimeOfDay(22, 0),
                new AutomationTimeOfDay(2, 0));
            var condition = AutomationCondition.TimeWindow("window", "local-time", window);

            Assert.Equal(AutomationTruth.Matched, EvaluateAt(condition, "server-local", 22, 0));
            Assert.Equal(AutomationTruth.Matched, EvaluateAt(condition, "server-local", 0, 30));
            Assert.Equal(AutomationTruth.Matched, EvaluateAt(condition, "server-local", 2, 0));
            Assert.Equal(AutomationTruth.NotMatched, EvaluateAt(condition, "server-local", 12, 0));
            Assert.Equal(AutomationTruth.Unknown, EvaluateAt(condition, "another-zone", 0, 30));
        }

        [Fact]
        public void Rule_player_cooldown_requires_a_stable_player_and_never_collides_with_rule_scope()
        {
            var ruleKey = AutomationCooldownKey.Create(AutomationCooldownScope.Rule, "rule-1");
            var playerKey = AutomationCooldownKey.Create(
                AutomationCooldownScope.RulePlayer,
                "rule-1",
                "player-1");

            Assert.NotEqual(ruleKey.Value, playerKey.Value);
            Assert.Equal("player-1", playerKey.StablePlayerId);
            Assert.Throws<ArgumentException>(() => AutomationCooldownKey.Create(
                AutomationCooldownScope.RulePlayer,
                "rule-1"));
            Assert.Throws<ArgumentException>(() => AutomationCooldownKey.Create(
                AutomationCooldownScope.RulePlayer,
                "rule-1",
                " "));
        }

        [Fact]
        public void Concurrency_policy_skips_running_work_or_keeps_only_one_queued_item()
        {
            Assert.Equal(
                AutomationConcurrencyDecision.Start,
                AutomationExecutionPolicy.DecideConcurrency(
                    AutomationConcurrencyPolicy.SkipIfRunning,
                    isRunning: false,
                    hasQueued: false));
            Assert.Equal(
                AutomationConcurrencyDecision.Skip,
                AutomationExecutionPolicy.DecideConcurrency(
                    AutomationConcurrencyPolicy.SkipIfRunning,
                    isRunning: true,
                    hasQueued: false));
            Assert.Equal(
                AutomationConcurrencyDecision.Queue,
                AutomationExecutionPolicy.DecideConcurrency(
                    AutomationConcurrencyPolicy.QueueOne,
                    isRunning: true,
                    hasQueued: false));
            Assert.Equal(
                AutomationConcurrencyDecision.Skip,
                AutomationExecutionPolicy.DecideConcurrency(
                    AutomationConcurrencyPolicy.QueueOne,
                    isRunning: true,
                    hasQueued: true));
        }

        [Fact]
        public void Failure_policy_only_decides_whether_the_next_action_runs()
        {
            Assert.True(AutomationExecutionPolicy.ShouldContinueAfterAction(
                AutomationFailurePolicy.StopOnFailure,
                actionSucceeded: true));
            Assert.False(AutomationExecutionPolicy.ShouldContinueAfterAction(
                AutomationFailurePolicy.StopOnFailure,
                actionSucceeded: false));
            Assert.True(AutomationExecutionPolicy.ShouldContinueAfterAction(
                AutomationFailurePolicy.Continue,
                actionSucceeded: false));
        }

        [Fact]
        public void Rule_is_a_bounded_immutable_product_snapshot_with_ordered_actions_and_utc_times()
        {
            var first = new AutomationAction("a-1", "SendGameMessage", "Global", "first");
            var second = new AutomationAction("a-2", "SendDiscordMessage", "Public", "second");
            var sourceActions = new List<AutomationAction> { first, second };
            var createdAtUtc = new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
            var rule = new AutomationRule(
                "rule-1",
                3,
                "Welcome",
                true,
                new AutomationTrigger("PlayerJoined"),
                AutomationCondition.TextEquals("condition", "player.group", "member"),
                sourceActions,
                TimeSpan.FromMinutes(5),
                AutomationCooldownScope.RulePlayer,
                AutomationConcurrencyPolicy.QueueOne,
                AutomationFailurePolicy.Continue,
                createdAtUtc,
                createdAtUtc.AddMinutes(1));
            sourceActions.Reverse();
            sourceActions.Clear();

            Assert.Equal(new[] { "a-1", "a-2" }, rule.Actions.Select(action => action.Id));
            Assert.Equal(TimeSpan.Zero, rule.CreatedAtUtc.Offset);
            Assert.Equal(TimeSpan.Zero, rule.UpdatedAtUtc.Offset);

            var publicProperties = typeof(AutomationRule)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Select(property => property.Name)
                .OrderBy(name => name)
                .ToArray();
            Assert.Equal(new[]
            {
                "Actions",
                "ConcurrencyPolicy",
                "ConditionRoot",
                "CooldownDuration",
                "CooldownScope",
                "CreatedAtUtc",
                "FailurePolicy",
                "Id",
                "IsEnabled",
                "Name",
                "Trigger",
                "UpdatedAtUtc",
                "Version"
            }, publicProperties);

            Assert.Throws<ArgumentException>(() => new AutomationRule(
                "rule-2",
                1,
                "Invalid time",
                true,
                new AutomationTrigger("PlayerJoined"),
                AutomationCondition.TextEquals("condition-2", "player.group", "member"),
                new[] { first },
                TimeSpan.Zero,
                AutomationCooldownScope.Rule,
                AutomationConcurrencyPolicy.SkipIfRunning,
                AutomationFailurePolicy.StopOnFailure,
                createdAtUtc.ToOffset(TimeSpan.FromHours(8)),
                createdAtUtc));
        }

        private static AutomationTruth EvaluateAt(
            AutomationCondition condition,
            string timeZoneId,
            int hour,
            int minute) =>
            condition.Evaluate(_ => AutomationConditionValue.LocalTime(
                new AutomationLocalTime(
                    timeZoneId,
                    new AutomationTimeOfDay(hour, minute))));
    }
}
