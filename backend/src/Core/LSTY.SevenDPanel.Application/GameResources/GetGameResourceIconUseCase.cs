using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetGameResourceIconUseCase
    {
        private readonly IGameResourceCatalog catalog;

        public GetGameResourceIconUseCase(IGameResourceCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public Task<GameResourceIconReadResult> ExecuteAsync(
            string resourceId,
            GameResourceAccess access,
            CancellationToken cancellationToken)
        {
            if (resourceId == null) throw new ArgumentNullException(nameof(resourceId));
            if (!Enum.IsDefined(typeof(GameResourceAccess), access))
                throw new ArgumentOutOfRangeException(nameof(access));
            cancellationToken.ThrowIfCancellationRequested();

            var read = catalog.Read();
            if (read == null)
                throw new InvalidOperationException("The game resource catalog returned no read result.");
            if (read.Status != GameResourceCatalogReadStatus.Available)
                return Task.FromResult(GameResourceIconReadResult.Unavailable());

            var snapshot = read.Snapshot!;
            var resource = snapshot.Resources.FirstOrDefault(candidate =>
                string.Equals(candidate.ResourceId, resourceId, StringComparison.Ordinal));
            if (resource == null ||
                (access != GameResourceAccess.Owner &&
                 resource.Visibility != GameResourceVisibility.Public) ||
                resource.IconStatus != GameResourceIconStatus.Available)
            {
                return Task.FromResult(GameResourceIconReadResult.Missing());
            }

            return catalog.ReadIconAsync(
                snapshot.CatalogVersion,
                resource.ResourceId,
                cancellationToken);
        }
    }
}
