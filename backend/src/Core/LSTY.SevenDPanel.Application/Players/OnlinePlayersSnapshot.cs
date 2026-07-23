using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class OnlinePlayersSnapshot
    {
        public OnlinePlayersSnapshot(IEnumerable<PlayerSnapshot> players)
        {
            Players = new ReadOnlyCollection<PlayerSnapshot>(
                (players ?? Enumerable.Empty<PlayerSnapshot>()).ToArray());
        }

        public IReadOnlyList<PlayerSnapshot> Players { get; }
    }
}
