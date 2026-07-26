using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/map")]
    public sealed class MapController : ApiController
    {
        private readonly GetMapMetadataUseCase metadataUseCase;
        private readonly GetMapGameTimeUseCase gameTimeUseCase;
        private readonly GetPlayerTrackUseCase playerTrackUseCase;
        private readonly GetMapTileUseCase? tileUseCase;
        private readonly GetMapLayerUseCase? mapLayerUseCase;
        private readonly GetHistoricalPlayerLastLocationsUseCase? historicalLocationsUseCase;
        private readonly SearchPlayersInAreaUseCase? areaSearchUseCase;
        private readonly GetTransientEntityMapLayerUseCase? transientEntityUseCase;

        public MapController(
            GetMapMetadataUseCase metadataUseCase,
            GetMapGameTimeUseCase gameTimeUseCase,
            GetPlayerTrackUseCase playerTrackUseCase,
            GetMapTileUseCase? tileUseCase = null,
            GetMapLayerUseCase? mapLayerUseCase = null,
            GetHistoricalPlayerLastLocationsUseCase? historicalLocationsUseCase = null,
            SearchPlayersInAreaUseCase? areaSearchUseCase = null,
            GetTransientEntityMapLayerUseCase? transientEntityUseCase = null)
        {
            this.metadataUseCase = metadataUseCase ?? throw new ArgumentNullException(nameof(metadataUseCase));
            this.gameTimeUseCase = gameTimeUseCase ?? throw new ArgumentNullException(nameof(gameTimeUseCase));
            this.playerTrackUseCase = playerTrackUseCase ?? throw new ArgumentNullException(nameof(playerTrackUseCase));
            this.tileUseCase = tileUseCase;
            this.mapLayerUseCase = mapLayerUseCase;
            this.historicalLocationsUseCase = historicalLocationsUseCase;
            this.areaSearchUseCase = areaSearchUseCase;
            this.transientEntityUseCase = transientEntityUseCase;
        }

        [HttpGet]
        [Route("metadata")]
        [ResponseType(typeof(MapMetadataHttpResponse))]
        public HttpResponseMessage GetMetadata()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new MapMetadataHttpResponse(metadataUseCase.Execute()));
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "map_metadata_query_failed",
                    "Map metadata could not be read.");
            }
        }

        [HttpGet]
        [Route("game-time")]
        [ResponseType(typeof(MapGameTimeHttpResponse))]
        public HttpResponseMessage GetGameTime()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new MapGameTimeHttpResponse(gameTimeUseCase.Execute()));
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "map_game_time_query_failed",
                    "Map game time could not be read.");
            }
        }

        [HttpGet]
        [Route("players/{crossplatformId}/track")]
        [ResponseType(typeof(PlayerTrackHttpResponse))]
        public HttpResponseMessage GetPlayerTrack(
            string crossplatformId,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null)
        {
            if (!ModelState.IsValid || !fromUtc.HasValue || !toUtc.HasValue)
                return InvalidTrackRequest();

            try
            {
                var result = playerTrackUseCase.Execute(new GetPlayerTrackQuery(
                    crossplatformId,
                    fromUtc.Value,
                    toUtc.Value));
                if (result == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "historical_player_not_found",
                        "The historical player was not found.");
                }

                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerTrackHttpResponse(crossplatformId, result));
            }
            catch (PlayerTrackLimitExceededException)
            {
                return InvalidTrackRequest();
            }
            catch (ArgumentException)
            {
                return InvalidTrackRequest();
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "player_track_query_failed",
                    "Player track data could not be read.");
            }
        }

        [HttpGet]
        [Route("tiles/{worldId}/{z:int}/{x:int}/{y:int}")]
        [ResponseType(typeof(byte[]))]
        public async Task<HttpResponseMessage> GetTile(
            string worldId,
            int z,
            int x,
            int y)
        {
            if (!HasBearerHeader())
            {
                return TileProblem(
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "A Bearer authorization header is required.");
            }

            if (tileUseCase == null)
            {
                return TileProblem(
                    HttpStatusCode.ServiceUnavailable,
                    "map_tile_unavailable",
                    "Map tiles are not available.");
            }

            try
            {
                var cancellationToken = TryGetRequestCancellationToken();
                var result = await tileUseCase.ExecuteAsync(
                        new MapTileKey(worldId, z, x, y),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.Status == MapTileReadStatus.Missing)
                {
                    return TileProblem(
                        HttpStatusCode.NotFound,
                        "map_tile_not_found",
                        "The requested map tile was not found.");
                }
                if (result.Status == MapTileReadStatus.Unavailable)
                {
                    return TileProblem(
                        HttpStatusCode.ServiceUnavailable,
                        "map_tile_unavailable",
                        "Map tiles are not available.");
                }

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = Request,
                    Content = new ByteArrayContent(result.Content!)
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue(result.ContentType!);
                ApplyTileHeaders(response, result.ETag);
                if (MatchesIfNoneMatch(result.ETag!))
                {
                    response.Content.Dispose();
                    response.Content = null;
                    response.StatusCode = HttpStatusCode.NotModified;
                }
                return response;
            }
            catch (ArgumentException)
            {
                return TileProblem(
                    HttpStatusCode.BadRequest,
                    "invalid_map_tile_key",
                    "The map tile coordinates are invalid.");
            }
            catch
            {
                return TileProblem(
                    HttpStatusCode.InternalServerError,
                    "map_tile_read_failed",
                    "The map tile could not be read.");
            }
        }

        [HttpGet]
        [Route("layers/{layerId}")]
        [ResponseType(typeof(MapLayerHttpResponse))]
        public async Task<HttpResponseMessage> GetLayer(
            string layerId,
            string? worldId = null,
            float? minimumX = null,
            float? minimumZ = null,
            float? maximumX = null,
            float? maximumZ = null,
            int? zoom = null,
            int? limit = null)
        {
            if (!ModelState.IsValid ||
                string.IsNullOrWhiteSpace(layerId) ||
                string.IsNullOrWhiteSpace(worldId) ||
                !minimumX.HasValue ||
                !minimumZ.HasValue ||
                !maximumX.HasValue ||
                !maximumZ.HasValue ||
                !zoom.HasValue ||
                !limit.HasValue)
            {
                return InvalidLayerRequest();
            }

            try
            {
                var extent = new MapExtent(
                    minimumX.Value,
                    minimumZ.Value,
                    maximumX.Value,
                    maximumZ.Value);
                if (!IsCurrentWorld(worldId!))
                {
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        MapLayerHttpResponse.Unavailable(layerId));
                }

                if (string.Equals(layerId, "historical-player-locations", StringComparison.Ordinal))
                {
                    if (historicalLocationsUseCase == null)
                        return UnavailableLayer(layerId);
                    var result = await historicalLocationsUseCase.ExecuteAsync(
                            new HistoricalPlayerLastLocationsRequest(extent, zoom.Value, limit.Value),
                            TryGetRequestCancellationToken())
                        .ConfigureAwait(false);
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        MapLayerHttpResponse.FromHistorical(layerId, result));
                }

                if (TryGetMapLayerKind(layerId, out var mapLayerKind))
                {
                    if (mapLayerUseCase == null)
                        return UnavailableLayer(layerId);
                    var snapshot = mapLayerUseCase.Execute(new MapLayerQuery(
                        mapLayerKind,
                        extent,
                        zoom.Value,
                        limit.Value));
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        MapLayerHttpResponse.FromProjection(layerId, snapshot));
                }

                if (TryGetTransientKind(layerId, out var transientKind))
                {
                    if (transientEntityUseCase == null)
                        return UnavailableLayer(layerId);
                    var snapshot = transientEntityUseCase.Execute(new TransientEntityMapQuery(
                        transientKind,
                        extent,
                        zoom.Value,
                        limit.Value));
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        MapLayerHttpResponse.FromTransient(layerId, snapshot));
                }

                return InvalidLayerRequest();
            }
            catch (MapLayerLimitExceededException)
            {
                return InvalidLayerRequest();
            }
            catch (ArgumentException)
            {
                return InvalidLayerRequest();
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "map_layer_query_failed",
                    "The map layer could not be read.");
            }
        }

        [HttpGet]
        [Route("players/area")]
        [ResponseType(typeof(PlayerAreaSearchHttpResponse))]
        public HttpResponseMessage SearchPlayersInArea(
            string? shape = null,
            double? minimumX = null,
            double? minimumZ = null,
            double? maximumX = null,
            double? maximumZ = null,
            double? centerX = null,
            double? centerZ = null,
            double? radius = null,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            int? limit = null)
        {
            if (!ModelState.IsValid ||
                areaSearchUseCase == null ||
                !fromUtc.HasValue ||
                !toUtc.HasValue)
            {
                return InvalidAreaRequest();
            }

            try
            {
                PlayerMapRectangle? rectangle = null;
                PlayerMapCircle? circle = null;
                if (string.Equals(shape, "rectangle", StringComparison.Ordinal) &&
                    minimumX.HasValue && minimumZ.HasValue &&
                    maximumX.HasValue && maximumZ.HasValue &&
                    !centerX.HasValue && !centerZ.HasValue && !radius.HasValue)
                {
                    rectangle = new PlayerMapRectangle(
                        minimumX.Value,
                        minimumZ.Value,
                        maximumX.Value,
                        maximumZ.Value);
                }
                else if (string.Equals(shape, "circle", StringComparison.Ordinal) &&
                    centerX.HasValue && centerZ.HasValue && radius.HasValue &&
                    !minimumX.HasValue && !minimumZ.HasValue &&
                    !maximumX.HasValue && !maximumZ.HasValue)
                {
                    circle = new PlayerMapCircle(
                        centerX.Value,
                        centerZ.Value,
                        radius.Value);
                }
                else
                {
                    return InvalidAreaRequest();
                }

                var result = areaSearchUseCase.Execute(new SearchPlayersInAreaRequest(
                    fromUtc.Value,
                    toUtc.Value,
                    rectangle,
                    circle,
                    playerResultLimit: limit ?? SearchPlayersInAreaRequest.DefaultPlayerResultLimit));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerAreaSearchHttpResponse(result));
            }
            catch (ArgumentException)
            {
                return InvalidAreaRequest();
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "player_area_query_failed",
                    "Player area observations could not be read.");
            }
        }

        private HttpResponseMessage InvalidTrackRequest() => Problem(
            HttpStatusCode.BadRequest,
            "invalid_player_track_query",
            "The player track query is invalid.");

        private HttpResponseMessage InvalidLayerRequest() => Problem(
            HttpStatusCode.BadRequest,
            "invalid_map_layer_query",
            "The map layer query is invalid.");

        private HttpResponseMessage InvalidAreaRequest() => Problem(
            HttpStatusCode.BadRequest,
            "invalid_player_area_query",
            "The player area query is invalid.");

        private HttpResponseMessage UnavailableLayer(string layerId) =>
            Request.CreateResponse(
                HttpStatusCode.OK,
                MapLayerHttpResponse.Unavailable(layerId));

        private bool IsCurrentWorld(string worldId)
        {
            var metadata = metadataUseCase.Execute();
            return metadata.Availability != AvailabilityState.Unavailable &&
                string.Equals(metadata.WorldId, worldId, StringComparison.Ordinal);
        }

        private static bool TryGetMapLayerKind(string layerId, out MapLayerKind kind)
        {
            switch (layerId)
            {
                case "traders": kind = MapLayerKind.Traders; return true;
                case "land-claims": kind = MapLayerKind.LandClaims; return true;
                case "vehicles": kind = MapLayerKind.Vehicles; return true;
                case "drones": kind = MapLayerKind.Drones; return true;
                default: kind = default; return false;
            }
        }

        private static bool TryGetTransientKind(string layerId, out TransientEntityMapKind kind)
        {
            switch (layerId)
            {
                case "animals": kind = TransientEntityMapKind.Animals; return true;
                case "hostiles": kind = TransientEntityMapKind.Hostiles; return true;
                default: kind = default; return false;
            }
        }

        private bool HasBearerHeader()
        {
            var authorization = Request.Headers.Authorization;
            return authorization != null &&
                string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(authorization.Parameter);
        }

        private bool MatchesIfNoneMatch(string etag) =>
            Request.Headers.IfNoneMatch.Any(candidate =>
                string.Equals(candidate.Tag, etag, StringComparison.Ordinal) ||
                string.Equals(candidate.Tag, "*", StringComparison.Ordinal));

        private CancellationToken TryGetRequestCancellationToken()
        {
            try
            {
                return Request.GetOwinContext().Request.CallCancelled;
            }
            catch
            {
                return CancellationToken.None;
            }
        }

        private HttpResponseMessage TileProblem(
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            var response = Problem(statusCode, code, detail);
            ApplyTileHeaders(response, null);
            return response;
        }

        private static void ApplyTileHeaders(HttpResponseMessage response, string? etag)
        {
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                Private = true,
                MustRevalidate = true,
                MaxAge = TimeSpan.Zero
            };
            if (etag != null)
                response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        private HttpResponseMessage Problem(
            HttpStatusCode statusCode,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, statusCode, code, detail);
    }
}
