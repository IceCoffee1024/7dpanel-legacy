using System;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqlitePlayerActionAuditTrail : IPlayerActionAuditTrail
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqlitePlayerActionAuditTrail(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void CreatePending(PlayerActionAuditIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO player_action_audit (
                      operation_id,
                      action_type,
                      actor_subject,
                      target_entity_id,
                      target_name,
                      target_platform_id,
                      target_platform,
                      reason,
                      requested_utc,
                      completed_utc,
                      status,
                      failure_code)
                  VALUES (
                      @OperationId,
                      @ActionType,
                      @ActorSubject,
                      @TargetEntityId,
                      NULL,
                      @TargetPlatformId,
                      @TargetPlatform,
                      @Reason,
                      @RequestedUtc,
                      NULL,
                      'Pending',
                      NULL);",
                new
                {
                    intent.OperationId,
                    intent.ActionType,
                    intent.ActorSubject,
                    intent.TargetEntityId,
                    TargetPlatformId = intent.TargetPlatformIdentity.CombinedId,
                    TargetPlatform = intent.TargetPlatformIdentity.Platform,
                    intent.Reason,
                    RequestedUtc = intent.RequestedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds()
                });
        }

        public bool TryComplete(PlayerActionAuditCompletion completion)
        {
            if (completion == null) throw new ArgumentNullException(nameof(completion));

            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"UPDATE player_action_audit
                  SET target_name = COALESCE(@TargetName, target_name),
                      completed_utc = @CompletedUtc,
                      status = @Status,
                      failure_code = @FailureCode
                  WHERE operation_id = @OperationId
                    AND status = 'Pending';",
                new
                {
                    completion.TargetName,
                    CompletedUtc = completion.CompletedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds(),
                    Status = completion.Status.ToString(),
                    completion.FailureCode,
                    completion.OperationId
                });
            return affected == 1;
        }

        public int MarkPendingUnknown(DateTimeOffset completedAtUtc)
        {
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var affected = connection.Execute(
                @"UPDATE player_action_audit
                  SET completed_utc = @CompletedUtc,
                      status = 'Unknown',
                      failure_code = 'process_interrupted'
                  WHERE status = 'Pending';",
                new
                {
                    CompletedUtc = completedAtUtc.ToUniversalTime().ToUnixTimeMilliseconds()
                },
                transaction);
            transaction.Commit();
            return affected;
        }
    }
}