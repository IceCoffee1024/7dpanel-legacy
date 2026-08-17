using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteServerOperationStore : IServerOperationStore
    {
        private const string SelectColumns = @"SELECT operation_id AS OperationId,
            operation_kind AS OperationKind, status AS Status, actor_subject AS ActorSubject,
            origin_process_instance_id AS OriginProcessInstanceId,
            requested_at_utc AS RequestedAtUtc, started_at_utc AS StartedAtUtc,
            completed_at_utc AS CompletedAtUtc, completion_deadline_utc AS CompletionDeadlineUtc,
            failure_code AS FailureCode, audit_status AS AuditStatus
            FROM server_operation_lifecycle";

        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteServerOperationStore(SqliteConnectionFactory connectionFactory) =>
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

        public void CreateQueued(ServerOperationSnapshot operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.Status != ServerOperationLifecycleStatus.Queued)
                throw new ArgumentException("Only queued server operations can be created.", nameof(operation));

            using var connection = connectionFactory.Open();
            connection.Execute(@"INSERT INTO server_operation_lifecycle (
                    operation_id, operation_kind, actor_subject, origin_process_instance_id,
                    status, requested_at_utc, started_at_utc, completed_at_utc,
                    completion_deadline_utc, failure_code, audit_status)
                VALUES (@OperationId, @OperationKind, @ActorSubject, @OriginProcessInstanceId,
                    'queued', @RequestedAtUtc, NULL, NULL, @CompletionDeadlineUtc, NULL, @AuditStatus);",
                new
                {
                    operation.OperationId,
                    operation.OperationKind,
                    operation.ActorSubject,
                    operation.OriginProcessInstanceId,
                    RequestedAtUtc = operation.RequestedAtUtc.ToUnixTimeMilliseconds(),
                    CompletionDeadlineUtc = operation.CompletionDeadlineUtc.ToUnixTimeMilliseconds(),
                    operation.AuditStatus
                });
        }

        public ServerOperationSnapshot? Get(string operationId)
        {
            RequireOperationId(operationId);
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<Row>(
                SelectColumns + " WHERE operation_id = @OperationId;",
                new { OperationId = operationId });
            return row == null ? null : ToSnapshot(row);
        }

        public IReadOnlyList<ServerOperationSnapshot> ListRunning()
        {
            using var connection = connectionFactory.Open();
            return connection.Query<Row>(
                    SelectColumns + " WHERE status = 'running' ORDER BY completion_deadline_utc, operation_id;")
                .Select(ToSnapshot)
                .ToArray();
        }

        public bool TryTransition(
            string operationId,
            ServerOperationLifecycleStatus expectedStatus,
            ServerOperationLifecycleStatus nextStatus,
            DateTimeOffset changedAtUtc,
            string? failureCode)
        {
            RequireOperationId(operationId);
            if (!ServerOperationSnapshot.IsLegalTransition(expectedStatus, nextStatus))
                return false;
            if (changedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("Server operation timestamps must be UTC.", nameof(changedAtUtc));
            var terminal = nextStatus != ServerOperationLifecycleStatus.Running;
            if (terminal == string.IsNullOrWhiteSpace(failureCode) && nextStatus != ServerOperationLifecycleStatus.Succeeded)
                throw new ArgumentException("Terminal failures require a failure code.", nameof(failureCode));
            if (nextStatus == ServerOperationLifecycleStatus.Succeeded && failureCode != null)
                throw new ArgumentException("Succeeded operations cannot have a failure code.", nameof(failureCode));

            using var connection = connectionFactory.Open();
            var affected = connection.Execute(@"UPDATE server_operation_lifecycle
                SET status = @NextStatus,
                    started_at_utc = CASE WHEN @NextStatus = 'running' THEN @ChangedAtUtc ELSE started_at_utc END,
                    completed_at_utc = CASE WHEN @IsTerminal = 1 THEN @ChangedAtUtc ELSE NULL END,
                    failure_code = @FailureCode,
                    row_version = row_version + 1
                WHERE operation_id = @OperationId AND status = @ExpectedStatus;",
                new
                {
                    OperationId = operationId,
                    ExpectedStatus = ServerOperationSnapshot.ToWireStatus(expectedStatus),
                    NextStatus = ServerOperationSnapshot.ToWireStatus(nextStatus),
                    ChangedAtUtc = changedAtUtc.ToUnixTimeMilliseconds(),
                    IsTerminal = terminal ? 1 : 0,
                    FailureCode = failureCode
                });
            return affected == 1;
        }

        public bool TrySetAuditStatus(
            string operationId,
            ServerOperationLifecycleStatus expectedStatus,
            string auditStatus)
        {
            RequireOperationId(operationId);
            if (auditStatus != "recorded" && auditStatus != "audit_degraded")
                throw new ArgumentException("The audit status is not approved.", nameof(auditStatus));
            using var connection = connectionFactory.Open();
            var affected = connection.Execute(@"UPDATE server_operation_lifecycle
                SET audit_status = @AuditStatus, row_version = row_version + 1
                WHERE operation_id = @OperationId AND status = @ExpectedStatus;",
                new
                {
                    OperationId = operationId,
                    ExpectedStatus = ServerOperationSnapshot.ToWireStatus(expectedStatus),
                    AuditStatus = auditStatus
                });
            return affected == 1;
        }

        private static ServerOperationSnapshot ToSnapshot(Row row)
        {
            if (!ServerOperationSnapshot.TryParseWireStatus(row.Status, out var status))
                throw new InvalidOperationException("server_operation_status_invalid");
            return new ServerOperationSnapshot(
                row.OperationId,
                row.OperationKind,
                status,
                row.ActorSubject,
                row.OriginProcessInstanceId,
                DateTimeOffset.FromUnixTimeMilliseconds(row.RequestedAtUtc),
                row.StartedAtUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.StartedAtUtc.Value) : null,
                row.CompletedAtUtc.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedAtUtc.Value) : null,
                DateTimeOffset.FromUnixTimeMilliseconds(row.CompletionDeadlineUtc),
                row.FailureCode,
                row.AuditStatus);
        }

        private static void RequireOperationId(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 128)
                throw new ArgumentException("A non-empty operation identifier of at most 128 characters is required.", nameof(operationId));
        }

        private sealed class Row
        {
            public string OperationId { get; set; } = string.Empty;
            public string OperationKind { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string ActorSubject { get; set; } = string.Empty;
            public string OriginProcessInstanceId { get; set; } = string.Empty;
            public long RequestedAtUtc { get; set; }
            public long? StartedAtUtc { get; set; }
            public long? CompletedAtUtc { get; set; }
            public long CompletionDeadlineUtc { get; set; }
            public string? FailureCode { get; set; }
            public string AuditStatus { get; set; } = string.Empty;
        }
    }
}
