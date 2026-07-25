using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IHostOverviewQuery
    {
        Task<HostOverviewSnapshot> GetHostOverviewAsync(CancellationToken cancellationToken);
    }
}
