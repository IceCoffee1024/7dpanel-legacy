using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IPlayerHistoryStore
    {
        void Append(PlayerSnapshot snapshot);

        void AppendGap(PlayerHistoryGap gap);

        HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query);

        HistoricalPlayerDetails? GetPlayer(string crossplatformId);

        PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query);

        int Compact(DateTimeOffset utcNow, int maximumDeletes);
    }
}
