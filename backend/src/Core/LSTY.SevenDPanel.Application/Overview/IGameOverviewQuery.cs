using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IGameOverviewQuery
    {
        Task<GameOverviewSnapshot> GetGameOverviewAsync(CancellationToken cancellationToken);
    }
}
