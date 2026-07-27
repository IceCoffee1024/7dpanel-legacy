using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Modules;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/modules")]
    public sealed class ModulesController : ApiController
    {
        private readonly FeatureModuleUseCases useCases;

        public ModulesController(FeatureModuleUseCases useCases) =>
            this.useCases = useCases ?? throw new ArgumentNullException(nameof(useCases));

        [HttpGet, Route("")]
        [ResponseType(typeof(FeatureModuleHttpResponse[]))]
        public HttpResponseMessage Get() =>
            Request.CreateResponse(
                HttpStatusCode.OK,
                useCases.List().Select(summary => new FeatureModuleHttpResponse(summary)).ToArray());

        [HttpPost, Route("{moduleId}/enable")]
        [ResponseType(typeof(FeatureModuleHttpResponse))]
        public HttpResponseMessage Enable(
            string moduleId,
            SetFeatureModuleStateHttpRequest? request) =>
            SetEnabled(moduleId, request, true);

        [HttpPost, Route("{moduleId}/disable")]
        [ResponseType(typeof(FeatureModuleHttpResponse))]
        public HttpResponseMessage Disable(
            string moduleId,
            SetFeatureModuleStateHttpRequest? request) =>
            SetEnabled(moduleId, request, false);

        private HttpResponseMessage SetEnabled(
            string moduleId,
            SetFeatureModuleStateHttpRequest? request,
            bool isEnabled)
        {
            if (request == null || !ModelState.IsValid || request.ExpectedRowVersion < 0)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryParseModuleId(moduleId, out var parsed))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.NotFound,
                    "feature_module_not_found",
                    "The requested feature module was not found.");
            }

            var actorSubject = GetActorSubject();
            if (actorSubject == null)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to change a feature module.");
            }

            try
            {
                var state = useCases.SetEnabled(new SetFeatureModuleStateRequest(
                    parsed,
                    isEnabled,
                    actorSubject,
                    "module:" + Guid.NewGuid().ToString("N"),
                    request.ExpectedRowVersion));
                var summary = useCases.List().Single(value => value.Descriptor.Id == state.ModuleId);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new FeatureModuleHttpResponse(summary));
            }
            catch (FeatureModuleStateConflictException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "feature_module_version_conflict",
                    "The feature module state changed. Refresh and try again.");
            }
            catch (FeatureModuleNotToggleableException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "feature_module_not_toggleable",
                    "This core feature module cannot be disabled.");
            }
            catch (FeatureModuleDependencyException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "feature_module_dependency_conflict",
                    "The feature module dependency state prevents this change.");
            }
            catch (FeatureModuleActiveWorkException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "feature_module_active_work",
                    "The feature module still has active work.");
            }
        }

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);

        private string? GetActorSubject()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrWhiteSpace(subject) ? null : subject;
        }

        private static bool TryParseModuleId(string value, out FeatureModuleId moduleId) =>
            Enum.TryParse(value, true, out moduleId) &&
            Enum.IsDefined(typeof(FeatureModuleId), moduleId);
    }
}
