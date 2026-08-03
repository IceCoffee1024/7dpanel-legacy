using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.Domain.Automations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Automation")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteAutomationIntegrationsTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 4, 0, 0, TimeSpan.Zero);

        private static readonly string[] ExpectedTables =
        {
            "automation_action_results",
            "automation_actions",
            "automation_condition_nodes",
            "automation_condition_results",
            "automation_condition_set_values",
            "automation_executions",
            "automation_integration_operation_audit",
            "automation_rules",
            "automation_trigger_gaps",
            "automation_triggers",
            "discord_binding_codes",
            "discord_bindings",
            "discord_bridge_messages",
            "discord_command_settings",
            "discord_deliveries",
            "discord_delivery_attempts",
            "discord_interaction_secrets",
            "discord_interactions",
            "discord_secrets",
            "discord_settings",
            "discord_targets",
            "geoip_cache",
            "geoip_country_rules",
            "geoip_decisions",
            "geoip_network_rules",
            "geoip_secrets",
            "geoip_settings"
        };

        [Fact]
        public void Upgrade_from_011_creates_only_012_once_with_foreign_keys_checks_and_safe_gap_references()
        {
            using var database = new TemporaryDatabase(upgrade: false);
            UpgradeThrough011(database.ConnectionFactory);
            using (var before = database.ConnectionFactory.Open())
            {
                before.Execute(
                    @"INSERT INTO game_events (event_id, event_type, occurred_utc, observed_utc)
                      VALUES ('before-012', 'PlayerJoined', 1785124800000, 1785124800000);");
            }

            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                ExpectedTables,
                connection.Query<string>(
                    @"SELECT name FROM sqlite_master
                      WHERE type = 'table' AND name IN (" +
                    string.Join(",", ExpectedTables.Select((_, index) => "@p" + index)) +
                    ") ORDER BY name;",
                    ExpectedTables.Select((name, index) =>
                            new KeyValuePair<string, object>("p" + index, name))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM game_events WHERE event_id = 'before-012';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM SchemaVersions
                  WHERE ScriptName LIKE '%012_AutomationIntegrations.sql';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM SchemaVersions
                  WHERE ScriptName LIKE '%015_DiscordInteractionPersistence.sql';"));
            Assert.Empty(connection.Query<ForeignKeyViolation>("PRAGMA foreign_key_check;"));

            var gapColumns = connection.Query<TableColumn>(
                    "PRAGMA table_info([automation_trigger_gaps]);")
                .Select(column => column.name)
                .ToArray();
            Assert.Equal(new[] { "trigger_id", "gap_id" }, gapColumns);
            Assert.Contains(
                connection.Query<ForeignKeyRow>("PRAGMA foreign_key_list([automation_trigger_gaps]);"),
                key => key.from == "gap_id" && key.table == "game_event_gaps");
            Assert.Contains(
                connection.Query<TableColumn>("PRAGMA table_info([automation_rules]);"),
                column => column.name == "deleted");

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO automation_rules (
                      rule_id, version, name, trigger_type, enabled, cooldown_seconds,
                      cooldown_scope, concurrency_policy, failure_policy, created_utc, updated_utc)
                  VALUES ('bad-rule', 1, 'Bad', 'PlayerJoined', 2, 0,
                      'Rule', 'SkipIfRunning', 'StopOnFailure', 1, 1);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO geoip_settings (
                      singleton_id, version, enabled, provider, failure_mode,
                      bypass_admins, rejection_message)
                  VALUES (1, 1, 1, 'Local', 'Sometimes', 0, 'Denied');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO geoip_secrets (
                      secret_key, secret_value, fingerprint, updated_utc)
                  VALUES ('unapproved-secret', 'value', 'fingerprint', 1);"));
        }

        [Fact]
        public void Unified_audit_projection_adds_only_stable_non_sensitive_integration_summaries()
        {
            using var database = new TemporaryDatabase();
            using var connection = database.ConnectionFactory.Open();
            connection.Execute(
                @"INSERT INTO automation_rules (
                      rule_id, version, name, trigger_type, enabled, cooldown_seconds,
                      cooldown_scope, concurrency_policy, failure_policy, created_utc, updated_utc)
                  VALUES ('rule-audit', 1, 'Audit rule', 'ChatMessage', 1, 0,
                      'Rule', 'SkipIfRunning', 'StopOnFailure', 10, 10);
                  INSERT INTO automation_triggers (
                      trigger_id, trigger_type, occurred_utc, actor_crossplatform_id, chat_text)
                  VALUES ('trigger-audit', 'ChatMessage', 11, 'EOS-AUDIT', 'CHAT-SENTINEL');
                  INSERT INTO automation_executions (
                      execution_id, rule_id, trigger_id, status, correlation_id,
                      started_utc, completed_utc)
                  VALUES ('execution-audit', 'rule-audit', 'trigger-audit', 'Succeeded',
                      'wave5-execution', 12, 13);
                  INSERT INTO automation_integration_operation_audit (
                      operation_id, actor_subject, action, target_kind, target_id,
                      status, occurred_utc, correlation_id)
                  VALUES ('operation-audit', 'owner', 'discord.settings.update',
                      'DiscordSettings', 'singleton', 'Succeeded', 14, 'wave5-operation');
                  INSERT INTO discord_secrets (secret_key, secret_value, fingerprint, updated_utc)
                  VALUES ('botToken', 'SECRET-SENTINEL', 'fp', 15);
                  INSERT INTO discord_targets (target_key, delivery_mode, channel_id, enabled)
                  VALUES ('public', 'Bot', 'channel-secret', 1);
                  INSERT INTO discord_deliveries (
                      delivery_id, business_key, target_key, status, content_text,
                      content_summary, retry_count, created_utc, completed_utc)
                  VALUES ('delivery-audit', 'business-audit', 'public', 'Succeeded',
                      'DISCORD-BODY-SENTINEL', 'safe summary', 0, 16, 17);
                  INSERT INTO geoip_cache (
                      canonical_ip, lookup_status, country_code, source, queried_utc, expires_utc)
                  VALUES ('203.0.113.77', 'Found', 'CN', 'Local', 18, 1000);
                  INSERT INTO geoip_decisions (
                      decision_id, occurred_utc, masked_ip, crossplatform_id,
                      decision, reason_code, lookup_status)
                  VALUES ('decision-audit', 19, '203.0.113.xxx', 'EOS-AUDIT',
                      'Denied', 'CountryDenied', 'Found');");

            var rows = connection.Query<AuditProjectionRow>(
                @"SELECT source_kind, source_id, actor_subject, target_ref, action,
                         status, correlation_id
                  FROM unified_audit_projection
                  WHERE source_id IN (
                      'execution-audit', 'operation-audit', 'delivery-audit', 'decision-audit')
                  ORDER BY source_kind;").ToArray();

            Assert.Equal(
                new[] { "automationExecution", "discordDelivery", "geoIpDecision", "integrationOperation" },
                rows.Select(row => row.source_kind));
            var rendered = string.Join("|", rows.SelectMany(row => new[]
            {
                row.source_id,
                row.actor_subject,
                row.target_ref,
                row.action,
                row.status,
                row.correlation_id
            }));
            Assert.DoesNotContain("CHAT-SENTINEL", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SECRET-SENTINEL", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DISCORD-BODY-SENTINEL", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("203.0.113", rendered, StringComparison.OrdinalIgnoreCase);

            var viewSql = connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            Assert.DoesNotContain(
                new[]
                {
                    "chat_text", "content_text", "secret_value", "canonical_ip",
                    "masked_ip", "token_value", "code_hash"
                },
                term => viewSql.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Automation_store_round_trips_rules_and_rejects_stale_versions()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteAutomationStore(database.ConnectionFactory);
            var first = Rule(version: 1, name: "Welcome");

            store.SaveRule(first, expectedVersion: 0);
            var saved = Assert.IsType<AutomationRule>(store.FindRule(first.Id));

            Assert.Equal(first.Name, saved.Name);
            Assert.Equal(first.ConditionRoot.NodeCount, saved.ConditionRoot.NodeCount);
            Assert.Equal(first.Actions.Select(action => action.Id), saved.Actions.Select(action => action.Id));
            Assert.Equal(new[] { "alpha", "beta" }, saved.ConditionRoot.Children[0].SetValues);
            Assert.True(saved.ConditionRoot.Children[2].Window!.Contains(
                new AutomationLocalTime("Asia/Shanghai", new AutomationTimeOfDay(0, 30))));

            var second = Rule(version: 2, name: "Welcome updated");
            store.SaveRule(second, expectedVersion: 1);
            Assert.Equal("Welcome updated", store.FindRule(first.Id)!.Name);
            Assert.Throws<AutomationVersionConflictException>(() =>
                store.SaveRule(Rule(version: 2, name: "Stale"), expectedVersion: 1));

            store.DeleteRule(first.Id, expectedVersion: 2, Now.AddMinutes(1));
            Assert.Null(store.FindRule(first.Id));
            Assert.Empty(store.ListRules());
            Assert.Throws<AutomationVersionConflictException>(() =>
                store.DeleteRule(first.Id, expectedVersion: 2, Now.AddMinutes(2)));
            Assert.Throws<AutomationVersionConflictException>(() =>
                store.SaveRule(Rule(version: 3, name: "Revive"), expectedVersion: 2));

            using var connection = database.ConnectionFactory.Open();
            var tombstone = connection.QuerySingle<RuleTombstoneRow>(
                @"SELECT version, enabled, deleted, updated_utc
                  FROM automation_rules WHERE rule_id = @RuleId;",
                new { RuleId = first.Id });
            Assert.Equal(3, tombstone.version);
            Assert.Equal(0, tombstone.enabled);
            Assert.Equal(1, tombstone.deleted);
            Assert.Equal(Milliseconds(Now.AddMinutes(1)), tombstone.updated_utc);
            Assert.Equal(4, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM automation_condition_nodes WHERE rule_id = @RuleId;",
                new { RuleId = first.Id }));
            Assert.Equal(2, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM automation_actions WHERE rule_id = @RuleId;",
                new { RuleId = first.Id }));
        }

        [Fact]
        public async Task Automation_execution_is_competition_safe_and_persists_per_node_and_action_evidence()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteAutomationStore(database.ConnectionFactory);
            var rule = Rule(version: 1, name: "Competition");
            store.SaveRule(rule, expectedVersion: 0);
            using (var connection = database.ConnectionFactory.Open())
            {
                connection.Execute(
                    @"INSERT INTO game_event_gaps (
                          gap_id, reason, started_utc, ended_utc, affected_count)
                      VALUES ('gap-immutable', 'QueueFull', @Started, @Ended, 1);",
                    new { Started = Milliseconds(Now), Ended = Milliseconds(Now.AddSeconds(1)) });
            }
            store.SaveTrigger(new AutomationTriggerSnapshot(
                "trigger-competition",
                rule.Trigger.Type,
                Now,
                "EOS-A",
                42,
                "players",
                0,
                "hello",
                null,
                null,
                new[] { "gap-immutable" }));

            var attempts = await Task.WhenAll(
                Task.Run(() => store.TryStartExecution(Execution("execution-a"))),
                Task.Run(() => store.TryStartExecution(Execution("execution-b"))));

            Assert.Single(attempts, attempt => attempt.WasCreated);
            Assert.Equal(attempts[0].Execution.ExecutionId, attempts[1].Execution.ExecutionId);
            var executionId = attempts[0].Execution.ExecutionId;
            store.RecordConditionResult(new AutomationConditionExecutionResult(
                executionId, "group", AutomationTruth.Matched, "set:matched"));
            store.RecordConditionResult(new AutomationConditionExecutionResult(
                executionId, "level", AutomationTruth.Unknown, "missing"));
            store.RecordActionResult(new AutomationActionExecutionResult(
                executionId,
                0,
                "Announcement",
                AutomationActionResultStatus.Succeeded,
                "automation:execution:0",
                null,
                Now.AddSeconds(1),
                Now.AddSeconds(2)));

            Assert.Equal(2, store.ListConditionResults(executionId).Count);
            Assert.Equal(AutomationTruth.Unknown, store.ListConditionResults(executionId)[1].Truth);
            Assert.Single(store.ListActionResults(executionId));
            store.SaveRule(
                Rule(version: 2, name: "Competition updated", replaceCondition: true),
                expectedVersion: 1);
            Assert.Equal(2, store.ListConditionResults(executionId).Count);
            using var verify = database.ConnectionFactory.Open();
            Assert.Equal(1, verify.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM automation_trigger_gaps
                  WHERE trigger_id = 'trigger-competition' AND gap_id = 'gap-immutable';"));
            Assert.Equal(1, verify.ExecuteScalar<int>(
                @"SELECT COUNT(*) FROM automation_executions
                  WHERE rule_id = 'rule-1' AND trigger_id = 'trigger-competition';"));
        }

        [Fact]
        public void Discord_store_keeps_secrets_dedicated_and_updates_delivery_attempts_binding_and_tokens_atomically()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteDiscordIntegrationStore(database.ConnectionFactory);
            var settings = new DiscordIntegrationSettings(
                1, true, DiscordIntegrationMode.Bot, "app", "guild", "channel",
                true, true, false, null, Now);
            store.SaveSettings(settings, expectedVersion: 0);
            Assert.Throws<DiscordIntegrationVersionConflictException>(() =>
                store.SaveSettings(settings, expectedVersion: 0));

            store.SetSecret(new DiscordSecretValue("botToken", "top-secret", "fp-1", Now));
            Assert.Equal("top-secret", store.GetSecret("botToken")!.SecretValue);
            Assert.Equal("fp-1", Assert.Single(store.ListSecretMetadata()).Fingerprint);
            Assert.DoesNotContain(
                typeof(DiscordIntegrationSettings).GetProperties(),
                property => property.Name == nameof(DiscordSecretValue.SecretValue));
            Assert.DoesNotContain(
                typeof(DiscordSecretMetadata).GetProperties(),
                property => property.Name == nameof(DiscordSecretValue.SecretValue));

            store.SaveTarget(new DiscordTarget("public", "Bot", "channel", true));
            var queued = new DiscordDelivery(
                "delivery-1", "business-1", "public", DiscordDeliveryStatus.Pending,
                "body", "summary", null, 0, Now, null);
            Assert.True(store.EnqueueDelivery(queued).WasCreated);
            Assert.False(store.EnqueueDelivery(queued with { DeliveryId = "delivery-replay" }).WasCreated);
            store.BeginDeliveryAttempt("delivery-1", 1, Now.AddSeconds(1));
            Assert.Throws<InvalidOperationException>(() => store.CompleteDeliveryAttempt(
                "delivery-1", 99, DiscordDeliveryStatus.RetryScheduled,
                Now.AddSeconds(2), "network", Now.AddMinutes(1)));
            Assert.Equal(DiscordDeliveryStatus.Sending, store.FindDelivery("delivery-1")!.Status);
            Assert.Equal(DiscordDeliveryStatus.Sending, Assert.Single(
                store.ListDeliveryAttempts("delivery-1")).Status);

            Assert.Equal(1, store.RecoverSendingAsResultUnknown(Now.AddSeconds(3)));
            Assert.Equal(DiscordDeliveryStatus.ResultUnknown, store.FindDelivery("delivery-1")!.Status);
            Assert.Equal(DiscordDeliveryStatus.ResultUnknown, Assert.Single(
                store.ListDeliveryAttempts("delivery-1")).Status);

            var codeHash = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            store.SaveBindingCode(new DiscordBindingCode(
                "code-1", "EOS-A", "1234", codeHash, Now.AddMinutes(5)));
            Assert.NotNull(store.TryConsumeBindingCode(codeHash, "discord-user", Now.AddMinutes(1)));
            Assert.Null(store.TryConsumeBindingCode(codeHash, "discord-other", Now.AddMinutes(2)));

            store.SaveInteractionWithToken(
                new DiscordInteraction("interaction-expired", "status", "Pending", Now, null),
                "expired-token");
            store.SaveInteractionWithToken(
                new DiscordInteraction("interaction-fresh", "status", "Pending", Now.AddMinutes(5), null),
                "fresh-token");
            store.SaveInteractionWithToken(
                new DiscordInteraction("interaction-fresh", "status", "Pending", Now.AddMinutes(5), null),
                "replacement-token");
            Assert.Null(store.GetInteractionToken("interaction-expired", Now));
            Assert.Equal("fresh-token", store.GetInteractionToken(
                "interaction-fresh", Now)!.TokenValue);
            Assert.Equal(0, store.ClearExpiredInteractionTokens(Now));

            Assert.True(store.TryRegisterBridgeMessage(
                "bridge-1", "Discord", "message-1", Now.AddMinutes(5)));
            Assert.False(store.TryRegisterBridgeMessage(
                "bridge-2", "Discord", "message-1", Now.AddMinutes(5)));
        }

        [Fact]
        public void GeoIp_store_canonicalizes_cache_keys_hides_secrets_and_pages_decisions_by_stable_keyset()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteGeoIpAccessPolicyStore(database.ConnectionFactory);
            var settings = new GeoIpAccessPolicySettings(
                1, true, "Local", GeoIpFailureMode.FailOpen, true, "Not available");
            store.SaveSettings(settings, expectedVersion: 0);
            Assert.Throws<GeoIpAccessPolicyVersionConflictException>(() =>
                store.SaveSettings(settings, expectedVersion: 0));

            store.ApplySecretChanges(new[]
            {
                new GeoIpSecretMutation(
                    GeoIpSecretKeys.MaxMindAccountId,
                    new GeoIpSecretValue(
                        GeoIpSecretKeys.MaxMindAccountId,
                        "12345",
                        "account-fp-1",
                        Now)),
                new GeoIpSecretMutation(
                    GeoIpSecretKeys.MaxMindLicenseKey,
                    new GeoIpSecretValue(
                        GeoIpSecretKeys.MaxMindLicenseKey,
                        "geo-secret-one",
                        "license-fp-1",
                        Now))
            });
            Assert.Equal("12345", store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Equal("geo-secret-one", store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey)!.SecretValue);
            Assert.Equal(
                new[] { "account-fp-1", "license-fp-1" },
                store.ListSecretMetadata().Select(item => item.Fingerprint));

            Assert.Throws<ArgumentException>(() => store.ApplySecretChanges(new[]
            {
                new GeoIpSecretMutation(
                    GeoIpSecretKeys.MaxMindAccountId,
                    new GeoIpSecretValue(
                        GeoIpSecretKeys.MaxMindAccountId,
                        "67890",
                        "account-fp-2",
                        Now.AddMinutes(1))),
                new GeoIpSecretMutation(
                    "unapproved-secret",
                    new GeoIpSecretValue(
                        "unapproved-secret",
                        "must-not-be-written",
                        "unapproved-fp",
                        Now.AddMinutes(1)))
            }));
            Assert.Equal("12345", store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Equal("account-fp-1", store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.Fingerprint);

            store.ApplySecretChanges(new[]
            {
                new GeoIpSecretMutation(
                    GeoIpSecretKeys.MaxMindAccountId,
                    new GeoIpSecretValue(
                        GeoIpSecretKeys.MaxMindAccountId,
                        "67890",
                        "account-fp-2",
                        Now.AddMinutes(2))),
                new GeoIpSecretMutation(GeoIpSecretKeys.MaxMindLicenseKey, null)
            });
            Assert.Equal("67890", store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Null(store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey));
            Assert.Equal("account-fp-2", Assert.Single(store.ListSecretMetadata()).Fingerprint);
            Assert.DoesNotContain(
                typeof(GeoIpAccessPolicySettings).GetProperties(),
                property => property.Name == nameof(GeoIpSecretValue.SecretValue));
            Assert.DoesNotContain(
                typeof(GeoIpSecretMetadata).GetProperties(),
                property => property.Name == nameof(GeoIpSecretValue.SecretValue));

            store.ReplaceNetworkRules(new[]
            {
                new GeoIpNetworkRule("network-1", "10.0.0.0/8", "Allow", 0)
            });
            store.ReplaceCountryRules(new[] { new GeoIpCountryRule("CN", "Deny") });
            Assert.Equal("10.0.0.0/8", Assert.Single(store.ListNetworkRules()).NetworkCidr);
            Assert.Equal("CN", Assert.Single(store.ListCountryRules()).CountryCode);

            store.UpsertCache(new GeoIpCacheEntry(
                "2001:0DB8:0:0:0:0:0:1", "Found", "CN", "Local", "v1",
                Now, Now.AddHours(1)));
            var cache = Assert.IsType<GeoIpCacheEntry>(store.FindCache("2001:db8::1"));
            Assert.Equal("2001:db8::1", cache.CanonicalIp);
            Assert.Throws<FormatException>(() => store.FindCache("not-an-ip"));

            store.RecordDecision(Decision("decision-a", Now));
            store.RecordDecision(Decision("decision-b", Now.AddSeconds(1)));
            store.RecordDecision(Decision("decision-c", Now.AddSeconds(1)));
            var first = store.QueryDecisions(new GeoIpDecisionQuery(2));
            var second = store.QueryDecisions(new GeoIpDecisionQuery(2, first.NextKeyset));

            Assert.Equal(new[] { "decision-c", "decision-b" },
                first.Decisions.Select(decision => decision.DecisionId));
            Assert.Equal(new[] { "decision-a" },
                second.Decisions.Select(decision => decision.DecisionId));
            Assert.Empty(first.Decisions.Select(decision => decision.DecisionId)
                .Intersect(second.Decisions.Select(decision => decision.DecisionId), StringComparer.Ordinal));
        }

        private static AutomationRule Rule(
            long version,
            string name,
            bool replaceCondition = false)
        {
            var condition = replaceCondition
                ? AutomationCondition.TextEquals("replacement", "player.group", "omega")
                : AutomationCondition.All(
                    "root",
                    AutomationCondition.InSet("group", "player.group", new[] { "alpha", "beta" }),
                    AutomationCondition.NumberRange("level", "player.level", 1, 300),
                    AutomationCondition.TimeWindow(
                        "night",
                        "server.localTime",
                        new AutomationTimeWindow(
                            "Asia/Shanghai",
                            new AutomationTimeOfDay(23, 0),
                            new AutomationTimeOfDay(1, 0))));
            return new AutomationRule(
                "rule-1",
                version,
                name,
                true,
                new AutomationTrigger("PlayerJoined"),
                condition,
                new[]
                {
                    new AutomationAction("action-1", "Announcement", "Global", "Welcome"),
                    new AutomationAction("action-2", "RewardPackage", "Player", referenceId: "starter")
                },
                TimeSpan.FromSeconds(30),
                AutomationCooldownScope.RulePlayer,
                AutomationConcurrencyPolicy.QueueOne,
                AutomationFailurePolicy.Continue,
                Now,
                Now);
        }

        private static AutomationExecutionRecord Execution(string executionId) => new(
            executionId,
            "rule-1",
            "trigger-competition",
            AutomationExecutionStatus.Running,
            "correlation-competition",
            Now,
            null,
            null);

        private static GeoIpDecision Decision(string decisionId, DateTimeOffset occurredAtUtc) => new(
            decisionId,
            occurredAtUtc,
            "203.0.113.xxx",
            "EOS-A",
            "Denied",
            "CountryDenied",
            "Found");

        private static long Milliseconds(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

        private static void UpgradeThrough011(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName =>
                        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, 11).Any(version => resourceName.IndexOf(
                            $".Migrations.{version:D3}_",
                            StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-automation-integrations-tests",
                Guid.NewGuid().ToString("N"));

            public TemporaryDatabase(bool upgrade = true)
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                if (upgrade) Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class TableColumn
        {
            public string name { get; set; } = string.Empty;
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class ForeignKeyRow
        {
            public string table { get; set; } = string.Empty;
            public string from { get; set; } = string.Empty;
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class ForeignKeyViolation
        {
            public string table { get; set; } = string.Empty;
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class RuleTombstoneRow
        {
            public long version { get; set; }
            public long enabled { get; set; }
            public long deleted { get; set; }
            public long updated_utc { get; set; }
        }

        [Trait("Capability", "Automation")]

        [Trait("Boundary", "Persistence")]

        private sealed class AuditProjectionRow
        {
            public string source_kind { get; set; } = string.Empty;
            public string source_id { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public string? target_ref { get; set; }
            public string action { get; set; } = string.Empty;
            public string status { get; set; } = string.Empty;
            public string? correlation_id { get; set; }
        }
    }
}
