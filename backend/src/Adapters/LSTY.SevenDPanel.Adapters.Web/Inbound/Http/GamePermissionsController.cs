using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/game-permissions")]
    public sealed class GamePermissionsController : ApiController
    {
        private const string PermissionsPath = "/api/v1/game-permissions";
        private readonly GamePermissionUseCases useCases;

        public GamePermissionsController(GamePermissionUseCases useCases)
        {
            this.useCases = useCases ?? throw new ArgumentNullException(nameof(useCases));
        }

        [HttpGet]
        [Route("admins")]
        [ResponseType(typeof(GameAdminHttpResponse[]))]
        public async Task<HttpResponseMessage> GetAdmins(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await useCases.GetAdminsAsync(cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(HttpStatusCode.OK, entries.Select(entry => new GameAdminHttpResponse(entry)).ToArray());
            }
            catch (GamePermissionGameNotReadyException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to read administrators.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.InternalServerError, "game_permission_read_failed", "Game administrators could not be read.");
            }
        }

        [HttpGet]
        [Route("commands")]
        [ResponseType(typeof(CommandPermissionHttpResponse[]))]
        public async Task<HttpResponseMessage> GetCommands(CancellationToken cancellationToken)
        {
            try
            {
                var entries = await useCases.GetCommandsAsync(cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(HttpStatusCode.OK, entries.Select(entry => new CommandPermissionHttpResponse(entry)).ToArray());
            }
            catch (GamePermissionGameNotReadyException)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready to read command permissions.");
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.InternalServerError, "game_permission_read_failed", "Command permissions could not be read.");
            }
        }

        [HttpPut]
        [Route("admins/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> PutAdmin(
            string playerId,
            GameAdminUpsertHttpRequest? request,
            CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.UpsertAdminAsync(
                actor,
                new GameAdminEntry(playerId, request?.DisplayName ?? string.Empty, request?.PermissionLevel ?? -1),
                cancellationToken));

        [HttpDelete]
        [Route("admins/{playerId}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> DeleteAdmin(string playerId, CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.RemoveAdminAsync(actor, playerId, cancellationToken));

        [HttpPut]
        [Route("commands/{command}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> PutCommand(
            string command,
            CommandPermissionUpsertHttpRequest? request,
            CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.UpsertCommandAsync(
                actor,
                new CommandPermissionRequest(command, request?.PermissionLevel ?? -1),
                cancellationToken));

        [HttpDelete]
        [Route("commands/{command}")]
        [ResponseType(typeof(void))]
        public Task<HttpResponseMessage> DeleteCommand(string command, CancellationToken cancellationToken) =>
            MutateAsync(actor => useCases.RemoveCommandAsync(actor, command, cancellationToken));

        private async Task<HttpResponseMessage> MutateAsync(Func<string, Task<GamePermissionMutationResult>> action)
        {
            if (!ModelState.IsValid)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryGetActor(out var actor))
                return Problem(HttpStatusCode.Unauthorized, "authentication_required", "Authentication is required.");

            GamePermissionMutationResult result;
            try
            {
                result = await action(actor!).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.InternalServerError, "game_permission_update_failed", "The game permission change could not be confirmed.");
            }

            switch (result.Status)
            {
                case GamePermissionMutationStatus.Succeeded:
                    return Request.CreateResponse(HttpStatusCode.NoContent);
                case GamePermissionMutationStatus.Invalid:
                    return Problem(HttpStatusCode.BadRequest, "invalid_game_permission_request", "The game permission request is invalid.");
                case GamePermissionMutationStatus.NotFound:
                    return Problem(HttpStatusCode.NotFound, "game_permission_not_found", "The game permission entry was not found.");
                case GamePermissionMutationStatus.Conflict:
                    return Problem(HttpStatusCode.Conflict, "game_permission_conflict", "The game permission changed before the operation completed.");
                case GamePermissionMutationStatus.GameNotReady:
                    return Problem(HttpStatusCode.ServiceUnavailable, "game_not_ready", "The game is not ready for permission changes.");
                case GamePermissionMutationStatus.NativeRejected:
                    return Problem(HttpStatusCode.BadGateway, "native_game_permission_rejected", "7DTD rejected the permission change.");
                default:
                    return Problem(HttpStatusCode.InternalServerError, "game_permission_update_failed", "The game permission change could not be confirmed.");
            }
        }

        private bool TryGetActor(out string? actor)
        {
            actor = (User?.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return !string.IsNullOrWhiteSpace(actor);
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail, PermissionsPath);
    }
}
