using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class QueryWorldUseCase
    {
        private readonly IWorldSnapshotProjection projection;

        public QueryWorldUseCase(IWorldSnapshotProjection projection)
        {
            this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        }

        public WorldSnapshot Execute() => projection.Query();
    }

    public sealed class QueryWorldToolCatalogUseCase
    {
        private readonly IWorldToolCatalog catalog;

        public QueryWorldToolCatalogUseCase(IWorldToolCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public WorldToolCatalogSnapshot Execute() => catalog.Read();
    }
}
