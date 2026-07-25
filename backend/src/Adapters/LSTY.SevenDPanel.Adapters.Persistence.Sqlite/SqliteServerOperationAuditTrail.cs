using System;
using System.Globalization;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteServerOperationAuditTrail : IServerOperationAuditTrail
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteServerOperationAuditTrail(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void CreateRestartPending(string operationId, string actorSubject, DateTimeOffset requestedAtUtc)
        {
            CreatePending(new ServerOperationAuditIntent(
                operationId,
                ServerOperationCodeContract.RestartScript,
                actorSubject,
                requestedAtUtc));
        }

        public void CreatePending(ServerOperationAuditIntent intent)
        {
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            RequireIdentifier(intent.OperationId, nameof(intent.OperationId));
            RequireIdentifier(intent.ActorSubject, nameof(intent.ActorSubject));
            if (!ServerOperationCodeContract.IsOperationCode(intent.OperationCode))
                throw new ArgumentException("The operation code is not approved for audit storage.", nameof(intent));

            var requestedUtc = ToUtcText(intent.RequestedAtUtc);
            var operationType = intent.OperationCode == ServerOperationCodeContract.RestartScript
                ? "restart"
                : "shutdown";

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT INTO server_operation_audit (
                      operation_id, operation_type, actor_subject, status, requested_utc, updated_utc, failure_code)
                  VALUES (@OperationId, @OperationType, @ActorSubject, 'Pending', @RequestedUtc, @RequestedUtc, NULL);",
                new
                {
                    intent.OperationId,
                    OperationType = operationType,
                    intent.ActorSubject,
                    RequestedUtc = requestedUtc
                });
        }

        public bool TryMarkStarted(string operationId, DateTimeOffset updatedAtUtc)
        {
            RequireIdentifier(operationId, nameof(operationId));
            return UpdateStatus(operationId, updatedAtUtc, "Started", null, "Pending");
        }

        public bool TryMarkFailed(string operationId, DateTimeOffset updatedAtUtc, string failureCode)
        {
            return TryMarkFailed(new ServerOperationAuditFailure(
                operationId,
                updatedAtUtc,
                failureCode));
        }

        public bool TryMarkFailed(ServerOperationAuditFailure failure)
        {
            if (failure == null) throw new ArgumentNullException(nameof(failure));
            RequireIdentifier(failure.OperationId, nameof(failure.OperationId));
            if (!ServerOperationCodeContract.IsFailureCode(failure.FailureCode))
            {
                throw new ArgumentException("The failure code is not approved for audit storage.", nameof(failure));
            }

            return UpdateStatus(
                failure.OperationId,
                failure.UpdatedAtUtc,
                "Failed",
                failure.FailureCode,
                "Pending");
        }

        private bool UpdateStatus(
            string operationId,
            DateTimeOffset updatedAtUtc,
            string status,
            string? failureCode,
            params string[] expectedStatuses)
        {
            using var connection = connectionFactory.Open();
            var affected = connection.Execute(
                @"UPDATE server_operation_audit
                  SET status = @Status,
                      updated_utc = @UpdatedUtc,
                      failure_code = @FailureCode
                  WHERE operation_id = @OperationId
                    AND status IN @ExpectedStatuses;",
                new
                {
                    OperationId = operationId,
                    Status = status,
                    UpdatedUtc = ToUtcText(updatedAtUtc),
                    FailureCode = failureCode,
                    ExpectedStatuses = expectedStatuses
                });
            return affected == 1;
        }

        private static void RequireIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            {
                throw new ArgumentException("A non-empty identifier of at most 128 characters is required.", parameterName);
            }
        }

        private static string ToUtcText(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture);
        }
    }
}
