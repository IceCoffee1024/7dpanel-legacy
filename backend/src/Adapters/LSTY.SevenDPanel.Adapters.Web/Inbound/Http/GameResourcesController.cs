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
    [Authorize]
    [RoutePrefix("api/v1/game-resources")]
    public sealed class GameResourcesController : ApiController
    {
        private const string ResourcesPath = "/api/v1/game-resources";
        private static readonly TimeSpan BuildingRetryAfter = TimeSpan.FromSeconds(2);
        private readonly QueryGameResourcesUseCase queryUseCase;
        private readonly GetGameResourceIconUseCase iconUseCase;

        public GameResourcesController(
            QueryGameResourcesUseCase queryUseCase,
            GetGameResourceIconUseCase iconUseCase)
        {
            this.queryUseCase = queryUseCase ?? throw new ArgumentNullException(nameof(queryUseCase));
            this.iconUseCase = iconUseCase ?? throw new ArgumentNullException(nameof(iconUseCase));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(GameResourcePageHttpResponse))]
        public HttpResponseMessage Get(
            string? search = null,
            string kind = "all",
            bool includeHidden = false,
            string language = "en",
            int page = 1,
            int pageSize = 50)
        {
            if (!ModelState.IsValid || !TryParseKind(kind, out var parsedKind))
                return InvalidQuery();

            try
            {
                var result = queryUseCase.Execute(
                    new GameResourceQuery(
                        search,
                        parsedKind,
                        includeHidden,
                        language,
                        page,
                        pageSize),
                    Access());
                switch (result.Status)
                {
                    case GameResourceCatalogReadStatus.Available:
                        return Request.CreateResponse(
                            HttpStatusCode.OK,
                            new GameResourcePageHttpResponse(result));
                    case GameResourceCatalogReadStatus.Building:
                    {
                        var response = Problem(
                            HttpStatusCode.ServiceUnavailable,
                            "game-resource-catalog-building",
                            "The game-resource catalog is being built.");
                        response.Headers.RetryAfter = new RetryConditionHeaderValue(BuildingRetryAfter);
                        return response;
                    }
                    default:
                        return CatalogUnavailable();
                }
            }
            catch (GameResourceHiddenForbiddenException)
            {
                return Problem(
                    HttpStatusCode.Forbidden,
                    "game-resource-hidden-forbidden",
                    "Owner access is required to include hidden game resources.");
            }
            catch (ArgumentException)
            {
                return InvalidQuery();
            }
            catch
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "game-resource-query-failed",
                    "Game resources could not be read.");
            }
        }

        [HttpGet]
        [Route("{resourceId}/icon")]
        [ResponseType(typeof(byte[]))]
        public async Task<HttpResponseMessage> GetIcon(string resourceId)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(resourceId))
                return IconNotFound();

            try
            {
                var result = await iconUseCase.ExecuteAsync(
                        resourceId,
                        Access(),
                        TryGetRequestCancellationToken())
                    .ConfigureAwait(false);
                switch (result.Status)
                {
                    case GameResourceIconReadStatus.Available:
                        return AvailableIcon(result);
                    case GameResourceIconReadStatus.Missing:
                        return IconNotFound();
                    default:
                        return IconProblem(
                            HttpStatusCode.ServiceUnavailable,
                            "game-resource-catalog-unavailable",
                            "The game-resource catalog is unavailable.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException)
            {
                return IconNotFound();
            }
            catch
            {
                return IconProblem(
                    HttpStatusCode.InternalServerError,
                    "game-resource-icon-read-failed",
                    "The game-resource icon could not be read.");
            }
        }

        private HttpResponseMessage AvailableIcon(GameResourceIconReadResult result)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = Request,
                Content = new ByteArrayContent(result.Content!)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            ApplyIconHeaders(response, result.ETag);
            if (MatchesIfNoneMatch(result.ETag!))
            {
                response.Content.Dispose();
                response.Content = null;
                response.StatusCode = HttpStatusCode.NotModified;
            }
            return response;
        }

        private GameResourceAccess Access() =>
            User?.IsInRole("Owner") == true
                ? GameResourceAccess.Owner
                : GameResourceAccess.Standard;

        private static bool TryParseKind(string? value, out GameResourceKind? kind)
        {
            if (string.Equals(value, "all", StringComparison.Ordinal))
            {
                kind = null;
                return true;
            }
            if (string.Equals(value, "item", StringComparison.Ordinal))
            {
                kind = GameResourceKind.Item;
                return true;
            }
            if (string.Equals(value, "block", StringComparison.Ordinal))
            {
                kind = GameResourceKind.Block;
                return true;
            }

            kind = null;
            return false;
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

        private HttpResponseMessage InvalidQuery() => Problem(
            HttpStatusCode.BadRequest,
            "invalid-game-resource-query",
            "The game-resource query is invalid.");

        private HttpResponseMessage CatalogUnavailable() => Problem(
            HttpStatusCode.ServiceUnavailable,
            "game-resource-catalog-unavailable",
            "The game-resource catalog is unavailable.");

        private HttpResponseMessage IconNotFound() => IconProblem(
            HttpStatusCode.NotFound,
            "game-resource-icon-not-found",
            "The game-resource icon was not found.");

        private HttpResponseMessage IconProblem(
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            var response = Problem(statusCode, code, detail);
            ApplyIconHeaders(response, null);
            return response;
        }

        private HttpResponseMessage Problem(
            HttpStatusCode statusCode,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                statusCode,
                code,
                detail,
                ResourcesPath);

        private static void ApplyIconHeaders(HttpResponseMessage response, string? etag)
        {
            response.Headers.CacheControl = new CacheControlHeaderValue { Private = true };
            response.Headers.TryAddWithoutValidation("X-Content-Type-Options", "nosniff");
            if (etag != null)
                response.Headers.ETag = new EntityTagHeaderValue(etag);
        }
    }
}
