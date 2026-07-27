using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/map-jobs")]
    public sealed class MapJobsController : ApiController
    {
        private readonly SubmitMapJobUseCase submitMapJob;
        private readonly QueryWorldUseCase queryWorld;

        public MapJobsController(
            SubmitMapJobUseCase submitMapJob,
            QueryWorldUseCase queryWorld)
        {
            this.submitMapJob = submitMapJob ?? throw new ArgumentNullException(nameof(submitMapJob));
            this.queryWorld = queryWorld ?? throw new ArgumentNullException(nameof(queryWorld));
        }

        [HttpPost]
        [Route("refresh-resources")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage RefreshResources(RefreshMapResourcesJobHttpRequest? request) =>
            Submit(request, MapJobKind.RefreshResources, strongConfirmed: false);

        [HttpPost]
        [Route("render-explored")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage RenderExplored(RenderExploredMapJobHttpRequest? request) =>
            Submit(request, MapJobKind.RenderExplored, strongConfirmed: false);

        [HttpPost]
        [Route("render-full")]
        [ResponseType(typeof(WorldOperationReceiptHttpResponse))]
        public HttpResponseMessage RenderFull(RenderFullMapJobHttpRequest? request) =>
            Submit(request, MapJobKind.RenderFull, request?.StrongConfirmed ?? false);

        [HttpGet]
        [Route("resource-version")]
        [ResponseType(typeof(MapResourceVersionHttpResponse))]
        public HttpResponseMessage GetResourceVersion()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new MapResourceVersionHttpResponse(queryWorld.Execute().World));
            }
            catch (Exception)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "map_resource_version_unavailable",
                    "The map resource version is unavailable.");
            }
        }

        private HttpResponseMessage Submit<TRequest>(
            TRequest? request,
            MapJobKind kind,
            bool strongConfirmed)
            where TRequest : ConfirmedWorldHttpRequest
        {
            if (!ModelState.IsValid || request == null) return InvalidRequest();
            var actor = ActorSubject();
            if (actor == null)
            {
                return Problem(
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to submit a map job.");
            }

            try
            {
                var receipt = submitMapJob.Execute(new SubmitMapJobRequest(
                    actor,
                    kind,
                    Text(request.WorldId),
                    Text(request.WorldVersion),
                    request.MapResourceVersion,
                    Bounds(request is RefreshMapResourcesJobHttpRequest refresh
                        ? refresh.Bounds
                        : request is RenderExploredMapJobHttpRequest explored
                            ? explored.Bounds
                            : ((RenderFullMapJobHttpRequest)(object)request).Bounds),
                    ApiProblemDetailsFactory.GetTraceId(Request),
                    request.Confirmed,
                    strongConfirmed,
                    DateTimeOffset.UtcNow));
                var response = Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new WorldOperationReceiptHttpResponse(receipt));
                response.Headers.Location = new Uri(
                    Request.RequestUri,
                    "/api/v1/world-operations/" + Uri.EscapeDataString(receipt.OperationId));
                return response;
            }
            catch (WorldOperationConfirmationRequiredException)
            {
                return Problem(
                    (HttpStatusCode)422,
                    "confirmation_required",
                    "Explicit confirmation is required for this map job.");
            }
            catch (WorldOperationStrongConfirmationRequiredException)
            {
                return Problem(
                    (HttpStatusCode)422,
                    "strong_confirmation_required",
                    "Strong confirmation is required for this map job.");
            }
            catch (WorldOperationConflictException exception)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    SafeConflictCode(exception.Code),
                    "The map job conflicts with current server state.");
            }
            catch (KeyNotFoundException)
            {
                return Problem(
                    HttpStatusCode.NotFound,
                    "world_resource_not_found",
                    "The requested world resource was not found.");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "map_job_unavailable",
                    "The map job service is unavailable.");
            }
        }

        private string? ActorSubject()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var value = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private HttpResponseMessage InvalidRequest() => Problem(
            (HttpStatusCode)422,
            "invalid_map_job_request",
            "The map job request is invalid.");

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) => ApiProblemDetailsFactory.CreateResponse(
                Request,
                status,
                code,
                detail);

        private static string Text(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required.");
            return value!;
        }

        private static WorldMapBounds? Bounds(WorldMapBoundsHttpRequest? value)
        {
            if (value == null) return null;
            if (!value.MinimumX.HasValue || !value.MinimumZ.HasValue ||
                !value.MaximumX.HasValue || !value.MaximumZ.HasValue)
            {
                throw new ArgumentException("Complete map bounds are required.");
            }
            return new WorldMapBounds(
                value.MinimumX.Value,
                value.MinimumZ.Value,
                value.MaximumX.Value,
                value.MaximumZ.Value);
        }

        private static string SafeConflictCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
                return "map_job_conflict";
            foreach (var character in code)
            {
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= '0' && character <= '9') ||
                      character == '_'))
                {
                    return "map_job_conflict";
                }
            }
            return code;
        }
    }
}
