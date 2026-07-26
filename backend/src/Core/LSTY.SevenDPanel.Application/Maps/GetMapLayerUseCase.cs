using System;

namespace LSTY.SevenDPanel.Application
{
    public interface IMapLayerProjection
    {
        MapLayerProjectionSnapshot Query(MapLayerQuery query);
    }

    public sealed class GetMapLayerUseCase
    {
        private readonly IMapLayerProjection projection;

        public GetMapLayerUseCase(IMapLayerProjection projection)
        {
            this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        }

        public MapLayerProjectionSnapshot Execute(MapLayerQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return projection.Query(query);
        }
    }
}
