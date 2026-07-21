using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
        private readonly IPanelRuntimeStatus runtimeStatus;

        public PlayersController(
            GetOnlinePlayersUseCase useCase,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
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

            try
            {
                var snapshot = await useCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                var players = snapshot.Players
                    .OrderBy(player => player.EntityId)
                    .Select(ToResponse)
                    .ToArray();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new OnlinePlayersResponse(snapshot.CapturedAtUtc, players));
            }
            catch (OnlinePlayerQueryBusyException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "online_player_query_busy",
                    "Another online player query is already in progress.");
            }
            catch (TimeoutException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "game_thread_timeout",
                    "The game thread did not start the online player query before the deadline.");
            }
            catch (OnlinePlayerSnapshotUnavailableException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "online_player_snapshot_unavailable",
                    "The online player snapshot is currently unavailable.");
            }
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
                player.Health);
        }

        private static OnlinePlayerPlatformIdentityResponse ToIdentityResponse(PlayerPlatformIdentity identity)
        {
            return new OnlinePlayerPlatformIdentityResponse(identity.CombinedId, identity.Platform);
        }
    }

    public sealed class OnlinePlayersResponse
    {
        public OnlinePlayersResponse(DateTimeOffset capturedAtUtc, IReadOnlyList<OnlinePlayerResponse> players)
        {
            CapturedAtUtc = capturedAtUtc.ToString("O", CultureInfo.InvariantCulture);
            Players = players;
        }

        public string CapturedAtUtc { get; }

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
            int health)
        {
            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
            CrossplatformIdentity = crossplatformIdentity;
            Ping = ping;
            Level = level;
            Health = health;
        }

        public int EntityId { get; }

        public string Name { get; }

        public OnlinePlayerPlatformIdentityResponse PlatformIdentity { get; }

        public OnlinePlayerPlatformIdentityResponse? CrossplatformIdentity { get; }

        public int Ping { get; }

        public int Level { get; }

        public int Health { get; }
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
