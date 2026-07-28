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
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Domain.Community;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/community")]
    public sealed class CommunityController : ApiController
    {
        private static readonly TeleportKind[] TeleportKinds =
        {
            TeleportKind.Home,
            TeleportKind.City,
            TeleportKind.Friend,
            TeleportKind.Return,
            TeleportKind.Admin
        };

        private static readonly VoteKind[] VoteKinds =
        {
            VoteKind.Kick,
            VoteKind.Restart
        };

        private readonly ICommunityStore store;
        private readonly IVoteStore voteStore;
        private readonly HomeUseCases homes;
        private readonly CityUseCases cities;
        private readonly FriendUseCases friends;
        private readonly TeleportUseCases teleports;
        private readonly StartVoteUseCase startVote;
        private readonly CastVoteUseCase castVote;
        private readonly SettleVoteUseCase settleVote;
        private readonly DispatchVoteActionUseCase dispatchVote;
        private readonly IPanelRuntimeStatus runtimeStatus;

        public CommunityController(
            ICommunityStore store,
            IVoteStore voteStore,
            HomeUseCases homes,
            CityUseCases cities,
            FriendUseCases friends,
            TeleportUseCases teleports,
            StartVoteUseCase startVote,
            CastVoteUseCase castVote,
            SettleVoteUseCase settleVote,
            DispatchVoteActionUseCase dispatchVote,
            IPanelRuntimeStatus runtimeStatus)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.voteStore = voteStore ?? throw new ArgumentNullException(nameof(voteStore));
            this.homes = homes ?? throw new ArgumentNullException(nameof(homes));
            this.cities = cities ?? throw new ArgumentNullException(nameof(cities));
            this.friends = friends ?? throw new ArgumentNullException(nameof(friends));
            this.teleports = teleports ?? throw new ArgumentNullException(nameof(teleports));
            this.startVote = startVote ?? throw new ArgumentNullException(nameof(startVote));
            this.castVote = castVote ?? throw new ArgumentNullException(nameof(castVote));
            this.settleVote = settleVote ?? throw new ArgumentNullException(nameof(settleVote));
            this.dispatchVote = dispatchVote ?? throw new ArgumentNullException(nameof(dispatchVote));
            this.runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
        }

        [HttpGet, Route("teleport-settings")]
        [ResponseType(typeof(TeleportSettingsHttpResponse[]))]
        public HttpResponseMessage GetTeleportSettings()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    TeleportKinds
                        .Select(kind => new TeleportSettingsHttpResponse(store.GetTeleportSettings(kind)))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(
                    exception,
                    "community_teleport_settings_unavailable",
                    "Teleport settings are unavailable.");
            }
        }

        [HttpGet, Route("teleport-settings/{kind}")]
        [ResponseType(typeof(TeleportSettingsHttpResponse))]
        public HttpResponseMessage GetTeleportSetting(string kind)
        {
            if (!TryParseTeleportKind(kind, out var parsed)) return InvalidKind("teleport");
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new TeleportSettingsHttpResponse(store.GetTeleportSettings(parsed)));
            }
            catch (CommunityNotFoundException)
            {
                return NotFound("community_teleport_setting_not_found", "The teleport setting was not found.");
            }
            catch (Exception exception)
            {
                return MapException(
                    exception,
                    "community_teleport_settings_unavailable",
                    "The teleport setting is unavailable.");
            }
        }

        [HttpPut, Route("teleport-settings/{kind}")]
        [ResponseType(typeof(TeleportSettingsHttpResponse))]
        public HttpResponseMessage PutTeleportSetting(
            string kind,
            TeleportSettingsUpsertHttpRequest? body)
        {
            if (!TryParseTeleportKind(kind, out var parsed)) return InvalidKind("teleport");
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (body.ExpectedRowVersion < 0 || body.MaxHomes < 0 || body.CooldownMs < 0 ||
                 body.GlobalCooldownMs < 0 || body.FeeAmount < 0 ||
                 (parsed != TeleportKind.Home && (body.MaxHomes.HasValue || body.HomeExperience != null)) ||
                 (parsed == TeleportKind.Home && body.HomeExperience == null) ||
                 body.HomeExperience?.SetFeeAmount < 0)
            {
                return Invalid("invalid_teleport_setting", "The teleport setting is invalid.");
            }

            try
            {
                var saved = store.SaveTeleportSettings(new TeleportSettings(
                    parsed,
                    body.Enabled,
                    body.MaxHomes,
                    TimeSpan.FromMilliseconds(body.CooldownMs),
                    TimeSpan.FromMilliseconds(body.GlobalCooldownMs),
                    body.DenyDuringBloodMoon,
                    body.FeeAmount,
                    UtcNow(),
                    body.ExpectedRowVersion,
                    body.HomeExperience?.ToDomain()));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new TeleportSettingsHttpResponse(saved));
            }
            catch (CommunityConflictException)
            {
                return VersionConflict();
            }
            catch (CommunityNotFoundException)
            {
                return NotFound("community_teleport_setting_not_found", "The teleport setting was not found.");
            }
            catch (ArgumentException)
            {
                return Invalid("invalid_teleport_setting", "The teleport setting is invalid.");
            }
            catch (Exception exception)
            {
                return MapException(
                    exception,
                    "community_teleport_settings_unavailable",
                    "The teleport setting could not be saved.");
            }
        }

        [HttpGet, Route("homes")]
        [ResponseType(typeof(PlayerHomeHttpResponse[]))]
        public HttpResponseMessage GetHomes(string? crossplatformId = null)
        {
            if (!HasText(crossplatformId))
                return Invalid("invalid_community_player", "A cross-platform player identifier is required.");
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    homes.List(crossplatformId!).Select(value => new PlayerHomeHttpResponse(value)).ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_homes_unavailable", "Player homes are unavailable.");
            }
        }

        [HttpGet, Route("homes/{crossplatformId}/{name}")]
        [ResponseType(typeof(PlayerHomeHttpResponse))]
        public HttpResponseMessage GetHome(string crossplatformId, string name)
        {
            if (!HasText(crossplatformId) || !HasText(name))
                return Invalid("invalid_community_home", "The player home identifier is invalid.");
            try
            {
                var home = store.FindHome(crossplatformId, name);
                return home == null
                    ? NotFound("community_home_not_found", "The player home was not found.")
                    : Request.CreateResponse(HttpStatusCode.OK, new PlayerHomeHttpResponse(home));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_homes_unavailable", "The player home is unavailable.");
            }
        }

        [HttpDelete, Route("homes/{crossplatformId}/{name}")]
        public HttpResponseMessage DeleteHome(string crossplatformId, string name)
        {
            if (!HasText(crossplatformId) || !HasText(name))
                return Invalid("invalid_community_home", "The player home identifier is invalid.");
            try
            {
                return homes.Delete(crossplatformId, name)
                    ? Request.CreateResponse(HttpStatusCode.NoContent)
                    : NotFound("community_home_not_found", "The player home was not found.");
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_homes_unavailable", "The player home could not be deleted.");
            }
        }

        [HttpGet, Route("cities")]
        [ResponseType(typeof(CityHttpResponse[]))]
        public HttpResponseMessage GetCities(bool enabledOnly = true)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    (enabledOnly ? cities.ListEnabled() : store.ListCities())
                        .Select(value => new CityHttpResponse(value))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_cities_unavailable", "Cities are unavailable.");
            }
        }

        [HttpGet, Route("cities/{name}")]
        [ResponseType(typeof(CityHttpResponse))]
        public HttpResponseMessage GetCity(string name)
        {
            if (!HasText(name)) return Invalid("invalid_community_city", "The city name is invalid.");
            try
            {
                var city = store.FindEnabledCity(name);
                return city == null
                    ? NotFound("community_city_not_found", "The enabled city was not found.")
                    : Request.CreateResponse(HttpStatusCode.OK, new CityHttpResponse(city));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_cities_unavailable", "The city is unavailable.");
            }
        }

        [HttpPut, Route("cities/{cityId}")]
        [ResponseType(typeof(CityHttpResponse))]
        public HttpResponseMessage PutCity(string cityId, CityUpsertHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!HasText(cityId) || !HasText(body.Name) || body.Description == null ||
                body.Position == null || !HasText(body.Position.WorldId))
            {
                return Invalid("invalid_community_city", "The city is invalid.");
            }
            try
            {
                var now = UtcNow();
                var saved = cities.Save(new City(
                    cityId,
                    body.Name!,
                    body.Description,
                    body.Enabled,
                    body.Position.ToDomain(),
                    body.SortOrder,
                    now,
                    now,
                    0));
                return Request.CreateResponse(HttpStatusCode.OK, new CityHttpResponse(saved));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_cities_unavailable", "The city could not be saved.");
            }
        }

        [HttpGet, Route("friendships")]
        [ResponseType(typeof(FriendshipStatusHttpResponse))]
        public HttpResponseMessage GetFriendship(
            string? firstCrossplatformId = null,
            string? secondCrossplatformId = null)
        {
            if (!HasText(firstCrossplatformId) || !HasText(secondCrossplatformId))
                return Invalid("invalid_community_friendship", "Two player identifiers are required.");
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new FriendshipStatusHttpResponse(
                        firstCrossplatformId!,
                        secondCrossplatformId!,
                        store.AreFriends(firstCrossplatformId!, secondCrossplatformId!)));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_friendships_unavailable", "The friendship is unavailable.");
            }
        }

        [HttpGet, Route("friendships/records")]
        [ResponseType(typeof(FriendshipHttpResponse[]))]
        public HttpResponseMessage GetFriendshipRecords()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    store.ListFriendships()
                        .Select(value => new FriendshipHttpResponse(value))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_friendships_unavailable", "Friendships are unavailable.");
            }
        }

        [HttpPost, Route("friendships/requests")]
        [ResponseType(typeof(FriendRequestHttpResponse))]
        public HttpResponseMessage InviteFriend(CreateFriendRequestHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            var now = UtcNow();
            if (!HasText(body.RequestId) || !HasText(body.RequesterCrossplatformId) ||
                !HasText(body.TargetCrossplatformId) || body.ExpiresAtUtc.Offset != TimeSpan.Zero ||
                body.ExpiresAtUtc <= now)
            {
                return Invalid("invalid_friend_request", "The friend request is invalid.");
            }
            try
            {
                var created = friends.Invite(new FriendRequest(
                    body.RequestId!,
                    body.RequesterCrossplatformId!,
                    body.TargetCrossplatformId!,
                    FriendRequestState.Pending,
                    null,
                    now,
                    body.ExpiresAtUtc,
                    null,
                    0));
                return Request.CreateResponse(HttpStatusCode.Created, new FriendRequestHttpResponse(created));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_friendships_unavailable", "The friend request could not be created.");
            }
        }

        [HttpPost, Route("friendships/requests/{requestId}/responses")]
        [ResponseType(typeof(FriendRequestHttpResponse))]
        public HttpResponseMessage RespondFriend(
            string requestId,
            RespondFriendRequestHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!HasText(requestId) || !HasText(body.ResponderCrossplatformId) ||
                (body.Accept && !HasText(body.FriendshipId)) ||
                (!body.Accept && HasText(body.FriendshipId)))
            {
                return Invalid("invalid_friend_response", "The friend response is invalid.");
            }
            try
            {
                var updated = friends.Respond(
                    requestId,
                    body.ResponderCrossplatformId!,
                    body.Accept,
                    body.FriendshipId,
                    UtcNow());
                return Request.CreateResponse(HttpStatusCode.OK, new FriendRequestHttpResponse(updated));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_friendships_unavailable", "The friend response could not be saved.");
            }
        }

        [HttpDelete, Route("friendships/{firstCrossplatformId}/{secondCrossplatformId}")]
        public HttpResponseMessage DeleteFriendship(
            string firstCrossplatformId,
            string secondCrossplatformId)
        {
            if (!HasText(firstCrossplatformId) || !HasText(secondCrossplatformId))
                return Invalid("invalid_community_friendship", "The friendship identifier is invalid.");
            try
            {
                return friends.Remove(firstCrossplatformId, secondCrossplatformId)
                    ? Request.CreateResponse(HttpStatusCode.NoContent)
                    : NotFound("community_friendship_not_found", "The friendship was not found.");
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_friendships_unavailable", "The friendship could not be deleted.");
            }
        }

        [HttpGet, Route("teleport-operations/{operationId}")]
        [ResponseType(typeof(TeleportOperationHttpResponse))]
        public HttpResponseMessage GetTeleportOperation(string operationId)
        {
            if (!HasText(operationId))
                return Invalid("invalid_teleport_operation", "The teleport operation identifier is invalid.");
            try
            {
                var operation = store.FindTeleportOperation(operationId);
                return operation == null
                    ? NotFound("teleport_operation_not_found", "The teleport operation was not found.")
                    : Request.CreateResponse(HttpStatusCode.OK, new TeleportOperationHttpResponse(operation));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_teleport_unavailable", "The teleport operation is unavailable.");
            }
        }

        [HttpGet, Route("teleport-operations")]
        [ResponseType(typeof(TeleportOperationHttpResponse[]))]
        public HttpResponseMessage GetTeleportOperations()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    store.ListTeleportOperations()
                        .Select(value => new TeleportOperationHttpResponse(value))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_teleport_unavailable", "Teleport operations are unavailable.");
            }
        }

        [HttpPost, Route("teleport-operations")]
        [ResponseType(typeof(TeleportOperationHttpResponse))]
        public async Task<HttpResponseMessage> CreateTeleportOperation(
            CreateTeleportOperationHttpRequest? body,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready) return GameNotReady();
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryParseTeleportKind(body.Kind, out var kind) || !IsValidTeleportRequest(body, kind))
                return Invalid("invalid_teleport_operation", "The teleport request is invalid.");
            var actorSubject = GetActorSubject();
            if (actorSubject == null)
                return Problem(HttpStatusCode.Unauthorized, "authentication_required", "Authentication is required to create a teleport.");

            try
            {
                var request = new TeleportExecutionRequest(
                    body.OperationId!,
                    body.IdempotencyKey!,
                    body.Player!.ToDomain(),
                    "PanelOwner",
                    actorSubject,
                    body.CorrelationId);
                TeleportOperation operation;
                switch (kind)
                {
                    case TeleportKind.Home:
                        operation = await teleports.TeleportHomeAsync(
                                request,
                                body.DestinationName!,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case TeleportKind.City:
                        operation = await teleports.TeleportCityAsync(
                                request,
                                body.DestinationName!,
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case TeleportKind.Friend:
                        operation = await teleports.TeleportFriendAsync(
                                request,
                                body.Target!.ToDomain(),
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case TeleportKind.Return:
                        operation = await teleports.TeleportBackAsync(request, cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case TeleportKind.Admin:
                        operation = await teleports.TeleportAdminAsync(
                                request,
                                body.Destination!.ToDomain(),
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    default:
                        return InvalidKind("teleport");
                }

                var status = operation.State == TeleportOperationState.Reserved ||
                    operation.State == TeleportOperationState.Dispatching ||
                    operation.State == TeleportOperationState.PendingReconciliation
                    ? HttpStatusCode.Accepted
                    : HttpStatusCode.OK;
                return Request.CreateResponse(status, new TeleportOperationHttpResponse(operation));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_teleport_unavailable", "The teleport service is unavailable.");
            }
        }

        [HttpGet, Route("vote-configurations")]
        [ResponseType(typeof(VoteConfigurationHttpResponse[]))]
        public HttpResponseMessage GetVoteConfigurations()
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    VoteKinds
                        .Select(kind => voteStore.GetConfiguration(kind))
                        .Where(value => value != null)
                        .Select(value => new VoteConfigurationHttpResponse(value!))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "Vote configurations are unavailable.");
            }
        }

        [HttpGet, Route("vote-configurations/{kind}")]
        [ResponseType(typeof(VoteConfigurationHttpResponse))]
        public HttpResponseMessage GetVoteConfiguration(string kind)
        {
            if (!TryParseVoteKind(kind, out var parsed)) return InvalidKind("vote");
            try
            {
                var configuration = voteStore.GetConfiguration(parsed);
                return configuration == null
                    ? NotFound("vote_configuration_not_found", "The vote configuration was not found.")
                    : Request.CreateResponse(HttpStatusCode.OK, new VoteConfigurationHttpResponse(configuration));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote configuration is unavailable.");
            }
        }

        [HttpPut, Route("vote-configurations/{kind}")]
        [ResponseType(typeof(VoteConfigurationHttpResponse))]
        public HttpResponseMessage PutVoteConfiguration(
            string kind,
            VoteConfigurationUpsertHttpRequest? body)
        {
            if (!TryParseVoteKind(kind, out var parsed)) return InvalidKind("vote");
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (body.ExpectedRowVersion < 0 || body.DurationMs <= 0 ||
                body.ThresholdPercent < 1 || body.ThresholdPercent > 100 ||
                body.MinimumParticipants < 1 || body.InitiatorMinimumOnlineMs < 0 ||
                body.ParticipantMinimumOnlineMs < 0 || body.InitiatorCooldownMs < 0 ||
                body.TargetCooldownMs < 0 || body.GlobalCooldownMs < 0 ||
                !HasText(body.MutualExclusionScope))
            {
                return Invalid("invalid_vote_configuration", "The vote configuration is invalid.");
            }

            try
            {
                var current = voteStore.GetConfiguration(parsed);
                if (current == null)
                    return NotFound("vote_configuration_not_found", "The vote configuration was not found.");
                var saved = voteStore.SaveConfiguration(new VoteConfiguration(
                    current.ConfigurationId,
                    parsed,
                    body.Enabled,
                    TimeSpan.FromMilliseconds(body.DurationMs),
                    body.ThresholdPercent,
                    body.MinimumParticipants,
                    TimeSpan.FromMilliseconds(body.InitiatorMinimumOnlineMs),
                    TimeSpan.FromMilliseconds(body.ParticipantMinimumOnlineMs),
                    TimeSpan.FromMilliseconds(body.InitiatorCooldownMs),
                    TimeSpan.FromMilliseconds(body.TargetCooldownMs),
                    TimeSpan.FromMilliseconds(body.GlobalCooldownMs),
                    body.MutualExclusionScope!,
                    body.AllowVoteChange,
                    UtcNow(),
                    body.ExpectedRowVersion));
                return Request.CreateResponse(HttpStatusCode.OK, new VoteConfigurationHttpResponse(saved));
            }
            catch (CommunityConflictException)
            {
                return VersionConflict();
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote configuration could not be saved.");
            }
        }

        [HttpGet, Route("vote-rounds")]
        [ResponseType(typeof(VoteRoundHttpResponse[]))]
        public HttpResponseMessage GetVoteRounds(bool actionQueuedOnly = false)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    (actionQueuedOnly ? voteStore.ListActionQueued() : voteStore.ListRounds())
                        .Select(value => new VoteRoundHttpResponse(value))
                        .ToArray());
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "Vote rounds are unavailable.");
            }
        }

        [HttpGet, Route("vote-rounds/{roundId}")]
        [ResponseType(typeof(VoteRoundHttpResponse))]
        public HttpResponseMessage GetVoteRound(string roundId)
        {
            if (!HasText(roundId)) return Invalid("invalid_vote_round", "The vote round identifier is invalid.");
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new VoteRoundHttpResponse(voteStore.GetRound(roundId)));
            }
            catch (VoteRoundNotFoundException)
            {
                return NotFound("vote_round_not_found", "The vote round was not found.");
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote round is unavailable.");
            }
        }

        [HttpPost, Route("vote-rounds")]
        [ResponseType(typeof(VoteStartHttpResponse))]
        public HttpResponseMessage StartVoteRound(StartVoteRoundHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!TryParseVoteKind(body.Kind, out var kind) || !HasText(body.RoundId) ||
                !HasText(body.InitiatorCrossplatformId) || !HasText(body.IdempotencyKey) ||
                body.EligiblePlayers == null || body.EligiblePlayers.Count == 0 ||
                body.EligiblePlayers.Any(value =>
                    value == null || !HasText(value.CrossplatformId) || value.OnlineDurationMs < 0))
            {
                return Invalid("invalid_vote_start", "The vote start request is invalid.");
            }

            try
            {
                var result = startVote.Execute(new StartVoteRequest(
                    body.RoundId!,
                    kind,
                    body.InitiatorCrossplatformId!,
                    body.TargetCrossplatformId,
                    body.EligiblePlayers
                        .Select(value => new VoteEligiblePlayer(
                            value.CrossplatformId!,
                            TimeSpan.FromMilliseconds(value.OnlineDurationMs)))
                        .ToArray(),
                    body.IdempotencyKey!,
                    body.CorrelationId,
                    UtcNow()));
                if (result.Status == VoteStartStatus.Started)
                    return Request.CreateResponse(HttpStatusCode.Created, new VoteStartHttpResponse(result));
                if (result.Status == VoteStartStatus.Replayed)
                    return Request.CreateResponse(HttpStatusCode.OK, new VoteStartHttpResponse(result));
                if (result.Status == VoteStartStatus.InvalidTarget)
                    return Invalid("vote_invalid_target", "The vote target is invalid.");
                return Conflict(
                    "vote_start_" + ToSnakeCase(result.Status.ToString()),
                    "The vote could not be started in the current state.");
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote could not be started.");
            }
        }

        [HttpPost, Route("vote-rounds/{roundId}/votes")]
        [ResponseType(typeof(VoteCastHttpResponse))]
        public HttpResponseMessage CastVote(string roundId, CastVoteHttpRequest? body)
        {
            if (!ModelState.IsValid || body == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            if (!HasText(roundId) || !HasText(body.CrossplatformId) ||
                !Enum.TryParse(body.Choice, true, out VoteChoice choice) ||
                !Enum.IsDefined(typeof(VoteChoice), choice))
            {
                return Invalid("invalid_vote_cast", "The vote choice is invalid.");
            }
            try
            {
                var result = castVote.Execute(roundId, body.CrossplatformId!, choice, UtcNow());
                if (result.Status == VoteCastStatus.Accepted ||
                    result.Status == VoteCastStatus.Replayed ||
                    result.Status == VoteCastStatus.Changed)
                {
                    return Request.CreateResponse(HttpStatusCode.OK, new VoteCastHttpResponse(result));
                }
                if (result.Status == VoteCastStatus.RoundNotFound ||
                    result.Status == VoteCastStatus.NoOpenRound)
                {
                    return NotFound("vote_round_not_found", "The open vote round was not found.");
                }
                return Conflict(
                    "vote_cast_" + ToSnakeCase(result.Status.ToString()),
                    "The vote could not be recorded in the current state.");
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote could not be recorded.");
            }
        }

        [HttpPost, Route("vote-rounds/{roundId}/settle")]
        [ResponseType(typeof(VoteSettlementHttpResponse))]
        public HttpResponseMessage SettleVoteRound(string roundId)
        {
            if (!HasText(roundId)) return Invalid("invalid_vote_round", "The vote round identifier is invalid.");
            try
            {
                var result = settleVote.Execute(roundId, UtcNow());
                return result.Status == VoteSettlementStatus.NotDue
                    ? Conflict("vote_not_due", "The vote round is not due for settlement.")
                    : Request.CreateResponse(HttpStatusCode.OK, new VoteSettlementHttpResponse(result));
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote round could not be settled.");
            }
        }

        [HttpPost, Route("vote-rounds/{roundId}/dispatch")]
        [ResponseType(typeof(VoteActionDispatchHttpResponse))]
        public async Task<HttpResponseMessage> DispatchVoteRound(
            string roundId,
            CancellationToken cancellationToken)
        {
            if (runtimeStatus.GameReadiness != GameReadinessState.Ready) return GameNotReady();
            if (!HasText(roundId)) return Invalid("invalid_vote_round", "The vote round identifier is invalid.");
            try
            {
                var result = await dispatchVote.ExecuteAsync(roundId, UtcNow(), cancellationToken)
                    .ConfigureAwait(false);
                return result.Status == VoteActionDispatchStatus.NotPassed
                    ? Conflict("vote_not_passed", "The vote has not passed.")
                    : Request.CreateResponse(HttpStatusCode.OK, new VoteActionDispatchHttpResponse(result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return MapException(exception, "community_votes_unavailable", "The vote action is unavailable.");
            }
        }

        private HttpResponseMessage MapException(
            Exception exception,
            string unavailableCode,
            string unavailableDetail)
        {
            if (exception is VoteRoundNotFoundException)
                return NotFound("vote_round_not_found", "The vote round was not found.");
            if (exception is CommunityNotFoundException)
                return NotFound("community_resource_not_found", "The community resource was not found.");
            if (exception is TeleportRejectedException rejected)
            {
                return string.Equals(
                    rejected.Code,
                    TeleportFailureCodes.DestinationNotFound,
                    StringComparison.Ordinal)
                    ? NotFound(rejected.Code, "The teleport destination was not found.")
                    : Conflict(rejected.Code, "The teleport was rejected by the current rules.");
            }
            if (exception is CommunityConflictException ||
                exception is CommunityLimitExceededException ||
                exception is VoteIdempotencyConflictException)
            {
                return Conflict(exception.Message, "The community state changed before the request completed.");
            }
            if (exception is ArgumentException || exception is OverflowException)
                return Invalid("invalid_community_request", "The community request is invalid.");
            return Problem(HttpStatusCode.ServiceUnavailable, unavailableCode, unavailableDetail);
        }

        private static bool IsValidTeleportRequest(
            CreateTeleportOperationHttpRequest body,
            TeleportKind kind)
        {
            if (!HasText(body.OperationId) || !HasText(body.IdempotencyKey) ||
                body.Player == null || !IsValidPlayer(body.Player))
            {
                return false;
            }
            switch (kind)
            {
                case TeleportKind.Home:
                case TeleportKind.City:
                    return HasText(body.DestinationName) && body.Target == null && body.Destination == null;
                case TeleportKind.Friend:
                    return body.Target != null && IsValidPlayer(body.Target) &&
                        body.Destination == null && !HasText(body.DestinationName);
                case TeleportKind.Return:
                    return body.Target == null && body.Destination == null && !HasText(body.DestinationName);
                case TeleportKind.Admin:
                    return body.Destination != null && HasText(body.Destination.WorldId) &&
                        body.Target == null && !HasText(body.DestinationName);
                default:
                    return false;
            }
        }

        private static bool IsValidPlayer(TeleportPlayerHttpRequest player) =>
            HasText(player.CrossplatformId) && player.EntityId >= 0 &&
            player.Position != null && HasText(player.Position.WorldId) &&
            player.WorldBounds != null;

        private static bool TryParseTeleportKind(string? value, out TeleportKind kind) =>
            Enum.TryParse(value, true, out kind) &&
            Enum.IsDefined(typeof(TeleportKind), kind) &&
            kind != TeleportKind.Global;

        private static bool TryParseVoteKind(string? value, out VoteKind kind) =>
            Enum.TryParse(value, true, out kind) && Enum.IsDefined(typeof(VoteKind), kind);

        private string? GetActorSubject()
        {
            var identity = User?.Identity as ClaimsIdentity;
            var subject = identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return HasText(subject) ? subject : null;
        }

        private static DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string ToSnakeCase(string value) =>
            string.Concat(value.Select((character, index) =>
                char.IsUpper(character) && index != 0
                    ? "_" + char.ToLowerInvariant(character)
                    : char.ToLowerInvariant(character).ToString()));

        private HttpResponseMessage InvalidKind(string kind) =>
            Invalid("invalid_community_kind", "The " + kind + " kind is invalid.");

        private HttpResponseMessage VersionConflict() =>
            Conflict(
                "community_version_conflict",
                "The community configuration changed. Refresh and try again.");

        private HttpResponseMessage GameNotReady() =>
            Problem(
                HttpStatusCode.ServiceUnavailable,
                "game_not_ready",
                "The game is not ready for the requested community action.");

        private HttpResponseMessage Invalid(string code, string detail) =>
            Problem(HttpStatusCode.BadRequest, code, detail);

        private HttpResponseMessage NotFound(string code, string detail) =>
            Problem(HttpStatusCode.NotFound, code, detail);

        private HttpResponseMessage Conflict(string code, string detail) =>
            Problem(HttpStatusCode.Conflict, code, detail);

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
