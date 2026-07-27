using System;
using Dapper;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations
{
    public sealed class SqliteWorldOperationJobBridge : IWorldOperationJobBridge
    {
        private readonly SqliteConnectionFactory connectionFactory;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly IWorldOperationStore store;

        public SqliteWorldOperationJobBridge(
            SqliteConnectionFactory connectionFactory,
            Func<DateTimeOffset> utcNow)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            store = new SqliteWorldOperationStore(connectionFactory);
        }

        public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            var operationId = Guid.NewGuid().ToString("D");
            var jobId = Guid.NewGuid();

            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            if (intent.Kind == WorldOperationKind.RenderFullMap &&
                connection.ExecuteScalar<int>(
                    @"SELECT COUNT(*)
                      FROM world_operations operation
                      INNER JOIN jobs job ON job.id = operation.job_id
                      WHERE operation.kind = 'RenderFullMap'
                        AND operation.world_id = @WorldId
                        AND job.status IN ('Queued', 'Running');",
                    new { intent.WorldId },
                    transaction) != 0)
            {
                throw new WorldOperationConflictException("full_map_render_already_active");
            }
            connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, actor_subject, source_schedule_id,
                      idempotency_key, correlation_id, created_at_utc, row_version)
                  VALUES (@JobId, 'WorldOperation', 'Queued', @ActorSubject, NULL,
                      @IdempotencyKey, @CorrelationId, @CreatedAtUtc, 0);",
                new
                {
                    JobId = jobId.ToString("D"),
                    intent.ActorSubject,
                    IdempotencyKey = "world-operation:" + intent.CorrelationId,
                    intent.CorrelationId,
                    CreatedAtUtc = intent.CreatedAtUtc.ToUnixTimeMilliseconds()
                },
                transaction);
            SqliteWorldOperationStore.InsertOperation(
                connection,
                transaction,
                operationId,
                jobId,
                intent);
            SqliteWorldOperationStore.InsertTarget(
                connection,
                transaction,
                operationId,
                intent.Kind,
                intent.Target);
            transaction.Commit();
            return new WorldOperationReceipt(
                operationId,
                jobId,
                WorldOperationStatus.Queued,
                intent.CorrelationId,
                intent.CreatedAtUtc);
        }

        public WorldOperationRecord Get(string operationId) => store.Get(operationId);

        public WorldOperationPage Query(WorldOperationQuery query) => store.Query(query);

        public bool RequestCancellation(string operationId, string actorSubject)
        {
            operationId = SqliteWorldOperationStore.RequireText(operationId, nameof(operationId));
            actorSubject = SqliteWorldOperationStore.RequireText(actorSubject, nameof(actorSubject));
            var now = utcNow();
            SqliteWorldOperationStore.RequireUtc(now, nameof(utcNow));
            using var connection = connectionFactory.Open();
            return connection.Execute(
                @"UPDATE jobs
                  SET status = 'Cancelled', completed_at_utc = @CompletedAtUtc,
                      worker_id = NULL, row_version = row_version + 1
                  WHERE status = 'Queued'
                    AND id = (
                        SELECT job_id FROM world_operations
                        WHERE operation_id = @OperationId
                          AND actor_subject = @ActorSubject);",
                new
                {
                    OperationId = operationId,
                    ActorSubject = actorSubject,
                    CompletedAtUtc = now.ToUnixTimeMilliseconds()
                }) == 1;
        }
    }
}
