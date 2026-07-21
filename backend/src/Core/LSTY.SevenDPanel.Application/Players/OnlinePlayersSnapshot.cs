using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OnlinePlayersSnapshot
    {
        public OnlinePlayersSnapshot(DateTimeOffset capturedAtUtc, IEnumerable<PlayerSnapshot> players)
        {
            CapturedAtUtc = capturedAtUtc;
            Players = Array.AsReadOnly((players ?? Array.Empty<PlayerSnapshot>()).ToArray());
        }

        public DateTimeOffset CapturedAtUtc { get; }

        public IReadOnlyList<PlayerSnapshot> Players { get; }
    }
}
