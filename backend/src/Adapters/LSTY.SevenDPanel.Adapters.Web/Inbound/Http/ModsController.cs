using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Mods;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/mods")]
    public sealed class ModsController : ApiController
    {
        private const string ModsPath = "/api/v1/mods";
        private readonly ListModsUseCase listMods;
        private readonly SetModStateUseCase setModState;

        public ModsController(ListModsUseCase listMods, SetModStateUseCase setModState)
        {
            this.listMods = listMods ?? throw new ArgumentNullException(nameof(listMods));
            this.setModState = setModState ?? throw new ArgumentNullException(nameof(setModState));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ModHttpResponse[]))]
        public HttpResponseMessage Get()
        {
            return Request.CreateResponse(
                HttpStatusCode.OK,
                listMods.Execute().Select(mod => new ModHttpResponse(mod)).ToArray());
        }

        [HttpPut]
        [Authorize(Roles = "Owner")]
        [Route("{directoryId}/state")]
        [ResponseType(typeof(ModStateHttpResponse))]
        public HttpResponseMessage Put(string directoryId, SetModStateHttpRequest? request)
        {
            if (!ModelState.IsValid || request?.Enabled == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);

            var result = setModState.Execute(directoryId, request.Enabled.Value);
            switch (result.Status)
            {
                case ModStateChangeStatus.Changed:
                case ModStateChangeStatus.Unchanged:
                    return Request.CreateResponse(
                        HttpStatusCode.OK,
                        new ModStateHttpResponse(
                            directoryId,
                            request.Enabled.Value,
                            result.Status == ModStateChangeStatus.Changed ? "changed" : "unchanged"));
                case ModStateChangeStatus.InvalidDirectory:
                    return Problem(HttpStatusCode.BadRequest, "invalid_mod_directory", "The mod directory identifier is invalid.");
                case ModStateChangeStatus.NotFound:
                    return Problem(HttpStatusCode.NotFound, "mod_not_found", "The mod was not found.");
                case ModStateChangeStatus.Protected:
                    return Problem(HttpStatusCode.Forbidden, "protected_mod", "This required mod cannot be disabled.");
                case ModStateChangeStatus.Conflict:
                    return Problem(HttpStatusCode.Conflict, "mod_state_conflict", "The mod marker files are in a conflicting state.");
                default:
                    return Problem(HttpStatusCode.InternalServerError, "mod_state_change_failed", "The next-start mod state could not be changed.");
            }
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail, ModsPath);
    }
}
