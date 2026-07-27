using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/world")]
    public sealed class WorldController : ApiController
    {
        private readonly QueryWorldUseCase world;
        private readonly QueryWorldToolCatalogUseCase catalog;

        public WorldController(
            QueryWorldUseCase world,
            QueryWorldToolCatalogUseCase catalog)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        [HttpGet]
        [Route("summary")]
        [ResponseType(typeof(WorldSummaryHttpResponse))]
        public HttpResponseMessage GetSummary() => ReadWorld(
            snapshot => new WorldSummaryHttpResponse(snapshot.World));

        [HttpGet]
        [Route("land-claims")]
        [ResponseType(typeof(WorldCollectionHttpResponse<WorldLandClaimHttpResponse>))]
        public HttpResponseMessage GetLandClaims() => ReadWorld(snapshot =>
            new WorldCollectionHttpResponse<WorldLandClaimHttpResponse>(
                snapshot.LandClaims.SourceState,
                snapshot.LandClaims.ObservedAtUtc,
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(
                        snapshot.LandClaims.Items,
                        item => new WorldLandClaimHttpResponse(item)))));

        [HttpGet]
        [Route("vehicles")]
        [ResponseType(typeof(WorldCollectionHttpResponse<WorldVehicleHttpResponse>))]
        public HttpResponseMessage GetVehicles() => ReadWorld(snapshot =>
            new WorldCollectionHttpResponse<WorldVehicleHttpResponse>(
                snapshot.Vehicles.SourceState,
                snapshot.Vehicles.ObservedAtUtc,
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(
                        snapshot.Vehicles.Items,
                        item => new WorldVehicleHttpResponse(item)))));

        [HttpGet]
        [Route("drones")]
        [ResponseType(typeof(WorldCollectionHttpResponse<WorldDroneHttpResponse>))]
        public HttpResponseMessage GetDrones() => ReadWorld(snapshot =>
            new WorldCollectionHttpResponse<WorldDroneHttpResponse>(
                snapshot.Drones.SourceState,
                snapshot.Drones.ObservedAtUtc,
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(
                        snapshot.Drones.Items,
                        item => new WorldDroneHttpResponse(item)))));

        [HttpGet]
        [Route("containers")]
        [ResponseType(typeof(WorldCollectionHttpResponse<WorldContainerHttpResponse>))]
        public HttpResponseMessage GetContainers() => ReadWorld(snapshot =>
            new WorldCollectionHttpResponse<WorldContainerHttpResponse>(
                snapshot.Containers.SourceState,
                snapshot.Containers.ObservedAtUtc,
                System.Linq.Enumerable.ToArray(
                    System.Linq.Enumerable.Select(
                        snapshot.Containers.Items,
                        item => new WorldContainerHttpResponse(item)))));

        [HttpGet]
        [Route("catalogs/blocks")]
        [ResponseType(typeof(WorldCatalogHttpResponse))]
        public HttpResponseMessage GetBlockCatalog() => ReadCatalog(
            snapshot => snapshot.BlockInternalNames);

        [HttpGet]
        [Route("catalogs/prefabs")]
        [ResponseType(typeof(WorldCatalogHttpResponse))]
        public HttpResponseMessage GetPrefabCatalog() => ReadCatalog(
            snapshot => snapshot.PrefabResourceIds);

        [HttpGet]
        [Route("catalogs/entity-types")]
        [ResponseType(typeof(WorldCatalogHttpResponse))]
        public HttpResponseMessage GetEntityTypeCatalog() => ReadCatalog(
            snapshot => snapshot.EntityTypeResourceIds);

        private HttpResponseMessage ReadWorld(Func<WorldSnapshot, object> select)
        {
            try
            {
                return Request.CreateResponse(HttpStatusCode.OK, select(world.Execute()));
            }
            catch (Exception)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "world_read_unavailable",
                    "World data is unavailable.");
            }
        }

        private HttpResponseMessage ReadCatalog(
            Func<WorldToolCatalogSnapshot, System.Collections.Generic.IReadOnlyList<string>> select)
        {
            try
            {
                var snapshot = catalog.Execute();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new WorldCatalogHttpResponse(snapshot, select(snapshot)));
            }
            catch (Exception)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "world_catalog_unavailable",
                    "The world tool catalog is unavailable.");
            }
        }
    }
}
