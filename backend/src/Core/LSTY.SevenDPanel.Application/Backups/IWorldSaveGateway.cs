using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application.Backups
{
    public interface IWorldSaveGateway
    {
        Task SaveCurrentWorldAsync(CancellationToken cancellationToken);
    }
}
