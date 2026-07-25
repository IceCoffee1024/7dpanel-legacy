using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetPlayerHistorySnapshotsUseCase
    {
        private readonly IPlayerHistoryStore store;

        public GetPlayerHistorySnapshotsUseCase(IPlayerHistoryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public PlayerHistorySnapshotsPage Execute(PlayerHistorySnapshotsQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return store.GetSnapshots(query);
        }
    }
}
