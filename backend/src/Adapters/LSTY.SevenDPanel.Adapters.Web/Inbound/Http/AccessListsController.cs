using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class AccessListEditorAuthorizeAttribute : AuthorizeAttribute
    {
        public AccessListEditorAuthorizeAttribute() { Roles = "Owner,Admin"; }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (actionContext.RequestContext.Principal?.Identity?.IsAuthenticated == true)
            {
                actionContext.Response = ApiProblemDetailsFactory.CreateResponse(
                    actionContext.Request,
                    HttpStatusCode.Forbidden,
                    "access_list_editor_required",
                    "Owner or Admin access is required to change access lists.");
                return;
            }
            base.HandleUnauthorizedRequest(actionContext);
        }
    }

    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/access-lists")]
    public sealed class AccessListsController : ApiController
    {
        private const string AccessListsPath = "/api/v1/access-lists";
        private readonly AccessListUseCases useCases;

        public AccessListsController(AccessListUseCases useCases)
        {
            this.useCases = useCases ?? throw new ArgumentNullException(nameof(useCases));
        }

        [HttpGet]
        [Route("bans")]
        [ResponseType(typeof(BanEntryHttpResponse[]))]
        public async Task<HttpResponseMessage> GetBans(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await useCases.GetBansAsync(cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    entries.Select(entry => new BanEntryHttpResponse(entry)).ToArray());
            }
            catch (AccessListGameNotReadyException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to read bans.");
            }
        }

        [HttpGet]
        [Route("whitelist")]
        [ResponseType(typeof(WhitelistEntryHttpResponse[]))]
        public async Task<HttpResponseMessage> GetWhitelist(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await useCases.GetWhitelistAsync(cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    entries.Select(entry => new WhitelistEntryHttpResponse(entry)).ToArray());
            }
            catch (AccessListGameNotReadyException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to read the whitelist.");
            }
        }

        [AccessListEditorAuthorize]
        [HttpPut]
        [Route("bans/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> PutBan(
            string playerId,
            BanUpsertHttpRequest? request,
            CancellationToken cancellationToken) =>
            MutateAsync(
                actor => useCases.UpsertBanAsync(
                    actor,
                    new BanRequest(
                        playerId,
                        request?.DisplayName ?? string.Empty,
                        request?.BannedUntilUtc,
                        request?.Reason),
                    cancellationToken));

        [AccessListEditorAuthorize]
        [HttpDelete]
        [Route("bans/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> DeleteBan(string playerId, CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.RemoveBanAsync(actor, playerId, cancellationToken));

        [AccessListEditorAuthorize]
        [HttpPut]
        [Route("whitelist/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> PutWhitelist(
            string playerId,
            WhitelistUpsertHttpRequest? request,
            CancellationToken cancellationToken) =>
            MutateAsync(
                actor => useCases.UpsertWhitelistAsync(
                    actor,
                    new WhitelistRequest(playerId, request?.DisplayName ?? string.Empty),
                    cancellationToken));

        [AccessListEditorAuthorize]
        [HttpDelete]
        [Route("whitelist/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> DeleteWhitelist(string playerId, CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.RemoveWhitelistAsync(actor, playerId, cancellationToken));

        private async Task<HttpResponseMessage> MutateAsync(
            Func<string, Task<AccessListMutationResult>> mutation)
        {
            if (!ModelState.IsValid)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryGetActor(out var actor))
                return Problem(HttpStatusCode.Unauthorized, "authentication_required", "Authentication is required.");

            AccessListMutationResult result;
            try
            {
                result = await mutation(actor!).ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                return Problem(HttpStatusCode.BadRequest, "invalid_access_list_request", exception.Message);
            }

            switch (result.Status)
            {
                case AccessListMutationStatus.Succeeded:
                    return Request.CreateResponse(HttpStatusCode.NoContent);
                case AccessListMutationStatus.NotFound:
                    return Problem(HttpStatusCode.NotFound, "access_list_entry_not_found", "The access-list entry was not found.");
                case AccessListMutationStatus.Conflict:
                    return Problem(HttpStatusCode.Conflict, "access_list_conflict", "The access list changed before the operation completed.");
                case AccessListMutationStatus.GameNotReady:
                    return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready for access-list changes.");
                case AccessListMutationStatus.NativeRejected:
                    return Problem(HttpStatusCode.BadGateway, "native_access_list_rejected", "7DTD rejected the access-list change.");
                default:
                    return Problem(HttpStatusCode.InternalServerError, "access_list_update_failed", "The access-list change could not be confirmed.");
            }
        }

        private bool TryGetActor(out string? actor)
        {
            actor = (User?.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(actor);
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail, AccessListsPath);
    }
}
