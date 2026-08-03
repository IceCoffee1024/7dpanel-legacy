using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DbUp;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Persistence")]
    public sealed class EvidenceFoundationMigrationTests
    {
        [Fact]
        public void Empty_database_upgrade_creates_the_evidence_foundation_schema()
        {
            using var database = new TemporaryDatabase();

            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                new[] { "chat_mute", "chat_mute_operation", "game_event_gaps", "game_events" },
                connection.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('game_events', 'game_event_gaps', 'chat_mute', 'chat_mute_operation') ORDER BY name;"));
            Assert.Equal(
                new[]
                {
                    "event_id", "event_type", "occurred_utc", "observed_utc",
                    "actor_crossplatform_id", "actor_platform_id", "actor_entity_id", "actor_name",
                    "target_crossplatform_id", "target_platform_id", "target_entity_id", "target_name",
                    "game_shutting_down"
                },
                Columns(connection, "game_events"));
            Assert.Equal(
                new[] { "gap_id", "reason", "started_utc", "ended_utc", "affected_count" },
                Columns(connection, "game_event_gaps"));
            Assert.Equal(
                new[]
                {
                    "crossplatform_id", "display_name", "reason", "muted_until_utc",
                    "created_by", "created_utc", "updated_by", "updated_utc"
                },
                Columns(connection, "chat_mute"));
            Assert.Equal(
                new[]
                {
                    "operation_id", "operation_kind", "target_crossplatform_id", "actor_subject",
                    "occurred_utc", "result", "correlation_id", "muted_until_utc", "reason"
                },
                Columns(connection, "chat_mute_operation"));
            Assert.Equal(
                new[]
                {
                    "ix_chat_mute_muted_until",
                    "ix_chat_mute_operation_occurred",
                    "ix_chat_mute_updated",
                    "ix_game_event_gaps_started",
                    "ix_game_events_actor_crossplatform",
                    "ix_game_events_occurred",
                    "ix_game_events_target_crossplatform",
                    "ix_game_events_type"
                },
                connection.Query<string>(
                    "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'ix_%' AND (name LIKE 'ix_game_event%' OR name LIKE 'ix_chat_mute%') ORDER BY name;"));
            Assert.Equal(
                new[]
                {
                    "source_kind", "source_id", "actor_subject", "target_ref", "action",
                    "occurred_utc", "status", "correlation_id", "has_details"
                },
                Columns(connection, "unified_audit_projection"));
        }

        [Fact]
        public void Upgrade_from_007_preserves_existing_dedicated_audit_data()
        {
            using var database = new TemporaryDatabase();
            UpgradeThrough007(database.ConnectionFactory);

            using (var connection = database.ConnectionFactory.Open())
            {
                InsertLegacyAuditData(connection);
                var before = ReadLegacySnapshot(connection);

                database.Upgrade();

                var after = ReadLegacySnapshot(connection);
                Assert.Equal(before, after);
                Assert.Equal(
                    1,
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';"));
            }
        }

        [Fact]
        public void Unified_audit_projection_is_stable_and_excludes_sensitive_details_and_gaps()
        {
            using var database = new TemporaryDatabase();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            InsertLegacyAuditData(connection);
            connection.Execute(
                @"INSERT INTO game_event_gaps (gap_id, reason, started_utc, ended_utc, affected_count)
                  VALUES ('gap-1', 'QueueFull', 1782460800000, NULL, 2);
                  INSERT INTO chat_mute_operation (
                      operation_id, operation_kind, target_crossplatform_id, actor_subject,
                      occurred_utc, result, correlation_id, muted_until_utc, reason)
                  VALUES ('mute-1', 'Create', 'EOS-1', 'owner', 1782460800000,
                      'Succeeded', 'corr-mute', NULL, 'Secret Token API Key /sensitive/path');",
                transaction: null);

            var viewSql = connection.ExecuteScalar<string>(
                "SELECT sql FROM sqlite_master WHERE type = 'view' AND name = 'unified_audit_projection';")!;
            var forbiddenViewTerms = new[]
            {
                "raw_command", "console_command_audit_argument", "console_command_audit_output",
                "reason", "changed_fields", "exception_type", "secret", "token", "api key", "path",
                "masked_ip", "canonical_ip"
            };
            Assert.DoesNotContain(forbiddenViewTerms, term => viewSql.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            var rows = connection.Query<ProjectionRow>(
                @"SELECT source_kind, source_id, actor_subject, target_ref, action,
                         occurred_utc, status, correlation_id, has_details
                  FROM unified_audit_projection
                  ORDER BY source_kind, source_id;").ToArray();
            Assert.Equal(
                new[] { "chatMuteOperation", "chatOperation", "consoleCommand", "playerAction", "serverOperation" },
                rows.Select(row => row.source_kind));
            Assert.All(rows, row => Assert.Equal(0, row.has_details));
            Assert.DoesNotContain(rows, row => row.source_kind == "gap");
            Assert.Equal(
                DateTimeOffset.Parse("2026-07-26T08:00:00.123Z").ToUnixTimeMilliseconds(),
                rows.Single(row => row.source_kind == "serverOperation").occurred_utc);

            var renderedRows = string.Join("\n", rows.Select(row => string.Join(
                "|",
                row.source_kind,
                row.source_id,
                row.actor_subject,
                row.target_ref,
                row.action,
                row.occurred_utc,
                row.status,
                row.correlation_id,
                row.has_details)));
            Assert.DoesNotContain("Secret", renderedRows, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token", renderedRows, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("API Key", renderedRows, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/sensitive/path", renderedRows, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw command body", renderedRows, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Columns(SqliteConnection connection, string name) =>
            connection.Query<SchemaColumn>("PRAGMA table_info(" + name + ");")
                .Select(column => column.name);

        private static void UpgradeThrough007(SqliteConnectionFactory connectionFactory)
        {
            var directory = Path.GetDirectoryName(connectionFactory.DatabasePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var result = DeployChanges.To
                .SqliteDatabase(connectionFactory.ConnectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(SqliteDatabaseBootstrapper).Assembly,
                    resourceName => new[]
                    {
                        ".001_Authentication.sql",
                        ".002_PlayerActionAudit.sql",
                        ".003_ConsoleCommandAudit.sql",
                        ".004_PlayerHistory.sql",
                        ".005_OverviewActivityAndServerOperations.sql",
                        ".006_PlayerMapSpatialQueries.sql",
                        ".007_GameChat.sql"
                    }.Any(suffix => resourceName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                .WithTransactionPerScript()
                .Build()
                .PerformUpgrade();

            Assert.True(result.Successful, result.Error?.ToString());
        }

        private static void InsertLegacyAuditData(SqliteConnection connection)
        {
            connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id, target_name,
                      target_platform_id, target_platform, reason, requested_utc, completed_utc,
                      status, failure_code)
                  VALUES ('player-1', 'kick', 'owner', 7, 'Alice', 'Steam-1', 'Steam',
                      'Secret Token API Key /sensitive/path', 1782460800000, 1782460800100,
                      'Succeeded', NULL);
                  INSERT INTO console_command_audit (
                      audit_id, raw_command, command_name, source, actor_subject, started_utc,
                      completed_utc, completion_kind, exception_type)
                  VALUES ('console-1', 'say raw command body Secret Token', 'say', 'panel', 'owner',
                      1782460800000, 1782460800100, 'Completed', NULL);
                  INSERT INTO console_command_audit_argument (audit_id, ordinal, value)
                  VALUES ('console-1', 0, 'Secret Token');
                  INSERT INTO console_command_audit_output (audit_id, ordinal, value)
                  VALUES ('console-1', 0, 'raw command body API Key');
                  INSERT INTO server_operation_audit (
                      operation_id, operation_type, actor_subject, status, requested_utc,
                      updated_utc, failure_code)
                  VALUES ('server-1', 'restart', 'owner', 'Failed',
                      '2026-07-26T08:00:00.123Z', '2026-07-26T08:00:01.123Z', 'Secret API Key');
                  INSERT INTO chat_operation_audit (
                      actor_subject, operation, occurred_utc, result, channel,
                      target_crossplatform_id, message_length, business_key, changed_fields)
                  VALUES ('owner', 'message_sent', 1782460800000, 'Succeeded', 'Global',
                      'EOS-1', 12, 'corr-chat', 'Secret changed fields /sensitive/path');");
        }

        private static LegacyAuditSnapshot ReadLegacySnapshot(SqliteConnection connection) =>
            new LegacyAuditSnapshot(
                connection.ExecuteScalar<string>(
                    "SELECT operation_id || '|' || action_type || '|' || actor_subject || '|' || reason FROM player_action_audit WHERE operation_id = 'player-1';")!,
                connection.ExecuteScalar<string>(
                    "SELECT audit_id || '|' || raw_command || '|' || command_name || '|' || completion_kind FROM console_command_audit WHERE audit_id = 'console-1';")!,
                connection.ExecuteScalar<string>(
                    "SELECT value FROM console_command_audit_argument WHERE audit_id = 'console-1' AND ordinal = 0;")!,
                connection.ExecuteScalar<string>(
                    "SELECT value FROM console_command_audit_output WHERE audit_id = 'console-1' AND ordinal = 0;")!,
                connection.ExecuteScalar<string>(
                    "SELECT operation_id || '|' || requested_utc || '|' || failure_code FROM server_operation_audit WHERE operation_id = 'server-1';")!,
                connection.ExecuteScalar<string>(
                    "SELECT actor_subject || '|' || operation || '|' || business_key || '|' || changed_fields FROM chat_operation_audit WHERE id = 1;")!);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Persistence")]

        private sealed class SchemaColumn
        {
            public string name { get; set; } = string.Empty;
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Persistence")]

        private sealed class LegacyAuditSnapshot : IEquatable<LegacyAuditSnapshot>
        {
            public LegacyAuditSnapshot(params string[] values) => Values = values;

            private string[] Values { get; }

            public bool Equals(LegacyAuditSnapshot? other) =>
                other != null && Values.SequenceEqual(other.Values, StringComparer.Ordinal);

            public override bool Equals(object? obj) => Equals(obj as LegacyAuditSnapshot);

            public override int GetHashCode() => Values.Aggregate(17, (hash, value) => hash * 31 + value.GetHashCode());
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Persistence")]

        private sealed class ProjectionRow
        {
            public string source_kind { get; set; } = string.Empty;
            public string source_id { get; set; } = string.Empty;
            public string? actor_subject { get; set; }
            public string? target_ref { get; set; }
            public string action { get; set; } = string.Empty;
            public long occurred_utc { get; set; }
            public string status { get; set; } = string.Empty;
            public string? correlation_id { get; set; }
            public int has_details { get; set; }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryDatabase()
            {
                directory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-evidence-foundation-tests",
                    Guid.NewGuid().ToString("N"));
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
