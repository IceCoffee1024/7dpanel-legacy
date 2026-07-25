using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetHistoricalPlayersUseCase
    {
        private readonly IPlayerHistoryStore store;

        public GetHistoricalPlayersUseCase(IPlayerHistoryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public HistoricalPlayersPage Execute(HistoricalPlayersQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return store.GetPlayers(query);
        }
    }
}
