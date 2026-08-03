using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Domain.Automations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Automation")]
    [Trait("Boundary", "Application")]
    public sealed class AutomationRuleUseCaseTests
    {
        private static readonly AuthenticatedActor Owner =
            new("owner-subject", AutomationActorRole.Owner);

        [Fact]
        public void Field_catalog_is_explicit_and_trigger_specific()
        {
            var catalog = new AutomationFieldCatalog();

            Assert.Equal(5, catalog.TriggerTypes.Count);
            Assert.NotNull(catalog.Find(AutomationTriggerType.ChatMessage, AutomationFieldKeys.ChatText));
            Assert.Null(catalog.Find(AutomationTriggerType.PlayerJoined, AutomationFieldKeys.ChatText));
            Assert.NotNull(catalog.Find(AutomationTriggerType.Cron, AutomationFieldKeys.ScheduledLocalTime));
            Assert.Null(catalog.Find(AutomationTriggerType.Cron, AutomationFieldKeys.ActorCrossplatformId));
            Assert.NotNull(catalog.Find(
                AutomationTriggerType.BloodMoonPhaseEntered,
                AutomationFieldKeys.BloodMoonPhase));

            var chatText = catalog.Find(
                AutomationTriggerType.ChatMessage,
                AutomationFieldKeys.ChatText)!;
            Assert.Equal(AutomationFieldValueKind.Text, chatText.ValueKind);
            Assert.Equal(
                new[]
                {
                    AutomationConditionOperator.Equals,
                    AutomationConditionOperator.NotEquals,
                    AutomationConditionOperator.InSet
                },
                chatText.AllowedOperators);
        }

        [Fact]
        public void Typed_actions_are_saved_as_whitelisted_structured_scalars()
        {
            var store = new RecordingAutomationStore();
            var useCases = CreateUseCases(store, new ReadyDependencyCatalog());
            var actions = new AutomationActionDraft[]
            {
                new BroadcastMessageActionDraft("broadcast", "Server restarting soon"),
                new PrivateMessageActionDraft("private", AutomationTarget.TriggerPlayer, "Welcome"),
                new AnnouncementActionDraft("announcement", "Blood moon incoming"),
                new GrantItemActionDraft("item", AutomationTarget.TriggerPlayer, "resource-iron", 3),
                new GrantRewardPackageActionDraft("package", AutomationTarget.TriggerPlayer, "starter"),
                new AdjustEconomyActionDraft("economy", AutomationTarget.TriggerPlayer, 25),
                new KickPlayerActionDraft("kick", AutomationTarget.StablePlayer("player-2"), "Policy violation"),
                new MutePlayerActionDraft("mute", AutomationTarget.TriggerPlayer, TimeSpan.FromMinutes(5), "Spam"),
                new RestrictedCommandActionDraft("command", AutomationTarget.Global, "server-status"),
                new DiscordMessageActionDraft("discord", AutomationTarget.DiscordTarget("ops"), "Server online")
            };

            var saved = useCases.Create(Draft(actions: actions), Owner);

            Assert.Equal(new[]
            {
                "BroadcastMessage", "PrivateMessage", "Announcement", "GrantItem",
                "GrantRewardPackage", "AdjustEconomy", "KickPlayer", "MutePlayer",
                "RestrictedCommand", "DiscordMessage"
            }, saved.Actions.Select(action => action.Type));
            Assert.Equal("resource-iron", saved.Actions[3].TextValue);
            Assert.Equal(3, saved.Actions[3].Amount);
            Assert.Equal("player-2", saved.Actions[6].ReferenceId);
            Assert.Equal("server-status", saved.Actions[8].TextValue);
            Assert.Equal("ops", saved.Actions[9].ReferenceId);

            var publicPropertyNames = actions
                .SelectMany(action => action.GetType().GetProperties())
                .Select(property => property.Name)
                .ToArray();
            Assert.DoesNotContain(publicPropertyNames, name =>
                name.IndexOf("payload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("commandtext", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("commandline", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Validator_rejects_cross_trigger_fields_and_disabled_dependencies()
        {
            var dependencies = new ReadyDependencyCatalog();
            dependencies.States["DiscordMessage"] = AutomationDependencyState.Disabled(
                "automation_dependency_discord_disabled");
            var validator = new AutomationRuleValidator(new AutomationFieldCatalog(), dependencies);
            var draft = Draft(
                condition: AutomationCondition.TextEquals(
                    "chat-condition",
                    AutomationFieldKeys.ChatText,
                    "hello"),
                actions: new AutomationActionDraft[]
                {
                    new DiscordMessageActionDraft(
                        "discord",
                        AutomationTarget.DiscordTarget("ops"),
                        "Hello")
                });

            var result = validator.Validate(draft, Owner);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue =>
                issue.Code == "automation_trigger_field_not_allowed" &&
                issue.Path == "condition.chat-condition");
            Assert.Contains(result.Issues, issue =>
                issue.Code == "automation_dependency_discord_disabled" &&
                issue.Path == "actions[0]");
        }

        [Fact]
        public void Rule_configuration_and_dry_run_require_an_owner_actor()
        {
            var dependencies = new ReadyDependencyCatalog();
            var validator = new AutomationRuleValidator(new AutomationFieldCatalog(), dependencies);
            var nonOwner = new AuthenticatedActor("admin-subject", AutomationActorRole.Admin);
            var useCases = new AutomationRuleUseCases(
                new RecordingAutomationStore(),
                validator,
                () => Utc(1, 0));
            var dryRun = new DryRunAutomationRuleUseCase(
                validator,
                new AutomationConditionEvaluator(TimeZoneInfo.Utc),
                dependencies,
                new RecordingTargetResolver());

            var createError = Assert.Throws<AutomationAuthorizationException>(() =>
                useCases.Create(Draft(), nonOwner));
            var dryRunError = Assert.Throws<AutomationAuthorizationException>(() =>
                dryRun.Execute(Draft(), Snapshot(actorGroup: "member"), nonOwner));

            Assert.Equal("automation_owner_required", createError.Code);
            Assert.Equal("automation_owner_required", dryRunError.Code);
        }

        [Fact]
        public void Condition_evaluator_records_unknown_for_each_missing_value_path()
        {
            var evaluator = new AutomationConditionEvaluator(TimeZoneInfo.Utc);
            var condition = AutomationCondition.All(
                "root",
                AutomationCondition.PlayerGroup(
                    "group",
                    AutomationFieldKeys.ActorGroup,
                    "member"),
                AutomationCondition.NumberRange(
                    "permission",
                    AutomationFieldKeys.ActorPermissionLevel,
                    0,
                    100));

            var result = evaluator.Evaluate(
                condition,
                AutomationTriggerType.PlayerJoined,
                Snapshot(actorGroup: null, permissionLevel: null));

            Assert.Equal(AutomationTruth.Unknown, result.Truth);
            Assert.Equal(new[] { "group", "permission", "root" },
                result.Trace.Select(item => item.NodeId));
            Assert.All(result.Trace, item => Assert.Equal(AutomationTruth.Unknown, item.Truth));
            Assert.False(result.Trace[0].IsValueKnown);
            Assert.False(result.Trace[1].IsValueKnown);
        }

        [Fact]
        public void Dry_run_evaluates_and_resolves_targets_without_accepting_side_effect_ports()
        {
            var dependencies = new ReadyDependencyCatalog();
            var validator = new AutomationRuleValidator(new AutomationFieldCatalog(), dependencies);
            var targets = new RecordingTargetResolver();
            targets.MissingReferences.Add("missing-player");
            var useCase = new DryRunAutomationRuleUseCase(
                validator,
                new AutomationConditionEvaluator(TimeZoneInfo.Utc),
                dependencies,
                targets);
            var draft = Draft(actions: new AutomationActionDraft[]
            {
                new PrivateMessageActionDraft("trigger", AutomationTarget.TriggerPlayer, "Welcome"),
                new KickPlayerActionDraft(
                    "missing",
                    AutomationTarget.StablePlayer("missing-player"),
                    "Review required")
            });

            var result = useCase.Execute(draft, Snapshot(actorGroup: "member"), Owner);

            Assert.True(result.Validation.IsValid);
            Assert.Equal(AutomationTruth.Matched, result.Evaluation!.Truth);
            Assert.True(result.PlannedActions[0].Target.IsResolved);
            Assert.Equal("player-1", result.PlannedActions[0].Target.ResolvedId);
            Assert.True(result.PlannedActions[0].WouldExecute);
            Assert.False(result.PlannedActions[1].Target.IsResolved);
            Assert.False(result.PlannedActions[1].WouldExecute);
            Assert.Equal(2, targets.ResolveCount);

            var constructorPorts = typeof(DryRunAutomationRuleUseCase)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .ToArray();
            Assert.DoesNotContain(typeof(IAutomationStore), constructorPorts);
        }

        [Fact]
        public void Create_read_and_update_use_optimistic_versions_and_preserve_creation_time()
        {
            var store = new RecordingAutomationStore();
            var now = Utc(1, 0);
            var useCases = new AutomationRuleUseCases(
                store,
                new AutomationRuleValidator(
                    new AutomationFieldCatalog(),
                    new ReadyDependencyCatalog()),
                () => now);

            var created = useCases.Create(Draft(name: "Welcome"), Owner);
            now = Utc(2, 0);
            var updated = useCases.Update(Draft(expectedVersion: 1, name: "Welcome v2"), Owner);

            Assert.Equal(1, created.Version);
            Assert.Equal(2, updated.Version);
            Assert.Equal(created.CreatedAtUtc, updated.CreatedAtUtc);
            Assert.Equal(now, updated.UpdatedAtUtc);
            Assert.Equal("Welcome v2", useCases.Find("rule-1", Owner)!.Name);
            Assert.Single(useCases.List(Owner));

            var conflict = Assert.Throws<AutomationVersionConflictException>(() =>
                useCases.Update(Draft(expectedVersion: 1, name: "stale"), Owner));
            Assert.Equal("automation_rule_version_conflict", conflict.Message);
        }

        [Fact]
        public void Delete_tombstones_the_expected_version_and_hides_the_rule()
        {
            var store = new RecordingAutomationStore();
            var now = Utc(1, 0);
            var useCases = new AutomationRuleUseCases(
                store,
                new AutomationRuleValidator(
                    new AutomationFieldCatalog(),
                    new ReadyDependencyCatalog()),
                () => now);
            useCases.Create(Draft(), Owner);
            now = Utc(2, 0);

            useCases.Delete("rule-1", expectedVersion: 1, Owner);

            Assert.Null(useCases.Find("rule-1", Owner));
            Assert.Empty(useCases.List(Owner));
            Assert.Equal(2, store.TombstoneVersion);
            Assert.Equal(now, store.DeletedAtUtc);
            Assert.Throws<AutomationVersionConflictException>(() =>
                useCases.Delete("rule-1", expectedVersion: 1, Owner));
            Assert.Throws<AutomationVersionConflictException>(() =>
                useCases.Create(Draft(), Owner));
        }

        [Fact]
        public void Dry_run_rejects_a_snapshot_from_another_trigger_type()
        {
            var dependencies = new ReadyDependencyCatalog();
            var useCase = new DryRunAutomationRuleUseCase(
                new AutomationRuleValidator(new AutomationFieldCatalog(), dependencies),
                new AutomationConditionEvaluator(TimeZoneInfo.Utc),
                dependencies,
                new RecordingTargetResolver());
            var snapshot = Snapshot(actorGroup: "member") with { TriggerType = "PlayerLeft" };

            var result = useCase.Execute(Draft(), snapshot, Owner);

            Assert.False(result.Validation.IsValid);
            Assert.Null(result.Evaluation);
            Assert.Empty(result.PlannedActions);
            Assert.Contains(result.Validation.Issues, issue =>
                issue.Code == "automation_snapshot_trigger_mismatch");
        }

        private static AutomationRuleUseCases CreateUseCases(
            RecordingAutomationStore store,
            IAutomationDependencyCatalog dependencies) =>
            new(
                store,
                new AutomationRuleValidator(new AutomationFieldCatalog(), dependencies),
                () => Utc(1, 0));

        private static AutomationRuleDraft Draft(
            long expectedVersion = 0,
            string name = "Welcome",
            AutomationCondition? condition = null,
            IReadOnlyList<AutomationActionDraft>? actions = null) =>
            new(
                "rule-1",
                expectedVersion,
                name,
                true,
                AutomationTriggerType.PlayerJoined,
                condition ?? AutomationCondition.PlayerGroup(
                    "group",
                    AutomationFieldKeys.ActorGroup,
                    "member"),
                actions ?? new AutomationActionDraft[]
                {
                    new PrivateMessageActionDraft(
                        "message",
                        AutomationTarget.TriggerPlayer,
                        "Welcome")
                },
                TimeSpan.FromMinutes(1),
                AutomationCooldownScope.RulePlayer,
                AutomationConcurrencyPolicy.QueueOne,
                AutomationFailurePolicy.Continue);

        private static AutomationTriggerSnapshot Snapshot(
            string? actorGroup,
            int? permissionLevel = 10) =>
            new(
                "trigger-1",
                "PlayerJoined",
                Utc(0, 30),
                "player-1",
                7,
                actorGroup,
                permissionLevel,
                null,
                null,
                null,
                Array.Empty<string>());

        private static DateTimeOffset Utc(int hour, int minute) =>
            new(2026, 7, 27, hour, minute, 0, TimeSpan.Zero);

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Application")]

        private sealed class ReadyDependencyCatalog : IAutomationDependencyCatalog
        {
            public IDictionary<string, AutomationDependencyState> States { get; } =
                new Dictionary<string, AutomationDependencyState>(StringComparer.Ordinal);

            public AutomationDependencyState Resolve(AutomationAction action) =>
                States.TryGetValue(action.Type, out var state)
                    ? state
                    : AutomationDependencyState.Ready;
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingTargetResolver : IAutomationTargetResolver
        {
            public ISet<string> MissingReferences { get; } =
                new HashSet<string>(StringComparer.Ordinal);

            public int ResolveCount { get; private set; }

            public AutomationTargetResolution Resolve(
                AutomationAction action,
                AutomationTriggerSnapshot snapshot)
            {
                ResolveCount++;
                if (action.TargetKind == AutomationTargetKind.Global.ToString())
                    return AutomationTargetResolution.Resolved("global");
                if (action.TargetKind == AutomationTargetKind.TriggerPlayer.ToString())
                {
                    return snapshot.ActorCrossplatformId == null
                        ? AutomationTargetResolution.Unresolved("automation_target_trigger_player_missing")
                        : AutomationTargetResolution.Resolved(snapshot.ActorCrossplatformId);
                }
                if (action.ReferenceId == null || MissingReferences.Contains(action.ReferenceId))
                    return AutomationTargetResolution.Unresolved("automation_target_not_found");
                return AutomationTargetResolution.Resolved(action.ReferenceId);
            }
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingAutomationStore : IAutomationStore
        {
            private AutomationRule? rule;
            private bool deleted;

            public long? TombstoneVersion { get; private set; }
            public DateTimeOffset? DeletedAtUtc { get; private set; }

            public IReadOnlyList<AutomationRule> ListRules() =>
                rule == null || deleted ? Array.Empty<AutomationRule>() : new[] { rule };

            public AutomationRule? FindRule(string ruleId) =>
                rule != null && !deleted && string.Equals(rule.Id, ruleId, StringComparison.Ordinal)
                    ? rule
                    : null;

            public void SaveRule(AutomationRule next, long expectedVersion)
            {
                var currentVersion = rule?.Version ?? 0;
                if (deleted || currentVersion != expectedVersion || next.Version != expectedVersion + 1)
                    throw new AutomationVersionConflictException();
                rule = next;
            }

            public void DeleteRule(
                string ruleId,
                long expectedVersion,
                DateTimeOffset deletedAtUtc)
            {
                if (rule == null || deleted ||
                    !string.Equals(rule.Id, ruleId, StringComparison.Ordinal) ||
                    rule.Version != expectedVersion)
                {
                    throw new AutomationVersionConflictException();
                }
                deleted = true;
                TombstoneVersion = checked(expectedVersion + 1);
                DeletedAtUtc = deletedAtUtc;
            }

            public void SaveTrigger(AutomationTriggerSnapshot trigger) =>
                throw new InvalidOperationException("dry-run must not save triggers");

            public AutomationExecutionStartResult TryStartExecution(AutomationExecutionRecord execution) =>
                throw new InvalidOperationException("dry-run must not start executions");

            public void RecordConditionResult(AutomationConditionExecutionResult result) =>
                throw new InvalidOperationException("dry-run must not record conditions");

            public void RecordActionResult(AutomationActionExecutionResult result) =>
                throw new InvalidOperationException("dry-run must not record actions");

            public IReadOnlyList<AutomationConditionExecutionResult> ListConditionResults(string executionId) =>
                Array.Empty<AutomationConditionExecutionResult>();

            public IReadOnlyList<AutomationActionExecutionResult> ListActionResults(string executionId) =>
                Array.Empty<AutomationActionExecutionResult>();
        }
    }
}
