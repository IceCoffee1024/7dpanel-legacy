using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IShutdownServerGateway
    {
        Task RequestShutdownAsync(CancellationToken cancellationToken);
    }
}
