using System;
using Dapper;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteGameChatCommandAuditTrail : IGameChatCommandAuditTrail
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteGameChatCommandAuditTrail(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Record(GameChatCommandAuditEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO chat_operation_audit (
                      actor_subject, operation, occurred_utc, result, channel,
                      target_crossplatform_id, message_length, business_key, changed_fields)
                  VALUES (
                      @ActorSubject, 'ExecuteGameCommand', @OccurredUtc, @Result,
                      'Global', NULL, NULL, @CommandName, @InvokedName);",
                new
                {
                    entry.ActorSubject,
                    OccurredUtc = entry.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    Result = entry.IsHandled ? entry.ResultCode : "unhandled",
                    entry.CommandName,
                    entry.InvokedName
                });
        }
    }
}
