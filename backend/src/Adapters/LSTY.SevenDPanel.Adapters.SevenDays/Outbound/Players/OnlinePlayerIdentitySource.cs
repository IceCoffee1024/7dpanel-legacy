using System;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    internal sealed class OnlinePlayerIdentitySource
    {
        public OnlinePlayerIdentitySource(int entityId, string combinedId)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(combinedId))
                throw new ArgumentException("A platform identity is required.", nameof(combinedId));

            EntityId = entityId;
            CombinedId = combinedId;
        }

        public int EntityId { get; }

        public string CombinedId { get; }
    }
}