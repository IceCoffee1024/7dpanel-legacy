using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IOnlinePlayerQuery
    {
        Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken);
    }
}
