using System;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum ServerOperationLifecycleStatus
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled,
        ResultUnknown
    }

    public sealed class ServerOperationSnapshot
    {
        public ServerOperationSnapshot(
            string operationId,
            string operationKind,
            ServerOperationLifecycleStatus status,
            string actorSubject,
            string originProcessInstanceId,
            DateTimeOffset requestedAtUtc,
            DateTimeOffset? startedAtUtc,
            DateTimeOffset? completedAtUtc,
            DateTimeOffset completionDeadlineUtc,
            string? failureCode,
            string auditStatus)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("An operation identifier is required.", nameof(operationId));
            if (!ServerOperationCodeContract.IsOperationCode(operationKind))
                throw new ArgumentException("The operation kind is not approved.", nameof(operationKind));
            if (string.IsNullOrWhiteSpace(actorSubject))
                throw new ArgumentException("An actor subject is required.", nameof(actorSubject));
            if (string.IsNullOrWhiteSpace(originProcessInstanceId))
                throw new ArgumentException("An origin process instance is required.", nameof(originProcessInstanceId));
            if (completionDeadlineUtc.Offset != TimeSpan.Zero || requestedAtUtc.Offset != TimeSpan.Zero ||
                (startedAtUtc.HasValue && startedAtUtc.Value.Offset != TimeSpan.Zero) ||
                (completedAtUtc.HasValue && completedAtUtc.Value.Offset != TimeSpan.Zero))
            {
                throw new ArgumentException("Server operation timestamps must be UTC.");
            }
            if (completionDeadlineUtc <= requestedAtUtc)
                throw new ArgumentException("The completion deadline must be after the request time.", nameof(completionDeadlineUtc));
            if (auditStatus != "recorded" && auditStatus != "audit_degraded")
                throw new ArgumentException("The audit status is not approved.", nameof(auditStatus));

            OperationId = operationId;
            OperationKind = operationKind;
            Status = status;
            ActorSubject = actorSubject;
            OriginProcessInstanceId = originProcessInstanceId;
            RequestedAtUtc = requestedAtUtc;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            CompletionDeadlineUtc = completionDeadlineUtc;
            FailureCode = failureCode;
            AuditStatus = auditStatus;
            ValidateLifecycle();
        }

        public string OperationId { get; }
        public string OperationKind { get; }
        public ServerOperationLifecycleStatus Status { get; }
        public string ActorSubject { get; }
        public string OriginProcessInstanceId { get; }
        public DateTimeOffset RequestedAtUtc { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public DateTimeOffset CompletionDeadlineUtc { get; }
        public string? FailureCode { get; }
        public string AuditStatus { get; }

        public bool IsTerminal => Status == ServerOperationLifecycleStatus.Succeeded ||
            Status == ServerOperationLifecycleStatus.Failed ||
            Status == ServerOperationLifecycleStatus.Cancelled ||
            Status == ServerOperationLifecycleStatus.ResultUnknown;

        public static bool IsLegalTransition(
            ServerOperationLifecycleStatus current,
            ServerOperationLifecycleStatus next)
        {
            return (current == ServerOperationLifecycleStatus.Queued && next == ServerOperationLifecycleStatus.Running) ||
                (current == ServerOperationLifecycleStatus.Running &&
                    (next == ServerOperationLifecycleStatus.Succeeded ||
                     next == ServerOperationLifecycleStatus.Failed ||
                     next == ServerOperationLifecycleStatus.Cancelled ||
                     next == ServerOperationLifecycleStatus.ResultUnknown));
        }

        public static string ToWireStatus(ServerOperationLifecycleStatus status)
        {
            switch (status)
            {
                case ServerOperationLifecycleStatus.Queued: return "queued";
                case ServerOperationLifecycleStatus.Running: return "running";
                case ServerOperationLifecycleStatus.Succeeded: return "succeeded";
                case ServerOperationLifecycleStatus.Failed: return "failed";
                case ServerOperationLifecycleStatus.Cancelled: return "cancelled";
                case ServerOperationLifecycleStatus.ResultUnknown: return "result-unknown";
                default: throw new ArgumentOutOfRangeException(nameof(status));
            }
        }

        public static bool TryParseWireStatus(string? value, out ServerOperationLifecycleStatus status)
        {
            switch (value)
            {
                case "queued": status = ServerOperationLifecycleStatus.Queued; return true;
                case "running": status = ServerOperationLifecycleStatus.Running; return true;
                case "succeeded": status = ServerOperationLifecycleStatus.Succeeded; return true;
                case "failed": status = ServerOperationLifecycleStatus.Failed; return true;
                case "cancelled": status = ServerOperationLifecycleStatus.Cancelled; return true;
                case "result-unknown": status = ServerOperationLifecycleStatus.ResultUnknown; return true;
                default: status = default; return false;
            }
        }

        private void ValidateLifecycle()
        {
            if (Status == ServerOperationLifecycleStatus.Queued &&
                StartedAtUtc == null && CompletedAtUtc == null && FailureCode == null)
            {
                return;
            }
            if (Status == ServerOperationLifecycleStatus.Running &&
                StartedAtUtc.HasValue && CompletedAtUtc == null && FailureCode == null)
            {
                return;
            }
            if (Status == ServerOperationLifecycleStatus.Succeeded &&
                StartedAtUtc.HasValue && CompletedAtUtc.HasValue && FailureCode == null)
            {
                return;
            }
            if ((Status == ServerOperationLifecycleStatus.Failed ||
                 Status == ServerOperationLifecycleStatus.Cancelled ||
                 Status == ServerOperationLifecycleStatus.ResultUnknown) &&
                StartedAtUtc.HasValue && CompletedAtUtc.HasValue && !string.IsNullOrWhiteSpace(FailureCode))
            {
                return;
            }
            throw new ArgumentException("The server operation lifecycle fields are inconsistent.");
        }
    }

    public sealed class ServerOperationProcessInstance
    {
        public ServerOperationProcessInstance()
            : this(Guid.NewGuid().ToString("N"))
        {
        }

        internal ServerOperationProcessInstance(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A process instance identifier is required.", nameof(value));
            Value = value;
        }

        public string Value { get; }
    }

    internal sealed class InMemoryServerOperationStore : IServerOperationStore
    {
        private readonly object sync = new object();
        private readonly System.Collections.Generic.Dictionary<string, ServerOperationSnapshot> operations =
            new System.Collections.Generic.Dictionary<string, ServerOperationSnapshot>(StringComparer.Ordinal);

        public void CreateQueued(ServerOperationSnapshot operation)
        {
            lock (sync)
            {
                if (operations.ContainsKey(operation.OperationId))
                    throw new InvalidOperationException("server_operation_already_exists");
                operations.Add(operation.OperationId, operation);
            }
        }

        public ServerOperationSnapshot? Get(string operationId)
        {
            lock (sync) return operations.TryGetValue(operationId, out var operation) ? operation : null;
        }

        public System.Collections.Generic.IReadOnlyList<ServerOperationSnapshot> ListRunning()
        {
            lock (sync)
            {
                return operations.Values
                    .Where(operation => operation.Status == ServerOperationLifecycleStatus.Running)
                    .ToArray();
            }
        }

        public bool TryTransition(string operationId, ServerOperationLifecycleStatus expectedStatus,
            ServerOperationLifecycleStatus nextStatus, DateTimeOffset changedAtUtc, string? failureCode)
        {
            lock (sync)
            {
                if (!operations.TryGetValue(operationId, out var operation) || operation.Status != expectedStatus ||
                    !ServerOperationSnapshot.IsLegalTransition(expectedStatus, nextStatus))
                {
                    return false;
                }
                operations[operationId] = new ServerOperationSnapshot(
                    operation.OperationId, operation.OperationKind, nextStatus, operation.ActorSubject,
                    operation.OriginProcessInstanceId, operation.RequestedAtUtc,
                    nextStatus == ServerOperationLifecycleStatus.Running ? changedAtUtc : operation.StartedAtUtc,
                    nextStatus == ServerOperationLifecycleStatus.Running ? null : changedAtUtc,
                    operation.CompletionDeadlineUtc, failureCode, operation.AuditStatus);
                return true;
            }
        }

        public bool TrySetAuditStatus(string operationId, ServerOperationLifecycleStatus expectedStatus, string auditStatus)
        {
            lock (sync)
            {
                if (!operations.TryGetValue(operationId, out var operation) || operation.Status != expectedStatus)
                    return false;
                operations[operationId] = new ServerOperationSnapshot(
                    operation.OperationId, operation.OperationKind, operation.Status, operation.ActorSubject,
                    operation.OriginProcessInstanceId, operation.RequestedAtUtc, operation.StartedAtUtc,
                    operation.CompletedAtUtc, operation.CompletionDeadlineUtc, operation.FailureCode, auditStatus);
                return true;
            }
        }
    }
}
