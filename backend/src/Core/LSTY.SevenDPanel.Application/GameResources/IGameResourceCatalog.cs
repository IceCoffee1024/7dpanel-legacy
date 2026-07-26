using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public interface IGameResourceCatalog
    {
        GameResourceCatalogReadResult Read();

        Task<GameResourceIconReadResult> ReadIconAsync(
            string catalogVersion,
            string resourceId,
            CancellationToken cancellationToken);
    }
}
