using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IServerOperationAuditTrail
    {
        void CreatePending(ServerOperationAuditIntent intent);

        bool TryMarkStarted(string operationId, DateTimeOffset updatedAtUtc);

        bool TryMarkFailed(ServerOperationAuditFailure failure);
    }
}
