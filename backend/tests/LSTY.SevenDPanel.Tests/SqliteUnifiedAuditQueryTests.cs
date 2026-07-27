using System;
using System.IO;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SqliteUnifiedAuditQueryTests
    {
        [Fact]
        public void Query_reads_only_the_stable_projection_with_descending_keyset_paging()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            Seed(database);
            var query = new SqliteUnifiedAuditQuery(database.ConnectionFactory);

            var first = query.Query(Filter(pageSize: 2));
            var second = query.Query(Filter(pageSize: 2, cursor: first.NextCursor));

            Assert.Equal(new[] { "server-1", "player-1" }, first.Entries.Select(entry => entry.SourceId));
            Assert.Equal(new[] { "console-2", "console-1" }, second.Entries.Select(entry => entry.SourceId));
            Assert.NotNull(first.NextCursor);
            Assert.NotNull(second.NextCursor);
            Assert.Empty(first.Entries.Select(entry => entry.SourceId).Intersect(second.Entries.Select(entry => entry.SourceId)));
            Assert.All(first.Entries.Concat(second.Entries), entry => Assert.False(entry.HasDetails));
            Assert.DoesNotContain(first.Entries.Concat(second.Entries), entry =>
                (entry.Action + "|" + entry.ActorSubject + "|" + entry.TargetRef + "|" + entry.CorrelationId)
                    .IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Query_applies_all_filters_as_parameters_and_returns_only_console_command_gaps()
        {
            using var database = new TemporaryAuditDatabase();
            database.Upgrade();
            Seed(database);
            var query = new SqliteUnifiedAuditQuery(database.ConnectionFactory);
            var atUtc = DateTimeOffset.Parse("2026-07-26T08:00:00Z");

            var filtered = query.Query(new UnifiedAuditFilter(
                20,
                atUtc,
                atUtc,
                "owner",
                null,
                "say",
                "consoleCommand",
                "Completed",
                null));
            var injection = query.Query(new UnifiedAuditFilter(
                20,
                null,
                null,
                null,
                null,
                "say' OR 1=1 --",
                null,
                null,
                null));

            var entry = Assert.Single(filtered.Entries);
            Assert.Equal("console-1", entry.SourceId);
            var gap = Assert.Single(filtered.Gaps);
            Assert.Equal("consoleCommand", gap.SourceKind);
            Assert.Equal(2, gap.AffectedCount);
            Assert.Empty(injection.Entries);
            Assert.Empty(injection.Gaps);
        }

        private static UnifiedAuditFilter Filter(int pageSize, UnifiedAuditCursor? cursor = null) =>
            new UnifiedAuditFilter(pageSize, null, null, null, null, null, null, null, cursor);

        private static void Seed(TemporaryAuditDatabase database)
        {
            using var connection = database.ConnectionFactory.Open();
            const long occurredUtc = 1785052800000;
            connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id, action_type, actor_subject, target_entity_id, target_name,
                      target_platform_id, target_platform, reason, requested_utc, completed_utc,
                      status, failure_code)
                  VALUES ('player-1', 'kick', 'owner', 7, 'Alice', 'EOS-1', 'EOS',
                      'Secret reason', @OccurredUtc, @OccurredUtc, 'Succeeded', NULL);
                  INSERT INTO console_command_audit (
                      audit_id, raw_command, command_name, source, actor_subject, started_utc,
                      completed_utc, completion_kind, exception_type)
                  VALUES ('console-1', 'say Secret Token', 'say', 'panel', 'owner', @OccurredUtc,
                      @OccurredUtc, 'Completed', NULL),
                         ('console-2', 'say Secret Body', 'say', 'panel', 'admin', @OccurredUtc,
                      @OccurredUtc, 'Completed', NULL);
                  INSERT INTO server_operation_audit (
                      operation_id, operation_type, actor_subject, status, requested_utc, updated_utc, failure_code)
                  VALUES ('server-1', 'restart', 'owner', 'Failed', '2026-07-26T08:00:00Z',
                      '2026-07-26T08:00:01Z', 'secret_failure');
                  INSERT INTO chat_operation_audit (
                      actor_subject, operation, occurred_utc, result, channel,
                      target_crossplatform_id, message_length, business_key, changed_fields)
                  VALUES ('owner', 'message_sent', @OccurredUtc, 'Succeeded', 'Global', 'EOS-1', 12,
                      'corr-chat', 'Secret changed fields');
                  INSERT INTO chat_mute_operation (
                      operation_id, operation_kind, target_crossplatform_id, actor_subject,
                      occurred_utc, result, correlation_id, muted_until_utc, reason)
                  VALUES ('mute-1', 'Create', 'EOS-1', 'owner', @OccurredUtc, 'Succeeded',
                      'corr-mute', NULL, 'Secret mute reason');
                  INSERT INTO console_command_audit_gap (
                      gap_id, started_utc, completed_utc, dropped_count, reason)
                  VALUES ('console-gap-1', @OccurredUtc, @OccurredUtc, 2, 'QueueFull');
                  INSERT INTO chat_history_gaps (
                      started_utc, ended_utc, dropped_message_count, reason)
                  VALUES (@OccurredUtc, @OccurredUtc, 3, 'QueueFull');",
                new { OccurredUtc = occurredUtc });
        }

        private sealed class TemporaryAuditDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(Path.GetTempPath(), "7dpanel-unified-audit-tests", Guid.NewGuid().ToString("N"));

            public TemporaryAuditDatabase() =>
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));

            public SqliteConnectionFactory ConnectionFactory { get; }
            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
