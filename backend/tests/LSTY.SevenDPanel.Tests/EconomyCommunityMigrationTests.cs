using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using DbUp;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Economy")]
    [Trait("Boundary", "Persistence")]
    public sealed class EconomyCommunityMigrationTests
    {
        private static readonly string[] ExpectedTables =
        {
            "achievement_definitions",
            "achievement_progress",
            "cities",
            "daily_reward_claims",
            "economy_accounts",
            "economy_entries",
            "economy_reservations",
            "economy_transactions",
            "friend_requests",
            "friendships",
            "grant_operation_entries",
            "grant_operations",
            "online_reward_rules",
            "player_homes",
            "player_return_points",
            "redeem_attempts",
            "redeem_codes",
            "reward_eligibilities",
            "reward_package_entries",
            "reward_packages",
            "shop_products",
            "shop_purchases",
            "teleport_cooldowns",
            "teleport_friend_requests",
            "teleport_operations",
            "teleport_settings",
            "vote_ballots",
            "vote_configurations",
            "vote_eligible_players",
            "vote_rounds"
        };

        [Fact]
        public void Upgrade_from_010_creates_the_fixed_schema_and_can_be_repeated()
        {
            using var database = new TemporaryDatabase();
            UpgradeThrough010(database.ConnectionFactory);
            using (var before = database.ConnectionFactory.Open())
            {
                before.Execute(
                    @"INSERT INTO game_events (event_id, event_type, occurred_utc, observed_utc)
                      VALUES ('before-011', 'PlayerJoined', 1785024000000, 1785024000000);");
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
                "SELECT COUNT(*) FROM game_events WHERE event_id = 'before-011';"));
            Assert.Empty(connection.Query<ForeignKeyViolation>("PRAGMA foreign_key_check;"));

            AssertExpectedIndexes(connection);
            AssertExpectedForeignKeys(connection);
            AssertTypedColumnsAndChecks(connection);
        }

        [Fact]
        public void Schema_rejects_invalid_money_typed_rewards_sensitive_codes_and_operation_states()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO economy_accounts (
                      account_id, account_kind, crossplatform_id, enabled, is_frozen,
                      posted_balance, reserved_debit, created_at_utc, updated_at_utc, row_version)
                  VALUES ('player:bad', 'Player', 'EOS-bad', 1, 0, -1, 0, 1, 1, 0);"));

            SeedRewardPackage(connection);
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO reward_package_entries (
                      entry_id, package_id, ordinal, entry_kind, currency_amount)
                  VALUES ('bad-item', 'package-1', 0, 'Item', 10);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO reward_package_entries (
                      entry_id, package_id, ordinal, entry_kind, registered_action)
                  VALUES ('bad-action', 'package-1', 0, 'RegisteredAction', 'console');"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO redeem_codes (
                      code_id, normalized_code_digest, masked_prefix, normalization_version,
                      reward_package_id, enabled, redemption_count, created_at_utc, updated_at_utc, row_version)
                  VALUES ('bad-code', 'plaintext-code', 'CODE', 1, 'package-1', 1, 0, 1, 1, 0);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO grant_operations (
                      operation_id, package_id, crossplatform_id, expected_entity_id,
                      expected_world_id, state, idempotency_key, actor_kind, actor_id,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES ('bad-grant', 'package-1', 'EOS-1', 1, 'world', 'Unknown',
                      'bad-grant', 'Owner', 'owner', 1, 1, 0);"));
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO vote_configurations (
                      configuration_id, vote_kind, enabled, duration_ms, threshold_percent,
                      minimum_participants, initiator_cooldown_ms, target_cooldown_ms,
                      global_cooldown_ms, mutual_exclusion_scope, allow_vote_change,
                      updated_at_utc, row_version)
                  VALUES ('bad-vote', 'Kick', 1, 60000, 101, 1, 0, 0, 0,
                      'global', 0, 1, 0);"));
        }

        [Fact]
        public void Unified_audit_projection_adds_only_stable_non_sensitive_summaries()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();
            using var connection = database.ConnectionFactory.Open();
            SeedAuditSources(connection);

            var rows = connection.Query<AuditProjectionRow>(
                @"SELECT source_kind, source_id, actor_subject, target_ref, action, status,
                         correlation_id
                  FROM unified_audit_projection
                  WHERE correlation_id LIKE 'wave4-%'
                  ORDER BY source_kind, source_id;").ToArray();

            Assert.Equal(7, rows.Length);
            Assert.Equal(
                new[]
                {
                    "commerceOperation", "commerceOperation", "economyTransaction",
                    "rewardEligibility", "rewardGrant", "teleportOperation", "voteRound"
                },
                rows.Select(row => row.source_kind));
            Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.action)));
            Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.status)));

            var viewSql = connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            Assert.Contains("economy_transactions", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("grant_operations", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("shop_purchases", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("redeem_attempts", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("reward_eligibilities", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("teleport_operations", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("vote_rounds", viewSql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                new[]
                {
                    "normalized_code_digest", "masked_prefix", "item_internal_name",
                    "item_kind", "quantity", "destination_x", "origin_x", "reason"
                },
                term => viewSql.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            var rendered = string.Join("|", rows.SelectMany(row => new[]
            {
                row.source_id, row.actor_subject, row.target_ref, row.action,
                row.status, row.correlation_id
            }));
            Assert.DoesNotContain("SECRETITEM", rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(new string('a', 64), rendered, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("123.125", rendered, StringComparison.OrdinalIgnoreCase);
        }

        private static void AssertExpectedIndexes(SqliteConnection connection)
        {
            var expected = new[]
            {
                "ix_economy_accounts_balance_keyset",
                "ix_economy_transactions_occurred_keyset",
                "ix_grant_operations_created_keyset",
                "ix_redeem_attempts_attempted_keyset",
                "ix_shop_purchases_created_keyset",
                "ix_teleport_operations_created_keyset",
                "ix_vote_rounds_opened_keyset",
                "ux_daily_reward_claim_period",
                "ux_economy_transaction_idempotency",
                "ux_redeem_player",
                "ux_reward_eligibility",
                "ux_teleport_friend_requests_idempotency",
                "ux_teleport_friend_requests_target_pending",
                "ux_vote_ballot"
            };
            Assert.Equal(
                expected.OrderBy(value => value, StringComparer.Ordinal),
                connection.Query<string>(
                    @"SELECT name FROM sqlite_master
                      WHERE type = 'index' AND name IN (" +
                    string.Join(",", expected.Select((_, index) => "@p" + index)) +
                    ") ORDER BY name;",
                    expected.Select((name, index) =>
                            new KeyValuePair<string, object>("p" + index, name))
                        .ToDictionary(pair => pair.Key, pair => pair.Value)));
        }

        private static void AssertExpectedForeignKeys(SqliteConnection connection)
        {
            var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["achievement_definitions"] = new[] { "reward_package_id->reward_packages:RESTRICT" },
                ["achievement_progress"] = new[]
                {
                    "achievement_id->achievement_definitions:CASCADE",
                    "grant_operation_id->grant_operations:RESTRICT"
                },
                ["daily_reward_claims"] = new[]
                {
                    "grant_operation_id->grant_operations:RESTRICT",
                    "reward_package_id->reward_packages:RESTRICT"
                },
                ["economy_entries"] = new[]
                {
                    "account_id->economy_accounts:RESTRICT",
                    "transaction_id->economy_transactions:RESTRICT"
                },
                ["economy_reservations"] = new[]
                {
                    "account_id->economy_accounts:RESTRICT",
                    "captured_transaction_id->economy_transactions:RESTRICT"
                },
                ["friend_requests"] = new[] { "friendship_id->friendships:RESTRICT" },
                ["grant_operation_entries"] = new[]
                {
                    "ledger_transaction_id->economy_transactions:RESTRICT",
                    "operation_id->grant_operations:CASCADE",
                    "package_entry_id->reward_package_entries:RESTRICT"
                },
                ["grant_operations"] = new[]
                {
                    "compensates_operation_id->grant_operations:RESTRICT",
                    "package_id->reward_packages:RESTRICT",
                    "reservation_id->economy_reservations:RESTRICT"
                },
                ["online_reward_rules"] = new[] { "reward_package_id->reward_packages:RESTRICT" },
                ["player_return_points"] = new[] { "source_operation_id->teleport_operations:RESTRICT" },
                ["redeem_attempts"] = new[]
                {
                    "code_id->redeem_codes:RESTRICT",
                    "grant_operation_id->grant_operations:RESTRICT"
                },
                ["redeem_codes"] = new[] { "reward_package_id->reward_packages:RESTRICT" },
                ["reward_eligibilities"] = new[] { "grant_operation_id->grant_operations:RESTRICT" },
                ["reward_package_entries"] = new[] { "package_id->reward_packages:CASCADE" },
                ["shop_products"] = new[] { "reward_package_id->reward_packages:RESTRICT" },
                ["shop_purchases"] = new[]
                {
                    "captured_transaction_id->economy_transactions:RESTRICT",
                    "grant_operation_id->grant_operations:RESTRICT",
                    "product_id->shop_products:RESTRICT",
                    "reservation_id->economy_reservations:RESTRICT"
                },
                ["teleport_cooldowns"] = new[] { "last_operation_id->teleport_operations:RESTRICT" },
                ["teleport_operations"] = new[] { "reservation_id->economy_reservations:RESTRICT" },
                ["vote_ballots"] = new[]
                {
                    "crossplatform_id->vote_eligible_players:CASCADE",
                    "round_id->vote_eligible_players:CASCADE"
                },
                ["vote_eligible_players"] = new[] { "round_id->vote_rounds:CASCADE" },
                ["vote_rounds"] = new[]
                {
                    "action_job_id->jobs:RESTRICT",
                    "configuration_id->vote_configurations:RESTRICT"
                }
            };

            var actual = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var table in ExpectedTables)
            {
                var keys = connection.Query<ForeignKeyRow>("PRAGMA foreign_key_list([" + table + "]);" )
                    .Select(key => key.from + "->" + key.table + ":" + key.on_delete)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                if (keys.Length > 0)
                    actual[table] = keys;
            }

            Assert.Equal(expected.Keys.OrderBy(value => value), actual.Keys.OrderBy(value => value));
            foreach (var pair in expected)
            {
                Assert.Equal(
                    pair.Value.OrderBy(value => value, StringComparer.Ordinal),
                    actual[pair.Key]);
            }
        }

        private static void AssertTypedColumnsAndChecks(SqliteConnection connection)
        {
            var rewardColumns = connection.Query<TableColumn>(
                    "PRAGMA table_info([reward_package_entries]);")
                .Select(column => column.name)
                .ToArray();
            Assert.Contains("entry_kind", rewardColumns);
            Assert.Contains("item_internal_name", rewardColumns);
            Assert.Contains("currency_amount", rewardColumns);
            Assert.Contains("registered_action", rewardColumns);
            Assert.DoesNotContain(rewardColumns, column =>
                column.IndexOf("payload", StringComparison.OrdinalIgnoreCase) >= 0 ||
                column.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0);

            var codeColumns = connection.Query<TableColumn>("PRAGMA table_info([redeem_codes]);")
                .Select(column => column.name)
                .ToArray();
            Assert.Contains("normalized_code_digest", codeColumns);
            Assert.Contains("masked_prefix", codeColumns);
            Assert.Contains("normalization_version", codeColumns);
            Assert.DoesNotContain(codeColumns, column =>
                string.Equals(column, "code", StringComparison.OrdinalIgnoreCase) ||
                column.IndexOf("plaintext", StringComparison.OrdinalIgnoreCase) >= 0 ||
                column.IndexOf("raw_code", StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var table in new[] { "player_homes", "cities", "player_return_points" })
            {
                var columns = connection.Query<TableColumn>("PRAGMA table_info([" + table + "]);" )
                    .Select(column => column.name)
                    .ToArray();
                Assert.Contains("world_id", columns);
                Assert.Contains("x", columns);
                Assert.Contains("y", columns);
                Assert.Contains("z", columns);
                Assert.Contains("yaw", columns);
            }

            var utcColumns = ExpectedTables
                .SelectMany(table => connection.Query<TableColumn>("PRAGMA table_info([" + table + "]);"))
                .Where(column => column.name.EndsWith("_utc", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(utcColumns);
            Assert.All(utcColumns, column => Assert.Equal("INTEGER", column.type));

            foreach (var table in ExpectedTables)
            {
                var createSql = connection.ExecuteScalar<string>(
                    "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @Name;",
                    new { Name = table });
                Assert.Contains("CHECK", createSql, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void SeedRewardPackage(SqliteConnection connection) => connection.Execute(
            @"INSERT INTO reward_packages (
                  package_id, name, description, enabled, sort_order,
                  created_at_utc, updated_at_utc, row_version)
              VALUES ('package-1', 'Package', 'Safe summary', 1, 0, 1, 1, 0);");

        private static void SeedAuditSources(SqliteConnection connection)
        {
            SeedRewardPackage(connection);
            connection.Execute(
                @"INSERT INTO reward_package_entries (
                      entry_id, package_id, ordinal, entry_kind, item_internal_name,
                      item_kind, quantity, min_quality, max_quality, catalog_version)
                  VALUES ('entry-1', 'package-1', 0, 'Item', 'SECRETITEM',
                      'Item', 1, 1, 1, 'catalog-v1');
                  INSERT INTO economy_transactions (
                      transaction_id, transaction_type, idempotency_key, occurred_utc,
                      actor_kind, actor_id, related_crossplatform_id, status,
                      correlation_id)
                  VALUES ('transaction-1', 'Adjustment', 'transaction-1', 100,
                      'Owner', 'owner', 'EOS-1', 'Committed', 'wave4-economy');
                  INSERT INTO grant_operations (
                      operation_id, package_id, crossplatform_id, expected_entity_id,
                      expected_world_id, state, idempotency_key, actor_kind, actor_id,
                      correlation_id, created_at_utc, updated_at_utc, row_version)
                  VALUES ('grant-1', 'package-1', 'EOS-1', 1, 'world-1', 'Completed',
                      'grant-1', 'Owner', 'owner', 'wave4-grant', 101, 102, 1);
                  INSERT INTO shop_products (
                      product_id, name, description, enabled, price_amount,
                      reward_package_id, sort_order, created_at_utc, updated_at_utc, row_version)
                  VALUES ('product-1', 'Product', 'Safe', 1, 10, 'package-1', 0, 100, 100, 0);
                  INSERT INTO shop_purchases (
                      purchase_id, product_id, crossplatform_id, quantity, unit_price,
                      total_amount, state, idempotency_key, grant_operation_id,
                      correlation_id, created_at_utc, updated_at_utc, row_version)
                  VALUES ('purchase-1', 'product-1', 'EOS-1', 1, 10, 10, 'Completed',
                      'purchase-1', 'grant-1', 'wave4-purchase', 103, 104, 1);
                  INSERT INTO redeem_codes (
                      code_id, normalized_code_digest, masked_prefix, normalization_version,
                      reward_package_id, enabled, redemption_count,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES ('code-1', @Digest, 'ABCD', 1, 'package-1', 1, 1, 100, 104, 1);
                  INSERT INTO redeem_attempts (
                      attempt_id, code_id, crossplatform_id, normalized_code_digest,
                      result, grant_operation_id, correlation_id, attempted_at_utc)
                  VALUES ('redeem-1', 'code-1', 'EOS-1', @Digest, 'Succeeded',
                      'grant-1', 'wave4-redeem', 105);
                  INSERT INTO reward_eligibilities (
                      eligibility_id, rule_kind, rule_id, crossplatform_id, eligibility_key,
                      state, grant_operation_id, correlation_id,
                      created_at_utc, updated_at_utc, row_version)
                  VALUES ('eligibility-1', 'Achievement', 'achievement-1', 'EOS-1', 'level-10',
                      'Granted', 'grant-1', 'wave4-reward', 106, 106, 1);
                  INSERT INTO teleport_operations (
                      operation_id, teleport_kind, crossplatform_id, expected_entity_id,
                      expected_world_id, destination_world_id, destination_x, destination_y,
                      destination_z, destination_yaw, state, idempotency_key, actor_kind,
                      actor_id, correlation_id, created_at_utc, updated_at_utc, row_version)
                  VALUES ('teleport-1', 'Home', 'EOS-1', 1, 'world-1', 'world-1',
                      123.125, 64, -4, 90, 'Completed', 'teleport-1', 'Player', 'EOS-1',
                      'wave4-teleport', 107, 108, 1);
                  INSERT INTO vote_rounds (
                      round_id, configuration_id, vote_kind, state,
                      initiator_crossplatform_id, target_crossplatform_id, scope_key,
                      eligible_count, threshold_percent, minimum_participants,
                      allow_vote_change, idempotency_key, correlation_id,
                      opened_at_utc, expires_at_utc, settled_at_utc, row_version)
                  VALUES ('vote-1', 'configuration-kick', 'Kick', 'Passed', 'EOS-1', 'EOS-2',
                      'global', 2, 60, 2, 0, 'vote-1', 'wave4-vote', 109, 169, 110, 1);",
                new { Digest = new string('a', 64) });
        }

        private static void UpgradeThrough010(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName =>
                        resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) &&
                        Enumerable.Range(1, 10).Any(version => resourceName.IndexOf(
                            $".Migrations.{version:D3}_",
                            StringComparison.OrdinalIgnoreCase) >= 0))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();
            Assert.True(result.Successful, result.Error?.ToString());
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-economy-community-tests", Guid.NewGuid().ToString("N"));

            public TemporaryDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Persistence")]

        private sealed class ForeignKeyViolation
        {
            public string table { get; set; } = string.Empty;
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Persistence")]

        private sealed class ForeignKeyRow
        {
            public string table { get; set; } = string.Empty;
            public string from { get; set; } = string.Empty;
            public string on_delete { get; set; } = string.Empty;
        }

        [Trait("Capability", "Economy")]

        [Trait("Boundary", "Persistence")]

        private sealed class TableColumn
        {
            public string name { get; set; } = string.Empty;
            public string type { get; set; } = string.Empty;
        }

        [Trait("Capability", "Economy")]

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
