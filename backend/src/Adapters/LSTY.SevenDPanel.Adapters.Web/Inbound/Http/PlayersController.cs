using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner")]
    [RoutePrefix("api/v1/players")]
    public sealed class PlayersController : ApiController
    {
        private readonly GetOnlinePlayersUseCase useCase;
        private readonly KickPlayerUseCase kickPlayerUseCase;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public PlayersController(
            GetOnlinePlayersUseCase useCase,
            KickPlayerUseCase kickPlayerUseCase,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            this.kickPlayerUseCase = kickPlayerUseCase ?? throw new ArgumentNullException(nameof(kickPlayerUseCase));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet]
        [Route("online")]
        public async System.Threading.Tasks.Task<HttpResponseMessage> Get(CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "game_not_ready",
                    "The game is not ready to provide online players.");
            }

            var snapshot = await useCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            var players = snapshot.Players
                .OrderBy(player => player.EntityId)
                .Select(ToResponse)
                .ToArray();
            return Request.CreateResponse(
                HttpStatusCode.OK,
                new OnlinePlayersResponse(players));
        }

        [HttpPost]
        [Route("{entityId:int}/kick")]
        public async Task<HttpResponseMessage> Kick(
            int entityId,
            KickPlayerRequestBody? body,
            CancellationToken cancellationToken)
        {
            var identity = User?.Identity as ClaimsIdentity;
            var actorSubject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(actorSubject))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_required",
                    "Authentication is required to kick a player.");
            }

            if (runtimeStatus.GameReadiness != GameReadinessState.Ready)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "game_not_ready",
                    "The game is not ready to kick players.");
            }

            if (entityId < 0 || body?.ExpectedPlatformIdentity == null ||
                string.IsNullOrWhiteSpace(body.ExpectedPlatformIdentity.CombinedId) ||
                string.IsNullOrWhiteSpace(body.ExpectedPlatformIdentity.Platform))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "invalid_player_identity",
                    "A valid player identity is required.");
            }

            try
            {
                var result = await kickPlayerUseCase.ExecuteAsync(
                    new KickPlayerRequest(
                        actorSubject!,
                        entityId,
                        new PlayerPlatformIdentity(
                            body.ExpectedPlatformIdentity.CombinedId!,
                            body.ExpectedPlatformIdentity.Platform!),
                        body.Reason ?? string.Empty,
                        body.Confirmed),
                    cancellationToken).ConfigureAwait(false);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new KickPlayerResponse(result));
            }
            catch (PlayerKickConfirmationRequiredException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "player_kick_confirmation_required",
                    "Player kick confirmation is required.");
            }
            catch (InvalidPlayerKickReasonException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_player_kick_reason",
                    "The player kick reason must contain between 1 and 200 characters.");
            }
            catch (InvalidPlayerIdentityException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_player_identity",
                    "A valid player identity is required.");
            }
            catch (PlayerNotOnlineException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "player_not_online",
                    "The player is no longer online.");
            }
            catch (PlayerIdentityChangedException)
            {
                return Problem(
                    HttpStatusCode.Conflict,
                    "player_identity_changed",
                    "The player identity has changed.");
            }
            catch (PlayerActionBusyException)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "player_action_busy",
                    "Another player kick is already in progress.");
            }
            catch (TimeoutException)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "game_thread_timeout",
                    "The game thread did not start the player kick before the deadline.");
            }
            catch (AuditUnavailableException)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "audit_unavailable",
                    "The player action audit trail is unavailable.");
            }
            catch (AuditCompletionUnavailableException)
            {
                return Problem(
                    HttpStatusCode.ServiceUnavailable,
                    "audit_completion_unavailable",
                    "The player action result could not be confirmed in the audit trail.");
            }
            catch (PlayerKickFailedException)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "player_kick_failed",
                    "The player kick failed.");
            }
        }

        private HttpResponseMessage Problem(
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            return ApiProblemDetailsFactory.CreateResponse(
                Request,
                statusCode,
                code,
                detail);
        }

        private static OnlinePlayerResponse ToResponse(PlayerSnapshot player)
        {
            return new OnlinePlayerResponse(
                player.EntityId,
                player.Name,
                ToIdentityResponse(player.PlatformIdentity),
                player.CrossplatformIdentity == null ? null : ToIdentityResponse(player.CrossplatformIdentity),
                player.Ping,
                player.Level,
                player.Health,
                player.ObservedAtUtc);
        }

        private static OnlinePlayerPlatformIdentityResponse ToIdentityResponse(PlayerPlatformIdentity identity)
        {
            return new OnlinePlayerPlatformIdentityResponse(identity.CombinedId, identity.Platform);
        }
    }

    public sealed class OnlinePlayersResponse
    {
        public OnlinePlayersResponse(IReadOnlyList<OnlinePlayerResponse> players)
        {
            Players = players;
        }

        public IReadOnlyList<OnlinePlayerResponse> Players { get; }
    }

    public sealed class OnlinePlayerResponse
    {
        public OnlinePlayerResponse(
            int entityId,
            string name,
            OnlinePlayerPlatformIdentityResponse platformIdentity,
            OnlinePlayerPlatformIdentityResponse? crossplatformIdentity,
            int ping,
            int level,
            int health,
            DateTimeOffset observedAtUtc)
        {
            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
            CrossplatformIdentity = crossplatformIdentity;
            Ping = ping;
            Level = level;
            Health = health;
            ObservedAtUtc = observedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        public int EntityId { get; }

        public string Name { get; }

        public OnlinePlayerPlatformIdentityResponse PlatformIdentity { get; }

        public OnlinePlayerPlatformIdentityResponse? CrossplatformIdentity { get; }

        public int Ping { get; }

        public int Level { get; }

        public int Health { get; }

        public string ObservedAtUtc { get; }
    }

    public sealed class OnlinePlayerPlatformIdentityResponse
    {
        public OnlinePlayerPlatformIdentityResponse(string combinedId, string platform)
        {
            CombinedId = combinedId;
            Platform = platform;
        }

        public string CombinedId { get; }

        public string Platform { get; }
    }
}
