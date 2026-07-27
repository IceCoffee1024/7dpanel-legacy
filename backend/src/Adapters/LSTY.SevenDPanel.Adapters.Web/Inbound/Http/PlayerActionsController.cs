using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/player-actions")]
    public sealed class PlayerActionsController : ApiController
    {
        private readonly GrantItemUseCase grantItem;
        private readonly RemoveItemUseCase removeItem;
        private readonly ResetSkillsUseCase resetSkills;
        private readonly ClearInventoryUseCase clearInventory;
        private readonly ResetPlayerDataUseCase resetPlayerData;
        private readonly IPlayerActionOperationQuery operations;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public PlayerActionsController(
            GrantItemUseCase grantItem,
            RemoveItemUseCase removeItem,
            ResetSkillsUseCase resetSkills,
            ClearInventoryUseCase clearInventory,
            ResetPlayerDataUseCase resetPlayerData,
            IPlayerActionOperationQuery operations,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.grantItem = grantItem ?? throw new ArgumentNullException(nameof(grantItem));
            this.removeItem = removeItem ?? throw new ArgumentNullException(nameof(removeItem));
            this.resetSkills = resetSkills ?? throw new ArgumentNullException(nameof(resetSkills));
            this.clearInventory = clearInventory ?? throw new ArgumentNullException(nameof(clearInventory));
            this.resetPlayerData = resetPlayerData ?? throw new ArgumentNullException(nameof(resetPlayerData));
            this.operations = operations ?? throw new ArgumentNullException(nameof(operations));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpPost]
        [Route("grant-item")]
        [ResponseType(typeof(GrantItemHttpResponse))]
        public async Task<HttpResponseMessage> GrantItem(
            GrantItemHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (!TryPrepare(body, out var actor, out var correlation, out var error))
                return error!;
            try
            {
                var result = await grantItem.ExecuteAsync(
                    new GrantItemRequest(
                        actor!,
                        body!.Target!.ToTargetStamp(),
                        RequireText(body.CatalogVersion),
                        RequireText(body.ResourceId),
                        body.Quantity,
                        body.Quality,
                        body.HiddenItemConfirmed,
                        RequireText(body.ClientRequestKey),
                        correlation),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    StatusCode(result.Status),
                    new GrantItemHttpResponse(
                        result,
                        CorrelationFor(result.OperationId, correlation!)));
            }
            catch (GrantItemRequestRejectedException exception)
            {
                return GrantRejected(exception.Code);
            }
            catch (GrantItemIdempotencyConflictException)
            {
                return Conflict("player_action_idempotency_conflict");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("remove-item")]
        [ResponseType(typeof(RemoveItemHttpResponse))]
        public async Task<HttpResponseMessage> RemoveItem(
            RemoveItemHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (!TryPrepare(body, out var actor, out var correlation, out var error))
                return error!;
            try
            {
                var result = await removeItem.ExecuteAsync(
                    new RemoveItemRequest(
                        actor!,
                        body!.Target!.ToTargetStamp(),
                        RequireText(body.CatalogVersion),
                        RequireText(body.ResourceId),
                        body.Quantity,
                        body.Quality,
                        body.RemovalScope,
                        body.RemovalMode,
                        RequireText(body.ClientRequestKey),
                        correlation),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    StatusCode(result.Status),
                    new RemoveItemHttpResponse(
                        result,
                        CorrelationFor(result.OperationId, correlation!)));
            }
            catch (RemoveItemCatalogUnavailableException)
            {
                return Unavailable("catalog_unavailable");
            }
            catch (RemoveItemCatalogConflictException)
            {
                return Conflict("catalog_changed");
            }
            catch (RemoveItemTargetNotFreshException)
            {
                return Conflict("target_not_fresh");
            }
            catch (RemoveItemIdempotencyConflictException)
            {
                return Conflict("player_action_idempotency_conflict");
            }
            catch (RemoveItemOperationStateConflictException)
            {
                return Conflict("player_action_state_conflict");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("reset-skills")]
        [ResponseType(typeof(ResetSkillsHttpResponse))]
        public async Task<HttpResponseMessage> ResetSkills(
            ResetSkillsHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (!TryPrepare(body, out var actor, out var correlation, out var error))
                return error!;
            try
            {
                var result = await resetSkills.ExecuteAsync(
                    new ResetSkillsRequest(
                        actor!,
                        body!.Target!.ToTargetStamp(),
                        RequireText(body.ClientRequestKey),
                        correlation!,
                        body.DangerConfirmed),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    StatusCode(result.Status == ResetSkillsOperationStatus.Pending),
                    new ResetSkillsHttpResponse(
                        result,
                        CorrelationFor(result.OperationId, correlation!)));
            }
            catch (ResetSkillsIdempotencyConflictException)
            {
                return Conflict("player_action_idempotency_conflict");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("clear-inventory")]
        [ResponseType(typeof(ClearInventoryHttpResponse))]
        public async Task<HttpResponseMessage> ClearInventory(
            ClearInventoryHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (!TryPrepare(body, out var actor, out var correlation, out var error))
                return error!;
            try
            {
                var result = await clearInventory.ExecuteAsync(
                    new ClearInventoryRequest(
                        actor!,
                        body!.Target!.ToTargetStamp(),
                        RequireText(body.ClientRequestKey),
                        correlation!,
                        body.DangerConfirmed),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    StatusCode(result.Status == ClearInventoryOperationStatus.Pending),
                    new ClearInventoryHttpResponse(
                        result,
                        CorrelationFor(result.OperationId, correlation!)));
            }
            catch (ClearInventoryIdempotencyConflictException)
            {
                return Conflict("player_action_idempotency_conflict");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpPost]
        [Route("reset-player-data")]
        [ResponseType(typeof(ResetPlayerDataHttpResponse))]
        public async Task<HttpResponseMessage> ResetPlayerData(
            ResetPlayerDataHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (!TryPrepare(body, out var actor, out var correlation, out var error))
                return error!;
            try
            {
                var result = await resetPlayerData.ExecuteAsync(
                    new ResetPlayerDataRequest(
                        actor!,
                        body!.Target!.ToTargetStamp(),
                        RequireText(body.ClientRequestKey),
                        correlation!,
                        body.DangerConfirmed),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    StatusCode(result.Status == ResetPlayerDataOperationStatus.Pending),
                    new ResetPlayerDataHttpResponse(
                        result,
                        CorrelationFor(result.OperationId, correlation!)));
            }
            catch (ResetPlayerDataIdempotencyConflictException)
            {
                return Conflict("player_action_idempotency_conflict");
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        [HttpGet]
        [Route("{operationId}")]
        [ResponseType(typeof(PlayerActionOperationHttpResponse))]
        public HttpResponseMessage Get(string operationId)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(operationId))
                return InvalidRequest();
            try
            {
                var operation = operations.Get(operationId);
                if (operation == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "player_action_not_found",
                        "The player action operation was not found.");
                }
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new PlayerActionOperationHttpResponse(operation));
            }
            catch (ArgumentException)
            {
                return InvalidRequest();
            }
            catch (Exception)
            {
                return Unavailable();
            }
        }

        private bool TryPrepare(
            object? body,
            out string? actor,
            out string? correlation,
            out HttpResponseMessage? error)
        {
            actor = null;
            correlation = null;
            error = null;
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
            {
                error = Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "game_not_ready",
                    "The game is not ready for player actions.");
                return false;
            }
            if (!ModelState.IsValid || body == null)
            {
                error = ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
                return false;
            }

            var identity = User?.Identity as ClaimsIdentity;
            actor = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(actor))
            {
                error = Problem(
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required for player actions.");
                return false;
            }
            correlation = ApiProblemDetailsFactory.GetTraceId(Request);
            return true;
        }

        private HttpResponseMessage GrantRejected(string code)
        {
            if (code == GrantItemFailureCodes.CatalogUnavailable)
                return Unavailable("catalog_unavailable");
            if (code == GrantItemFailureCodes.ResourceNotFound)
                return Problem(HttpStatusCode.NotFound, "resource_not_found", "The resource was not found.");
            if (code == GrantItemFailureCodes.VersionUnsupported ||
                code == GrantItemFailureCodes.QualityUnsupported)
            {
                return Problem(
                    (HttpStatusCode)422,
                    ToProblemCode(code),
                    "The submitted resource is not supported by the current game version.");
            }
            return Conflict(ToProblemCode(code));
        }

        private string CorrelationFor(string operationId, string fallback)
        {
            try
            {
                return operations.Get(operationId)?.CorrelationId ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static HttpStatusCode StatusCode(PlayerActionStatus status) =>
            StatusCode(status == PlayerActionStatus.Pending);

        private static HttpStatusCode StatusCode(bool pending) =>
            pending ? HttpStatusCode.Accepted : HttpStatusCode.OK;

        private static string RequireText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.");
            return value!;
        }

        private static string ToProblemCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "player_action_conflict";
            var result = new System.Text.StringBuilder(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsUpper(character) && index > 0) result.Append('_');
                result.Append(char.ToLowerInvariant(character));
            }
            return result.ToString();
        }

        private HttpResponseMessage InvalidRequest() => Problem(
            HttpStatusCode.BadRequest,
            "invalid_player_action_request",
            "The player action request is invalid.");

        private HttpResponseMessage Conflict(string code) => Problem(
            HttpStatusCode.Conflict,
            code,
            "The player action conflicts with the current player or game state.");

        private HttpResponseMessage Unavailable(string code = "player_actions_unavailable") => Problem(
            HttpStatusCode.ServiceUnavailable,
            code,
            "Player actions are temporarily unavailable.");

        private HttpResponseMessage Problem(
            HttpStatusCode status,
            string code,
            string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
