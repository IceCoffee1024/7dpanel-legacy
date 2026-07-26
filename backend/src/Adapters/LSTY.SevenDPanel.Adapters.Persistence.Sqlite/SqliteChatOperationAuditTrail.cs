using System;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteChatOperationAuditTrail : IChatOperationAuditTrail
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteChatOperationAuditTrail(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Record(ChatOperationAuditEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO chat_operation_audit (
                      actor_subject, operation, occurred_utc, result, channel,
                      target_crossplatform_id, message_length, business_key, changed_fields)
                  VALUES (
                      @ActorSubject, @Operation, @OccurredUtc, @Result, @Channel,
                      @TargetCrossplatformId, @MessageLength, @BusinessKey, @ChangedFields);",
                new
                {
                    entry.ActorSubject,
                    Operation = entry.Operation.ToString(),
                    OccurredUtc = entry.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    entry.Result,
                    Channel = entry.Channel?.ToString(),
                    entry.TargetCrossplatformId,
                    entry.MessageLength,
                    entry.BusinessKey,
                    ChangedFields = string.Join(",", entry.ChangedFields.OrderBy(value => value, StringComparer.Ordinal))
                });
        }
    }
}
