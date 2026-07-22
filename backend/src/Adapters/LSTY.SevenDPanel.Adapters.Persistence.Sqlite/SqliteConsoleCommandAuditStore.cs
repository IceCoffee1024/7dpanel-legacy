using System;
using Dapper;
using LSTY.SevenDPanel.Application.ConsoleCommands;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteConsoleCommandAuditStore : IConsoleCommandAuditStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteConsoleCommandAuditStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Append(ConsoleCommandAuditEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO console_command_audit (
                      audit_id, raw_command, command_name, source, actor_subject,
                      started_utc, completed_utc, completion_kind, exception_type)
                  VALUES (
                      @AuditId, @RawCommand, @CommandName, @Source, @ActorSubject,
                      @StartedUtc, @CompletedUtc, @CompletionKind, @ExceptionType);",
                new
                {
                    entry.AuditId,
                    entry.RawCommand,
                    CommandName = entry.Tokens.Count == 0 ? null : entry.Tokens[0],
                    entry.Source,
                    entry.ActorSubject,
                    StartedUtc = entry.StartedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds(),
                    CompletedUtc = entry.CompletedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds(),
                    CompletionKind = entry.CompletionKind.ToString(),
                    entry.ExceptionType
                },
                transaction);

            for (var ordinal = 1; ordinal < entry.Tokens.Count; ordinal++)
            {
                connection.Execute(
                    @"INSERT INTO console_command_audit_argument (audit_id, ordinal, value)
                      VALUES (@AuditId, @Ordinal, @Value);",
                    new
                    {
                        entry.AuditId,
                        Ordinal = ordinal - 1,
                        Value = entry.Tokens[ordinal]
                    },
                    transaction);
            }

            for (var ordinal = 0; ordinal < entry.Output.Count; ordinal++)
            {
                connection.Execute(
                    @"INSERT INTO console_command_audit_output (audit_id, ordinal, value)
                      VALUES (@AuditId, @Ordinal, @Value);",
                    new
                    {
                        entry.AuditId,
                        Ordinal = ordinal,
                        Value = entry.Output[ordinal]
                    },
                    transaction);
            }

            transaction.Commit();
        }

        public void AppendGap(ConsoleCommandAuditGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT OR IGNORE INTO console_command_audit_gap (
                      gap_id, started_utc, completed_utc, dropped_count, reason)
                  VALUES (@GapId, @StartedUtc, @CompletedUtc, @DroppedCount, @Reason);",
                new
                {
                    gap.GapId,
                    StartedUtc = gap.StartedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds(),
                    CompletedUtc = gap.CompletedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds(),
                    gap.DroppedCount,
                    gap.Reason
                },
                transaction);
            transaction.Commit();
        }
    }
}