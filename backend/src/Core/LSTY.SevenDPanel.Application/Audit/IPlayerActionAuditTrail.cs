using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerActionAuditTrail
    {
        void CreatePending(PlayerActionAuditIntent intent);

        bool TryComplete(PlayerActionAuditCompletion completion);

        int MarkPendingUnknown(DateTimeOffset completedAtUtc);
    }
}