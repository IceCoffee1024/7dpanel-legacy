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
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class OwnerAuthorizeAttribute : AuthorizeAttribute
    {
        public OwnerAuthorizeAttribute()
        {
            Roles = "Owner";
        }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (actionContext.RequestContext.Principal?.Identity?.IsAuthenticated == true)
            {
                actionContext.Response = ApiProblemDetailsFactory.CreateResponse(
                    actionContext.Request,
                    HttpStatusCode.Forbidden,
                    "owner_required",
                    "Owner access is required.");
                return;
            }

            base.HandleUnauthorizedRequest(actionContext);
        }
    }

    [OwnerAuthorize]
    [RoutePrefix("api/v1/players")]
    public sealed class PlayersController : ApiController
    {
        private readonly GetOnlinePlayersUseCase useCase;
        private readonly KickPlayerUseCase kickPlayerUseCase;
        private readonly GetHistoricalPlayersUseCase historicalPlayersUseCase;
        private readonly GetHistoricalPlayerUseCase historicalPlayerUseCase;
        private readonly GetPlayerHistorySnapshotsUseCase historicalPlayerSnapshotsUseCase;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public PlayersController(
            GetOnlinePlayersUseCase useCase,
            KickPlayerUseCase kickPlayerUseCase,
            GetHistoricalPlayersUseCase historicalPlayersUseCase,
            GetHistoricalPlayerUseCase historicalPlayerUseCase,
            GetPlayerHistorySnapshotsUseCase historicalPlayerSnapshotsUseCase,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
            this.kickPlayerUseCase = kickPlayerUseCase ?? throw new ArgumentNullException(nameof(kickPlayerUseCase));
            this.historicalPlayersUseCase = historicalPlayersUseCase ??
                throw new ArgumentNullException(nameof(historicalPlayersUseCase));
            this.historicalPlayerUseCase = historicalPlayerUseCase ??
                throw new ArgumentNullException(nameof(historicalPlayerUseCase));
            this.historicalPlayerSnapshotsUseCase = historicalPlayerSnapshotsUseCase ??
                throw new ArgumentNullException(nameof(historicalPlayerSnapshotsUseCase));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet]
        [Route("online")]
        [ResponseType(typeof(OnlinePlayersResponse))]
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

        [HttpGet]
        [Route("history")]
        [ResponseType(typeof(HistoricalPlayersResponse))]
        public HttpResponseMessage GetHistoricalPlayers(
            string? query = null,
            int? pageSize = null,
            string? cursor = null)
        {
            if (!ModelState.IsValid)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_history_query",
                    "The historical player query is invalid.");
            }

            HistoricalPlayersCursor? decodedCursor = null;
            if (cursor != null && !PlayerHistoryCursorCodec.TryDecode(cursor, out decodedCursor))
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_history_cursor",
                    "The historical player cursor is invalid.");
            }

            try
            {
                var page = historicalPlayersUseCase.Execute(new HistoricalPlayersQuery(
                    query,
                    pageSize ?? HistoricalPlayersQuery.DefaultPageSize,
                    decodedCursor));
                var players = page.Players
                    .Select(player => new HistoricalPlayerSummaryResponse(player))
                    .ToArray();
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new HistoricalPlayersResponse(
                        players,
                        page.NextCursor == null ? null : PlayerHistoryCursorCodec.Encode(page.NextCursor)));
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_history_query",
                    "The historical player query is invalid.");
            }
            catch (Exception)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "historical_player_query_failed",
                    "Historical player data could not be read.");
            }
        }

        [HttpGet]
        [Route("history/{crossplatformId}")]
        [ResponseType(typeof(HistoricalPlayerDetailsResponse))]
        public HttpResponseMessage GetHistoricalPlayer(string crossplatformId)
        {
            if (!ModelState.IsValid)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_crossplatform_id",
                    "A valid cross-platform identity is required.");
            }

            try
            {
                var details = historicalPlayerUseCase.Execute(crossplatformId);
                if (details == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "historical_player_not_found",
                        "The historical player was not found.");
                }

                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new HistoricalPlayerDetailsResponse(
                        new HistoricalPlayerSummaryResponse(details.Player),
                        new HistoricalPlayerGapSummaryResponse(details.GapSummary)));
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_crossplatform_id",
                    "A valid cross-platform identity is required.");
            }
            catch (Exception)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "historical_player_query_failed",
                    "Historical player data could not be read.");
            }
        }

        [HttpGet]
        [Route("history/{crossplatformId}/snapshots")]
        [ResponseType(typeof(HistoricalPlayerSnapshotsResponse))]
        public HttpResponseMessage GetHistoricalPlayerSnapshots(
            string crossplatformId,
            int? pageSize = null,
            long? beforeSnapshotId = null)
        {
            if (!ModelState.IsValid)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_history_snapshot_query",
                    "The historical player snapshot query is invalid.");
            }

            try
            {
                var query = new PlayerHistorySnapshotsQuery(
                    crossplatformId,
                    pageSize ?? PlayerHistorySnapshotsQuery.DefaultPageSize,
                    beforeSnapshotId);
                if (historicalPlayerUseCase.Execute(crossplatformId) == null)
                {
                    return Problem(
                        HttpStatusCode.NotFound,
                        "historical_player_not_found",
                        "The historical player was not found.");
                }

                var page = historicalPlayerSnapshotsUseCase.Execute(query);
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new HistoricalPlayerSnapshotsResponse(
                        page.Snapshots
                            .Select(snapshot => new HistoricalPlayerSnapshotResponse(snapshot))
                            .ToArray(),
                        page.NextBeforeSnapshotId,
                        page.Gaps.Select(gap => new PlayerHistoryGapResponse(gap)).ToArray()));
            }
            catch (ArgumentException)
            {
                return Problem(
                    HttpStatusCode.BadRequest,
                    "invalid_history_snapshot_query",
                    "The historical player snapshot query is invalid.");
            }
            catch (Exception)
            {
                return Problem(
                    HttpStatusCode.InternalServerError,
                    "historical_player_query_failed",
                    "Historical player data could not be read.");
            }
        }

        [HttpPost]
        [Route("{entityId:int}/kick")]
        [ResponseType(typeof(KickPlayerResponse))]
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

            if (!ModelState.IsValid)
            {
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
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
                ToDeviceType(player.DeviceType),
                player.Ip,
                player.Ping,
                player.CompatibilityVersion,
                player.DiscordUserId,
                player.PermissionLevel,
                new OnlinePlayerPositionResponse(
                    player.Position.X,
                    player.Position.Y,
                    player.Position.Z),
                player.IsDead,
                player.Health,
                player.MaxHealth,
                player.Level,
                player.PlayGroup,
                player.LastLoginUtc?.ToString("O", CultureInfo.InvariantCulture),
                player.GameStage,
                player.ExpToNextLevel,
                player.SkillPoints,
                player.Bedroll == null
                    ? null
                    : new OnlinePlayerPositionResponse(
                        player.Bedroll.Value.X,
                        player.Bedroll.Value.Y,
                        player.Bedroll.Value.Z),
                player.Score,
                player.ZombieKills,
                player.PlayerKills,
                player.Deaths,
                player.TotalTimePlayedMinutes,
                player.DistanceWalkedMeters,
                player.TotalItemsCrafted,
                player.LongestLifeMinutes,
                player.CurrentLifeMinutes,
                player.ObservedAtUtc);
        }

        private static OnlinePlayerPlatformIdentityResponse ToIdentityResponse(PlayerPlatformIdentity identity)
        {
            return new OnlinePlayerPlatformIdentityResponse(identity.CombinedId, identity.Platform);
        }

        private static string ToDeviceType(PlayerDeviceType deviceType)
        {
            switch (deviceType)
            {
                case PlayerDeviceType.Linux: return "linux";
                case PlayerDeviceType.Mac: return "mac";
                case PlayerDeviceType.Windows: return "windows";
                case PlayerDeviceType.PlayStation: return "playStation";
                case PlayerDeviceType.Xbox: return "xbox";
                default: return "unknown";
            }
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
            string deviceType,
            string? ip,
            int ping,
            string? compatibilityVersion,
            string? discordUserId,
            int permissionLevel,
            OnlinePlayerPositionResponse position,
            bool isDead,
            int health,
            int maxHealth,
            int level,
            string? playGroup,
            string? lastLoginUtc,
            int? gameStage,
            int? expToNextLevel,
            int? skillPoints,
            OnlinePlayerPositionResponse? bedroll,
            int score,
            int zombieKills,
            int playerKills,
            int deaths,
            float totalTimePlayedMinutes,
            float distanceWalkedMeters,
            uint totalItemsCrafted,
            float longestLifeMinutes,
            float currentLifeMinutes,
            DateTimeOffset observedAtUtc)
        {
            EntityId = entityId;
            Name = name;
            PlatformIdentity = platformIdentity;
            CrossplatformIdentity = crossplatformIdentity;
            DeviceType = deviceType;
            Ip = ip;
            Ping = ping;
            CompatibilityVersion = compatibilityVersion;
            DiscordUserId = discordUserId;
            PermissionLevel = permissionLevel;
            Position = position;
            IsDead = isDead;
            Health = health;
            MaxHealth = maxHealth;
            Level = level;
            PlayGroup = playGroup;
            LastLoginUtc = lastLoginUtc;
            GameStage = gameStage;
            ExpToNextLevel = expToNextLevel;
            SkillPoints = skillPoints;
            Bedroll = bedroll;
            Score = score;
            ZombieKills = zombieKills;
            PlayerKills = playerKills;
            Deaths = deaths;
            TotalTimePlayedMinutes = totalTimePlayedMinutes;
            DistanceWalkedMeters = distanceWalkedMeters;
            TotalItemsCrafted = totalItemsCrafted;
            LongestLifeMinutes = longestLifeMinutes;
            CurrentLifeMinutes = currentLifeMinutes;
            ObservedAtUtc = observedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        }

        public int EntityId { get; }

        public string Name { get; }

        public OnlinePlayerPlatformIdentityResponse PlatformIdentity { get; }

        public OnlinePlayerPlatformIdentityResponse? CrossplatformIdentity { get; }

        public string DeviceType { get; }

        public string? Ip { get; }

        public int Ping { get; }

        public string? CompatibilityVersion { get; }

        public string? DiscordUserId { get; }

        public int PermissionLevel { get; }

        public OnlinePlayerPositionResponse Position { get; }

        public bool IsDead { get; }

        public int Health { get; }

        public int MaxHealth { get; }

        public int Level { get; }

        public string? PlayGroup { get; }

        public string? LastLoginUtc { get; }

        public int? GameStage { get; }

        public int? ExpToNextLevel { get; }

        public int? SkillPoints { get; }

        public OnlinePlayerPositionResponse? Bedroll { get; }

        public int Score { get; }

        public int ZombieKills { get; }

        public int PlayerKills { get; }

        public int Deaths { get; }

        public float TotalTimePlayedMinutes { get; }

        public float DistanceWalkedMeters { get; }

        public uint TotalItemsCrafted { get; }

        public float LongestLifeMinutes { get; }

        public float CurrentLifeMinutes { get; }

        public string ObservedAtUtc { get; }
    }

    public sealed class OnlinePlayerPositionResponse
    {
        public OnlinePlayerPositionResponse(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }
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
