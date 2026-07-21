using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetOnlinePlayersUseCase
    {
        private readonly IOnlinePlayerQuery query;

        public GetOnlinePlayersUseCase(IOnlinePlayerQuery query)
        {
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public Task<OnlinePlayersSnapshot> ExecuteAsync(CancellationToken cancellationToken)
        {
            return query.GetOnlineAsync(cancellationToken);
        }
    }
}
