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

        public long Begin(GameChatCommandAuditIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            using var connection = connectionFactory.Open();
            return connection.QuerySingle<long>(
                @"INSERT INTO chat_operation_audit (
                      actor_subject, operation, occurred_utc, result, channel,
                      target_crossplatform_id, message_length, business_key, changed_fields)
                  VALUES (
                      @ActorSubject, 'ExecuteGameCommand', @OccurredUtc, 'pending',
                      'Global', NULL, NULL, @CommandName, @InvokedName);
                  SELECT last_insert_rowid();",
                new
                {
                    intent.ActorSubject,
                    OccurredUtc = intent.OccurredAtUtc.ToUnixTimeMilliseconds(),
                    intent.CommandName,
                    intent.InvokedName
                });
        }

        public void Complete(long auditId, GameChatCommandAuditCompletion completion)
        {
            if (auditId <= 0) throw new ArgumentOutOfRangeException(nameof(auditId));
            if (completion == null) throw new ArgumentNullException(nameof(completion));
            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"UPDATE chat_operation_audit
                  SET result = @Result
                  WHERE id = @AuditId
                    AND operation = 'ExecuteGameCommand'
                    AND result = 'pending';",
                new
                {
                    AuditId = auditId,
                    Result = completion.IsHandled ? completion.ResultCode : "unhandled"
                });
            if (affected != 1)
                throw new InvalidOperationException("The pending game chat command audit was not completed.");
        }
    }
}
