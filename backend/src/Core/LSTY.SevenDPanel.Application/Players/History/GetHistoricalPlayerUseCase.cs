using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetHistoricalPlayerUseCase
    {
        private readonly IPlayerHistoryStore store;

        public GetHistoricalPlayerUseCase(IPlayerHistoryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public HistoricalPlayerDetails? Execute(string crossplatformId)
        {
            var identity = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            return store.GetPlayer(identity);
        }
    }
}
