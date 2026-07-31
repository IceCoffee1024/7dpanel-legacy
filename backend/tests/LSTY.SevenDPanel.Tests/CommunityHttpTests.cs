using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.Community;
using LSTY.SevenDPanel.Application.Economy;
using LSTY.SevenDPanel.Domain.Community;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class CommunityHttpTests
    {
        private const string ControllerName =
            "LSTY.SevenDPanel.Adapters.Web.Inbound.Http.CommunityController";

        [Fact]
        public void Controller_exposes_only_closed_owner_routes()
        {
            var type = typeof(AuditController).Assembly.GetType(ControllerName);
            Assert.NotNull(type);
            var authorization = type!.GetCustomAttribute<AuthorizeAttribute>();
            Assert.IsType<OwnerAuthorizeAttribute>(authorization);
            Assert.Equal("Owner", authorization!.Roles);
            Assert.Equal("api/v1/community", type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);

            AssertRoute(type, "GetTeleportSettings", "teleport-settings", typeof(HttpGetAttribute));
            AssertRoute(type, "GetTeleportSetting", "teleport-settings/{kind}", typeof(HttpGetAttribute));
            AssertRoute(type, "PutTeleportSetting", "teleport-settings/{kind}", typeof(HttpPutAttribute));
            AssertRoute(type, "GetHomes", "homes", typeof(HttpGetAttribute));
            AssertRoute(type, "GetHome", "homes/{crossplatformId}/{name}", typeof(HttpGetAttribute));
            AssertRoute(type, "DeleteHome", "homes/{crossplatformId}/{name}", typeof(HttpDeleteAttribute));
            AssertRoute(type, "GetCities", "cities", typeof(HttpGetAttribute));
            AssertRoute(type, "GetCity", "cities/{name}", typeof(HttpGetAttribute));
            AssertRoute(type, "PutCity", "cities/{cityId}", typeof(HttpPutAttribute));
            AssertRoute(type, "GetFriendship", "friendships", typeof(HttpGetAttribute));
            AssertRoute(type, "GetFriendshipRecords", "friendships/records", typeof(HttpGetAttribute));
            AssertRoute(type, "InviteFriend", "friendships/requests", typeof(HttpPostAttribute));
            AssertRoute(type, "RespondFriend", "friendships/requests/{requestId}/responses", typeof(HttpPostAttribute));
            AssertRoute(type, "DeleteFriendship", "friendships/{firstCrossplatformId}/{secondCrossplatformId}", typeof(HttpDeleteAttribute));
            AssertRoute(type, "GetTeleportOperations", "teleport-operations", typeof(HttpGetAttribute));
            AssertRoute(type, "GetTeleportOperation", "teleport-operations/{operationId}", typeof(HttpGetAttribute));
            AssertRoute(type, "CreateTeleportOperation", "teleport-operations", typeof(HttpPostAttribute));
            AssertRoute(type, "GetVoteConfigurations", "vote-configurations", typeof(HttpGetAttribute));
            AssertRoute(type, "GetVoteConfiguration", "vote-configurations/{kind}", typeof(HttpGetAttribute));
            AssertRoute(type, "PutVoteConfiguration", "vote-configurations/{kind}", typeof(HttpPutAttribute));
            AssertRoute(type, "GetVoteRounds", "vote-rounds", typeof(HttpGetAttribute));
            AssertRoute(type, "GetVoteRound", "vote-rounds/{roundId}", typeof(HttpGetAttribute));
            AssertRoute(type, "StartVoteRound", "vote-rounds", typeof(HttpPostAttribute));
            AssertRoute(type, "CastVote", "vote-rounds/{roundId}/votes", typeof(HttpPostAttribute));
            AssertRoute(type, "SettleVoteRound", "vote-rounds/{roundId}/settle", typeof(HttpPostAttribute));
            AssertRoute(type, "DispatchVoteRound", "vote-rounds/{roundId}/dispatch", typeof(HttpPostAttribute));
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.OK, null)]
        [InlineData("Admin", HttpStatusCode.Forbidden, "owner_required")]
        [InlineData("Viewer", HttpStatusCode.Forbidden, "owner_required")]
        [InlineData(null, HttpStatusCode.Unauthorized, null)]
        public async Task Community_management_is_owner_only(
            string? role,
            HttpStatusCode expectedStatus,
            string? expectedProblemCode)
        {
            using var host = CreateHost(role);

            using var response = await host.Client.GetAsync(
                "api/v1/community/teleport-settings",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedProblemCode != null)
            {
                var problem = JObject.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(expectedProblemCode, (string?)problem["code"]);
            }
        }

        [Fact]
        public async Task Teleport_settings_are_camel_case_utc_and_optimistically_updated()
        {
            using var host = CreateHost("Owner");

            using var get = await host.Client.GetAsync(
                "api/v1/community/teleport-settings",
                TestContext.Current.CancellationToken);
            var settingsJson = await get.Content.ReadAsStringAsync();
            var settings = JArray.Parse(settingsJson);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.Equal("Home", (string?)settings[0]!["kind"]);
            Assert.Contains("\"updatedAtUtc\":\"2026-07-27T00:00:00Z\"", settingsJson);
            Assert.Null(settings[0]!["UpdatedAtUtc"]);

            var update = (JObject)settings[0]!.DeepClone();
            update.Remove("kind");
            update.Remove("updatedAtUtc");
            update.Remove("rowVersion");
            update["enabled"] = true;
            update["maxHomes"] = 3;
            update["cooldownMs"] = 1000;
            update["globalCooldownMs"] = 500;
            update["denyDuringBloodMoon"] = true;
            update["feeAmount"] = 0;
            update["expectedRowVersion"] = 9;

            using var conflict = await PutJsonAsync(
                host.Client,
                "api/v1/community/teleport-settings/home",
                update.ToString());
            var problem = JObject.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("community_version_conflict", (string?)problem["code"]);

            update["expectedRowVersion"] = 0;
            using var updated = await PutJsonAsync(
                host.Client,
                "api/v1/community/teleport-settings/home",
                update.ToString());
            var payload = JObject.Parse(await updated.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal(1, (long?)payload["rowVersion"]);
        }

        [Fact]
        public async Task Homes_cities_and_friendships_use_only_stable_store_contracts()
        {
            using var host = CreateHost("Owner");

            using var homes = await host.Client.GetAsync(
                "api/v1/community/homes?crossplatformId=EOS_1",
                TestContext.Current.CancellationToken);
            var homeItems = JArray.Parse(await homes.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, homes.StatusCode);
            Assert.Equal("base", (string?)homeItems[0]!["name"]);

            using var missing = await host.Client.GetAsync(
                "api/v1/community/homes/EOS_1/missing",
                TestContext.Current.CancellationToken);
            var missingProblem = JObject.Parse(await missing.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("community_home_not_found", (string?)missingProblem["code"]);

            using var city = await PutJsonAsync(
                host.Client,
                "api/v1/community/cities/city-2",
                "{\"name\":\"Trader\",\"description\":\"Public destination\",\"enabled\":true,\"position\":{\"worldId\":\"world-1\",\"x\":10,\"y\":70,\"z\":20,\"yaw\":90},\"sortOrder\":2}");
            Assert.Equal(HttpStatusCode.OK, city.StatusCode);

            using var invitation = await PostJsonAsync(
                host.Client,
                "api/v1/community/friendships/requests",
                "{\"requestId\":\"request-2\",\"requesterCrossplatformId\":\"EOS_1\",\"targetCrossplatformId\":\"EOS_2\",\"expiresAtUtc\":\"2099-01-01T00:00:00Z\"}");
            Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);

            using var removed = await host.Client.DeleteAsync(
                "api/v1/community/friendships/EOS_1/EOS_2",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        [Fact]
        public async Task Owner_can_query_full_community_records_in_stable_order()
        {
            using var host = CreateHost("Owner");

            using var savedCity = await PutJsonAsync(
                host.Client,
                "api/v1/community/cities/city-2",
                "{\"name\":\"Hidden\",\"description\":\"Disabled destination\",\"enabled\":false,\"position\":{\"worldId\":\"world-1\",\"x\":10,\"y\":70,\"z\":20,\"yaw\":90},\"sortOrder\":2}");
            Assert.Equal(HttpStatusCode.OK, savedCity.StatusCode);

            using var cities = await host.Client.GetAsync(
                "api/v1/community/cities?enabledOnly=false",
                TestContext.Current.CancellationToken);
            var cityItems = JArray.Parse(await cities.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, cities.StatusCode);
            Assert.Equal(new[] { "city-1", "city-2" },
                cityItems.Select(value => (string?)value!["cityId"]).ToArray());
            Assert.False((bool)cityItems[1]!["enabled"]!);

            using var friendships = await host.Client.GetAsync(
                "api/v1/community/friendships/records",
                TestContext.Current.CancellationToken);
            var friendshipItems = JArray.Parse(await friendships.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, friendships.StatusCode);
            Assert.Equal("friendship-1", (string?)friendshipItems[0]!["friendshipId"]);

            using var created = await PostJsonAsync(
                host.Client,
                "api/v1/community/teleport-operations",
                TeleportBody());
            Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
            using var operations = await host.Client.GetAsync(
                "api/v1/community/teleport-operations",
                TestContext.Current.CancellationToken);
            var operationItems = JArray.Parse(await operations.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, operations.StatusCode);
            Assert.Equal(new[] { "operation-1" },
                operationItems.Select(value => (string?)value!["operationId"]).ToArray());

            using var rounds = await host.Client.GetAsync(
                "api/v1/community/vote-rounds",
                TestContext.Current.CancellationToken);
            var roundItems = JArray.Parse(await rounds.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, rounds.StatusCode);
            Assert.Equal(new[] { "round-1" },
                roundItems.Select(value => (string?)value!["roundId"]).ToArray());
        }

        [Fact]
        public async Task Teleport_operations_are_queried_by_id_and_never_fake_unknown_delivery_as_complete()
        {
            using var unavailableHost = CreateHost("Owner", GameReadinessState.Loading);
            using var unavailable = await PostJsonAsync(
                unavailableHost.Client,
                "api/v1/community/teleport-operations",
                TeleportBody());
            var unavailableProblem = JObject.Parse(await unavailable.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("game_not_ready", (string?)unavailableProblem["code"]);

            using var host = CreateHost("Owner");
            using var accepted = await PostJsonAsync(
                host.Client,
                "api/v1/community/teleport-operations",
                TeleportBody());
            var operation = JObject.Parse(await accepted.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            Assert.Equal("PendingReconciliation", (string?)operation["state"]);
            Assert.NotEqual("Completed", (string?)operation["state"]);

            using var fetched = await host.Client.GetAsync(
                "api/v1/community/teleport-operations/operation-1",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        }

        [Fact]
        public async Task Vote_configuration_and_round_routes_keep_missing_lists_and_conflicts_explicit()
        {
            using var host = CreateHost("Owner");

            using var configurations = await host.Client.GetAsync(
                "api/v1/community/vote-configurations",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, configurations.StatusCode);

            using var conflict = await PutJsonAsync(
                host.Client,
                "api/v1/community/vote-configurations/kick",
                "{\"enabled\":true,\"durationMs\":60000,\"thresholdPercent\":60,\"minimumParticipants\":2,\"initiatorMinimumOnlineMs\":0,\"participantMinimumOnlineMs\":0,\"initiatorCooldownMs\":0,\"targetCooldownMs\":0,\"globalCooldownMs\":0,\"mutualExclusionScope\":\"global\",\"allowVoteChange\":true,\"expectedRowVersion\":8}");
            var conflictProblem = JObject.Parse(await conflict.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Equal("community_version_conflict", (string?)conflictProblem["code"]);

            using var rounds = await host.Client.GetAsync(
                "api/v1/community/vote-rounds",
                TestContext.Current.CancellationToken);
            var roundItems = JArray.Parse(await rounds.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, rounds.StatusCode);
            Assert.Equal(new[] { "round-1" },
                roundItems.Select(value => (string?)value!["roundId"]).ToArray());

            using var round = await host.Client.GetAsync(
                "api/v1/community/vote-rounds/round-1",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, round.StatusCode);
        }

        private static void AssertRoute(
            Type controllerType,
            string methodName,
            string template,
            Type verbAttribute)
        {
            var method = controllerType.GetMethod(methodName);
            Assert.NotNull(method);
            Assert.Equal(template, method!.GetCustomAttribute<RouteAttribute>()?.Template);
            Assert.NotNull(method.GetCustomAttribute(verbAttribute));
        }

        private static HttpTestHost CreateHost(
            string? role,
            GameReadinessState readiness = GameReadinessState.Ready)
        {
            var store = new MemoryCommunityStore();
            var services = new ServiceCollection();
            services.AddSingleton<ICommunityStore>(store);
            services.AddSingleton<IVoteStore>(store);
            services.AddSingleton<IEconomyLedgerStore, UnusedLedger>();
            services.AddSingleton<ICommunityGameGateway, UnknownTeleportGateway>();
            services.AddSingleton<ICommunityVoteActionPort, SuccessfulVoteActionPort>();
            services.AddSingleton<IPanelRuntimeStatus>(new StubRuntimeStatus(readiness));
            services.AddSingleton<Func<DateTimeOffset>>(() => FixedNow);
            services.AddSingleton(new GameChatCommandCatalog(Array.Empty<IGameChatCommandHandler>()));
            services.AddSingleton<Func<IEnumerable<IGameChatCommandHandler>>>(
                _ => () => Array.Empty<IGameChatCommandHandler>());
            services.AddSingleton<GameChatCommandRegistrationService>();
            services.AddSingleton<HomeUseCases>();
            services.AddSingleton<CityUseCases>();
            services.AddSingleton<FriendUseCases>();
            services.AddSingleton<TeleportUseCases>();
            services.AddSingleton<StartVoteUseCase>();
            services.AddSingleton<CastVoteUseCase>();
            services.AddSingleton<SettleVoteUseCase>();
            services.AddSingleton<DispatchVoteActionUseCase>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        private static string TeleportBody() =>
            "{\"operationId\":\"operation-1\",\"idempotencyKey\":\"teleport-1\",\"kind\":\"Admin\",\"player\":{\"crossplatformId\":\"EOS_1\",\"entityId\":1,\"position\":{\"worldId\":\"world-1\",\"x\":0,\"y\":70,\"z\":0,\"yaw\":0},\"isOnline\":true,\"isAlive\":true,\"isSpawned\":true,\"isBloodMoon\":false,\"allowsFriendTeleport\":true,\"worldBounds\":{\"minimumX\":-1000,\"maximumX\":1000,\"minimumZ\":-1000,\"maximumZ\":1000}},\"destination\":{\"worldId\":\"world-1\",\"x\":10,\"y\":70,\"z\":20,\"yaw\":0},\"actorId\":\"owner-1\"}";

        private static Task<HttpResponseMessage> PutJsonAsync(
            HttpClient client,
            string path,
            string json) =>
            client.PutAsync(path, Json(json), TestContext.Current.CancellationToken);

        private static Task<HttpResponseMessage> PostJsonAsync(
            HttpClient client,
            string path,
            string json) =>
            client.PostAsync(path, Json(json), TestContext.Current.CancellationToken);

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public HttpTestHost(ServiceProvider provider, HttpConfiguration configuration)
            {
                this.provider = provider;
                this.configuration = configuration;
                Client = new HttpClient(new HttpServer(configuration))
                {
                    BaseAddress = new Uri("http://localhost/")
                };
            }

            public HttpClient Client { get; }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;
            public PrincipalHandler(string? role) => this.role = role;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "owner-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                owin.Environment["owin.CallCancelled"] = cancellationToken;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        private sealed class StubRuntimeStatus : IPanelRuntimeStatus
        {
            public StubRuntimeStatus(GameReadinessState readiness) => GameReadiness = readiness;
            public ModHostState State => default;
            public GameReadinessState GameReadiness { get; }
        }

        private sealed class UnknownTeleportGateway : ICommunityGameGateway
        {
            public Task<TeleportActionResult> TeleportAsync(
                TeleportActionCommand command,
                CancellationToken cancellationToken) =>
                Task.FromResult(TeleportActionResult.ResultUnknown());
        }

        private sealed class SuccessfulVoteActionPort : ICommunityVoteActionPort
        {
            public Task<VoteActionResult> ExecuteAsync(
                VoteActionCommand command,
                CancellationToken cancellationToken) =>
                Task.FromResult(VoteActionResult.Succeeded("operation:vote", null));
        }

        private sealed class UnusedLedger : IEconomyLedgerStore
        {
            public AccountSnapshot GetOrCreatePlayerAccount(string crossplatformId, string idempotencyKey, long openingAmount, DateTimeOffset occurredAtUtc) => throw new NotSupportedException();
            public LedgerWriteResult Commit(LedgerTransactionDraft transaction) => throw new NotSupportedException();
            public FundsReservationResult TryReserve(FundsReservationDraft reservation) => throw new NotSupportedException();
            public LedgerWriteResult Capture(string reservationId, string transactionId, string idempotencyKey, DateTimeOffset occurredAtUtc) => throw new NotSupportedException();
            public bool Release(string reservationId, DateTimeOffset occurredAtUtc) => true;
            public AccountPage QueryAccounts(AccountKeysetQuery query) => throw new NotSupportedException();
            public TransactionPage QueryTransactions(TransactionKeysetQuery query) => throw new NotSupportedException();
        }

        private sealed class MemoryCommunityStore : ICommunityStore, IVoteStore
        {
            private readonly Dictionary<TeleportKind, TeleportSettings> settings;
            private readonly List<PlayerHome> homes;
            private readonly List<City> cities;
            private readonly List<Friendship> friendships;
            private readonly Dictionary<string, TeleportOperation> operations =
                new Dictionary<string, TeleportOperation>(StringComparer.Ordinal);
            private readonly Dictionary<VoteKind, VoteConfiguration> configurations;
            private readonly Dictionary<string, VoteRoundSnapshot> rounds =
                new Dictionary<string, VoteRoundSnapshot>(StringComparer.Ordinal);

            public MemoryCommunityStore()
            {
                settings = new[]
                {
                    Setting(TeleportKind.Home, 3),
                    Setting(TeleportKind.City, null),
                    Setting(TeleportKind.Friend, null),
                    Setting(TeleportKind.Return, null),
                    Setting(TeleportKind.Admin, null)
                }.ToDictionary(value => value.Kind);
                homes = new List<PlayerHome>
                {
                    new PlayerHome(
                        "home-1",
                        "EOS_1",
                        "base",
                        Position(1, 70, 2),
                        FixedNow.AddDays(-1),
                        FixedNow,
                        0)
                };
                cities = new List<City>
                {
                    new City(
                        "city-1",
                        "Spawn",
                        "Spawn city",
                        true,
                        Position(5, 70, 5),
                        1,
                        FixedNow.AddDays(-1),
                        FixedNow,
                        0)
                };
                friendships = new List<Friendship>
                {
                    new Friendship("friendship-1", "EOS_1", "EOS_2", "EOS_1", FixedNow.AddDays(-1))
                };
                configurations = new[]
                {
                    Configuration(VoteKind.Kick),
                    Configuration(VoteKind.Restart)
                }.ToDictionary(value => value.Kind);
                rounds.Add("round-1", Round("round-1", VoteKind.Kick, VoteRoundState.Open));
            }

            public TeleportSettings GetTeleportSettings(TeleportKind kind) =>
                settings.TryGetValue(kind, out var value)
                    ? value
                    : throw new CommunityNotFoundException();

            public TeleportSettings SaveTeleportSettings(TeleportSettings value)
            {
                var current = settings[value.Kind];
                if (current.RowVersion != value.RowVersion) throw new CommunityConflictException();
                var saved = new TeleportSettings(
                    value.Kind,
                    value.Enabled,
                    value.MaxHomes,
                    value.Cooldown,
                    value.GlobalCooldown,
                    value.DenyDuringBloodMoon,
                    value.FeeAmount,
                    value.UpdatedAtUtc,
                    current.RowVersion + 1);
                settings[value.Kind] = saved;
                return saved;
            }

            public PlayerHome SaveHome(PlayerHome home, int maxHomes, long? expectedRowVersion)
            {
                homes.RemoveAll(value => string.Equals(value.HomeId, home.HomeId, StringComparison.Ordinal));
                homes.Add(home);
                return home;
            }

            public IReadOnlyList<PlayerHome> ListHomes(string crossplatformId) =>
                homes.Where(value => string.Equals(value.CrossplatformId, crossplatformId, StringComparison.Ordinal)).ToArray();

            public PlayerHome? FindHome(string crossplatformId, string name) =>
                homes.SingleOrDefault(value =>
                    string.Equals(value.CrossplatformId, crossplatformId, StringComparison.Ordinal) &&
                    string.Equals(value.Name, name, StringComparison.Ordinal));

            public bool DeleteHome(string crossplatformId, string name) =>
                homes.RemoveAll(value =>
                    string.Equals(value.CrossplatformId, crossplatformId, StringComparison.Ordinal) &&
                    string.Equals(value.Name, name, StringComparison.Ordinal)) == 1;

            public City SaveCity(City city)
            {
                cities.RemoveAll(value => string.Equals(value.CityId, city.CityId, StringComparison.Ordinal));
                cities.Add(city);
                return city;
            }

            public IReadOnlyList<City> ListCities() =>
                cities.OrderBy(value => value.SortOrder).ThenBy(value => value.CityId, StringComparer.Ordinal).ToArray();

            public IReadOnlyList<City> ListEnabledCities() =>
                cities.Where(value => value.Enabled).OrderBy(value => value.SortOrder).ToArray();

            public City? FindEnabledCity(string name) =>
                cities.SingleOrDefault(value => value.Enabled && string.Equals(value.Name, name, StringComparison.Ordinal));

            public FriendRequest CreateFriendRequest(FriendRequest request) => request;

            public FriendRequest RespondToFriendRequest(
                string requestId,
                string responderCrossplatformId,
                bool accept,
                string? friendshipId,
                DateTimeOffset respondedAtUtc) =>
                new FriendRequest(
                    requestId,
                    "EOS_1",
                    responderCrossplatformId,
                    accept ? FriendRequestState.Accepted : FriendRequestState.Rejected,
                    friendshipId,
                    FixedNow.AddHours(-1),
                    FixedNow.AddDays(1),
                    respondedAtUtc,
                    1);

            public bool AreFriends(string firstCrossplatformId, string secondCrossplatformId) =>
                friendships.Any(value =>
                    (string.Equals(value.MemberACrossplatformId, firstCrossplatformId, StringComparison.Ordinal) &&
                     string.Equals(value.MemberBCrossplatformId, secondCrossplatformId, StringComparison.Ordinal)) ||
                    (string.Equals(value.MemberACrossplatformId, secondCrossplatformId, StringComparison.Ordinal) &&
                     string.Equals(value.MemberBCrossplatformId, firstCrossplatformId, StringComparison.Ordinal)));

            public IReadOnlyList<Friendship> ListFriendships() =>
                friendships.OrderBy(value => value.AcceptedAtUtc).ThenBy(value => value.FriendshipId, StringComparer.Ordinal).ToArray();

            public bool RemoveFriendship(string firstCrossplatformId, string secondCrossplatformId)
            {
                return friendships.RemoveAll(value =>
                    (string.Equals(value.MemberACrossplatformId, firstCrossplatformId, StringComparison.Ordinal) &&
                     string.Equals(value.MemberBCrossplatformId, secondCrossplatformId, StringComparison.Ordinal)) ||
                    (string.Equals(value.MemberACrossplatformId, secondCrossplatformId, StringComparison.Ordinal) &&
                     string.Equals(value.MemberBCrossplatformId, firstCrossplatformId, StringComparison.Ordinal))) == 1;
            }

            public PlayerReturnPoint? GetReturnPoint(string crossplatformId) => null;
            public DateTimeOffset? GetCooldown(string crossplatformId, TeleportKind kind) => null;

            public TeleportOperation CreateTeleportOperation(TeleportOperationDraft draft)
            {
                var operation = new TeleportOperation(
                    draft,
                    null,
                    TeleportOperationState.Reserved,
                    null,
                    draft.CreatedAtUtc,
                    null,
                    0);
                operations[draft.OperationId] = operation;
                return operation;
            }

            public TeleportOperation? FindTeleportOperation(string operationId) =>
                operations.TryGetValue(operationId, out var value) ? value : null;

            public IReadOnlyList<TeleportOperation> ListTeleportOperations() =>
                operations.Values
                    .OrderBy(value => value.Draft.CreatedAtUtc)
                    .ThenBy(value => value.OperationId, StringComparer.Ordinal)
                    .ToArray();

            public bool TryTransitionTeleportOperation(
                string operationId,
                TeleportOperationState expectedState,
                TeleportOperationState nextState,
                string? errorCode,
                DateTimeOffset updatedAtUtc)
            {
                if (!operations.TryGetValue(operationId, out var current) || current.State != expectedState)
                    return false;
                operations[operationId] = new TeleportOperation(
                    current.Draft,
                    current.Origin,
                    nextState,
                    errorCode,
                    updatedAtUtc,
                    null,
                    current.RowVersion + 1);
                return true;
            }

            public TeleportOperation CompleteTeleportOperation(
                string operationId,
                WorldPosition origin,
                DateTimeOffset kindAvailableAtUtc,
                DateTimeOffset globalAvailableAtUtc,
                DateTimeOffset completedAtUtc)
            {
                var current = operations[operationId];
                var completed = new TeleportOperation(
                    current.Draft,
                    origin,
                    TeleportOperationState.Completed,
                    null,
                    completedAtUtc,
                    completedAtUtc,
                    current.RowVersion + 1);
                operations[operationId] = completed;
                return completed;
            }

            public VoteConfiguration? GetConfiguration(VoteKind kind) =>
                configurations.TryGetValue(kind, out var value) ? value : null;

            public VoteConfiguration SaveConfiguration(VoteConfiguration configuration)
            {
                var current = configurations[configuration.Kind];
                if (current.RowVersion != configuration.RowVersion) throw new CommunityConflictException();
                var saved = new VoteConfiguration(
                    configuration.ConfigurationId,
                    configuration.Kind,
                    configuration.Enabled,
                    configuration.Duration,
                    configuration.ThresholdPercent,
                    configuration.MinimumParticipants,
                    configuration.InitiatorMinimumOnline,
                    configuration.ParticipantMinimumOnline,
                    configuration.InitiatorCooldown,
                    configuration.TargetCooldown,
                    configuration.GlobalCooldown,
                    configuration.MutualExclusionScope,
                    configuration.AllowVoteChange,
                    configuration.UpdatedAtUtc,
                    current.RowVersion + 1);
                configurations[configuration.Kind] = saved;
                return saved;
            }

            public VoteStartResult TryStart(VoteRoundDraft draft)
            {
                var round = new VoteRoundSnapshot(
                    draft.Request.RoundId,
                    draft.Configuration.ConfigurationId,
                    draft.Request.Kind,
                    VoteRoundState.Open,
                    draft.Request.InitiatorCrossplatformId,
                    draft.Request.TargetCrossplatformId,
                    draft.Configuration.MutualExclusionScope,
                    draft.EligibleCrossplatformIds.Count,
                    draft.Configuration.ThresholdPercent,
                    draft.Configuration.MinimumParticipants,
                    draft.Configuration.AllowVoteChange,
                    draft.Request.IdempotencyKey,
                    null,
                    null,
                    draft.Request.CorrelationId,
                    draft.Request.OpenedAtUtc,
                    draft.ExpiresAtUtc,
                    null,
                    null,
                    0);
                rounds[round.RoundId] = round;
                return new VoteStartResult(VoteStartStatus.Started, round);
            }

            public VoteRoundSnapshot GetRound(string roundId) =>
                rounds.TryGetValue(roundId, out var value)
                    ? value
                    : throw new VoteRoundNotFoundException();

            public VoteRoundSnapshot? FindOpenRound(VoteKind kind, string crossplatformId) =>
                rounds.Values.FirstOrDefault(value => value.Kind == kind && value.State == VoteRoundState.Open);

            public VoteCastResult Cast(string roundId, string crossplatformId, VoteChoice choice, DateTimeOffset castAtUtc) =>
                rounds.TryGetValue(roundId, out var round)
                    ? new VoteCastResult(VoteCastStatus.Accepted, round)
                    : new VoteCastResult(VoteCastStatus.RoundNotFound, null);

            public VoteSettlementResult TrySettle(string roundId, DateTimeOffset settledAtUtc) =>
                new VoteSettlementResult(VoteSettlementStatus.NotDue, GetRound(roundId), 0, 0, 0, false);

            public bool TryQueueAction(string roundId, long expectedRowVersion, DateTimeOffset queuedAtUtc) => false;
            public bool TryCompleteAction(string roundId, long expectedRowVersion, VoteRoundState resultState, string? actionJobId, string? actionOperationId, DateTimeOffset completedAtUtc) => false;
            public IReadOnlyList<VoteRoundSnapshot> ListRounds() =>
                rounds.Values.OrderBy(value => value.OpenedAtUtc).ThenBy(value => value.RoundId, StringComparer.Ordinal).ToArray();
            public IReadOnlyList<VoteRoundSnapshot> ListActionQueued() =>
                rounds.Values.Where(value => value.State == VoteRoundState.ActionQueued).ToArray();

            private static TeleportSettings Setting(TeleportKind kind, int? maxHomes) =>
                new TeleportSettings(
                    kind,
                    true,
                    maxHomes,
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromMilliseconds(500),
                    true,
                    0,
                    FixedNow,
                    0);

            private static VoteConfiguration Configuration(VoteKind kind) =>
                new VoteConfiguration(
                    "configuration-" + kind.ToString().ToLowerInvariant(),
                    kind,
                    true,
                    TimeSpan.FromMinutes(1),
                    60,
                    2,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    "global",
                    true,
                    FixedNow,
                    0);

            private static VoteRoundSnapshot Round(
                string roundId,
                VoteKind kind,
                VoteRoundState state) =>
                new VoteRoundSnapshot(
                    roundId,
                    "configuration-" + kind.ToString().ToLowerInvariant(),
                    kind,
                    state,
                    "EOS_1",
                    kind == VoteKind.Kick ? "EOS_2" : null,
                    "global",
                    2,
                    60,
                    2,
                    true,
                    "vote-1",
                    null,
                    null,
                    null,
                    FixedNow,
                    FixedNow.AddMinutes(1),
                    null,
                    null,
                    0);

            private static WorldPosition Position(double x, double y, double z) =>
                new WorldPosition("world-1", x, y, z, 0);
        }
    }
}
