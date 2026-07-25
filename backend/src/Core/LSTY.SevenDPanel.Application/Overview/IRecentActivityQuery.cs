using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IRecentActivityQuery
    {
        Task<RecentActivitySnapshot> GetRecentActivityAsync(CancellationToken cancellationToken);
    }
}
