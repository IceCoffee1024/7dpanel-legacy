using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    internal sealed class OnlinePlayerObservation
    {
        public OnlinePlayerObservation(PlayerSnapshot player, DateTimeOffset observedAtUtc)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            ObservedAtUtc = observedAtUtc;
        }

        public PlayerSnapshot Player { get; }

        public DateTimeOffset ObservedAtUtc { get; }
    }
}