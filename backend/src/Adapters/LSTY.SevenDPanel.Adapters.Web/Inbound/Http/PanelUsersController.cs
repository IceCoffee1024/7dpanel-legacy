using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/panel-users")]
    public sealed class PanelUsersController : ApiController
    {
        private readonly IPanelUserAdministrationStore store;

        public PanelUsersController(IPanelUserAdministrationStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        [HttpGet, Route("")]
        [ResponseType(typeof(PanelUserRecord[]))]
        public HttpResponseMessage Get() =>
            Request.CreateResponse(HttpStatusCode.OK, store.ListUsers());

        [HttpPost, Route("")]
        [ResponseType(typeof(PanelUserRecord))]
        public HttpResponseMessage Post(PanelUserCreateRequest? request)
        {
            if (request == null || !ModelState.IsValid)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            var result = store.CreateUser(
                request.Username ?? string.Empty,
                request.Password ?? string.Empty,
                request.Role ?? string.Empty,
                request.Enabled);
            return MutationResponse(result, HttpStatusCode.Created);
        }

        [HttpPut, Route("{subject}")]
        [ResponseType(typeof(PanelUserRecord))]
        public HttpResponseMessage Put(string subject, PanelUserUpdateRequest? request)
        {
            if (request == null || !ModelState.IsValid)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            return MutationResponse(store.UpdateUser(
                subject,
                request.Username ?? string.Empty,
                request.Role ?? string.Empty,
                request.Enabled));
        }

        [HttpPost, Route("{subject}/password")]
        [ResponseType(typeof(void))]
        public HttpResponseMessage ResetPassword(string subject, PanelUserPasswordRequest? request)
        {
            if (request == null || !ModelState.IsValid)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            return MutationResponse(
                store.ResetPassword(subject, request.Password ?? string.Empty),
                HttpStatusCode.NoContent);
        }

        [HttpDelete, Route("{subject}")]
        [ResponseType(typeof(void))]
        public HttpResponseMessage Delete(string subject) =>
            MutationResponse(store.DeleteUser(subject), HttpStatusCode.NoContent);

        private HttpResponseMessage MutationResponse(
            PanelUserMutationResult result,
            HttpStatusCode success = HttpStatusCode.OK)
        {
            switch (result.Status)
            {
                case PanelUserMutationStatus.Created:
                case PanelUserMutationStatus.Updated:
                    return success == HttpStatusCode.NoContent
                        ? Request.CreateResponse(HttpStatusCode.NoContent)
                        : Request.CreateResponse(success, result.User);
                case PanelUserMutationStatus.Deleted:
                    return Request.CreateResponse(HttpStatusCode.NoContent);
                case PanelUserMutationStatus.Invalid:
                    return Problem(HttpStatusCode.BadRequest, "invalid_panel_user", "The panel user input is invalid.");
                case PanelUserMutationStatus.NotFound:
                    return Problem(HttpStatusCode.NotFound, "panel_user_not_found", "The panel user was not found.");
                case PanelUserMutationStatus.LastOwner:
                    return Problem(HttpStatusCode.Conflict, "last_owner_required", "At least one enabled Owner must remain.");
                case PanelUserMutationStatus.Conflict:
                    return Problem(HttpStatusCode.Conflict, "panel_user_conflict", "The username is already in use.");
                default:
                    return Problem(HttpStatusCode.InternalServerError, "panel_user_update_failed", "The panel user could not be updated.");
            }
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }

    public sealed class PanelUserCreateRequest
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Role { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public sealed class PanelUserUpdateRequest
    {
        public string? Username { get; set; }
        public string? Role { get; set; }
        public bool Enabled { get; set; }
    }

    public sealed class PanelUserPasswordRequest
    {
        public string? Password { get; set; }
    }
}
