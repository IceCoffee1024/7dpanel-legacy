using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Application
{
    public interface IServerOperationStore
    {
        void CreateQueued(ServerOperationSnapshot operation);

        ServerOperationSnapshot? Get(string operationId);

        IReadOnlyList<ServerOperationSnapshot> ListRunning();

        bool TryTransition(
            string operationId,
            ServerOperationLifecycleStatus expectedStatus,
            ServerOperationLifecycleStatus nextStatus,
            DateTimeOffset changedAtUtc,
            string? failureCode);

        bool TrySetAuditStatus(
            string operationId,
            ServerOperationLifecycleStatus expectedStatus,
            string auditStatus);
    }
}
