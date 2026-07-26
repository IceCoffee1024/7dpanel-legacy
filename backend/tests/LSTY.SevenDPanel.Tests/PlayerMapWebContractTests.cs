using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerMapWebContractTests
    {
        private const string PlayerId = "EOS_0002d12af0fe4add9c7de0fbc238d431";
        private static readonly DateTimeOffset ObservedAt =
            new DateTimeOffset(2026, 7, 26, 8, 30, 0, TimeSpan.Zero);

        [Fact]
        public void Map_controller_exposes_owner_map_routes()
        {
            Assert.Equal("api/v1/map", typeof(MapController)
                .GetCustomAttributes(typeof(RoutePrefixAttribute), true)
                .Cast<RoutePrefixAttribute>()
                .Single().Prefix);
            AssertRoute(nameof(MapController.GetMetadata), "metadata");
            AssertRoute(nameof(MapController.GetGameTime), "game-time");
            AssertRoute(nameof(MapController.GetPlayerTrack), "players/{crossplatformId}/track");
            AssertRoute("GetLayer", "layers/{layerId}");
            AssertRoute("SearchPlayersInArea", "players/area");
        }

        [Fact]
        public async Task Common_layer_route_maps_projection_features_to_the_frontend_envelope()
        {
            var projection = new StubMapLayerProjection(query =>
                MapLayerProjectionSnapshot.Available(
                    query.Layer,
                    ObservedAt,
                    new MapLayerFeature[]
                    {
                        new TraderMapFeature(
                            "trader-1",
                            new MapLayerPosition(10, 70, -20),
                            "Trader Jen",
                            true,
                            null,
                            41)
                    }));
            using var host = CreateHost(
                "Owner",
                new TrackStore(CreateTrackHistory()),
                layerProjection: projection);

            using var response = await host.Client.GetAsync(
                "api/v1/map/layers/traders?worldId=world-guid&minimumX=-100&minimumZ=-100&maximumX=100&maximumZ=100&zoom=3&limit=250",
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("available", (string?)json["availability"]);
            Assert.Equal(ObservedAt, (DateTimeOffset?)json["observedAtUtc"]);
            Assert.True((bool?)json["isZoomSufficient"]);
            Assert.Equal("trader", (string?)json["items"]?[0]?["kind"]);
            Assert.Equal("Trader Jen", (string?)json["items"]?[0]?["name"]);
            Assert.Equal(10d, (double?)json["items"]?[0]?["position"]?["x"]);
            Assert.Equal(-20d, (double?)json["items"]?[0]?["position"]?["z"]);
        }

        [Fact]
        public async Task Area_search_returns_retained_observation_hits_and_truncation_flags()
        {
            var store = new TrackStore(CreateTrackHistory());
            store.AreaCandidates = new[]
            {
                new PlayerAreaObservationCandidate(
                    51,
                    PlayerId,
                    "Alice",
                    ObservedAt,
                    10,
                    70,
                    -20)
            };
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/area?shape=rectangle&minimumX=-10&minimumZ=-30&maximumX=20&maximumZ=10&fromUtc=2026-07-26T00:00:00Z&toUtc=2026-07-27T00:00:00Z&limit=250",
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(PlayerId, (string?)json["hits"]?[0]?["crossplatformId"]);
            Assert.Equal(1, (int?)json["matchingObservationCount"]);
            Assert.False((bool?)json["candidateObservationLimitReached"]);
            Assert.False((bool?)json["playerResultLimitReached"]);
            Assert.Null(json.SelectToken("$..continuousPresence"));
        }

        [Fact]
        public async Task Transient_layer_without_a_captured_snapshot_is_explicitly_unavailable()
        {
            using var host = CreateHost("Owner", new TrackStore(CreateTrackHistory()));

            using var response = await host.Client.GetAsync(
                "api/v1/map/layers/animals?worldId=world-guid&minimumX=-100&minimumZ=-100&maximumX=100&maximumZ=100&zoom=3&limit=250",
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("unavailable", (string?)json["availability"]);
            Assert.Null((DateTimeOffset?)json["observedAtUtc"]);
            Assert.Empty((JArray)json["items"]!);
        }

        [Theory]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData("Owner", HttpStatusCode.OK)]
        public async Task Map_routes_are_owner_only(string? role, HttpStatusCode expected)
        {
            using var host = CreateHost(role, new TrackStore(CreateTrackHistory()));

            using var response = await host.Client.GetAsync(
                "api/v1/map/metadata",
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Metadata_and_game_time_express_independent_availability()
        {
            using var host = CreateHost(
                "Owner",
                new TrackStore(CreateTrackHistory()),
                MapMetadataProjectionSnapshot.Available(
                    "world-guid",
                    new MapMetadata(
                        "Navezgane",
                        new MapExtent(-512, -384, 512, 384),
                        new MapAxisConvention("east", "north"),
                        new[] { 0, 1, 2, 3, 4 },
                        128,
                        null),
                    ObservedAt),
                MapGameTimeProjectionSnapshot.Unavailable());

            using var metadataResponse = await host.Client.GetAsync(
                "api/v1/map/metadata",
                TestContext.Current.CancellationToken);
            using var gameTimeResponse = await host.Client.GetAsync(
                "api/v1/map/game-time",
                TestContext.Current.CancellationToken);
            var metadata = JObject.Parse(await metadataResponse.Content.ReadAsStringAsync());
            var gameTime = JObject.Parse(await gameTimeResponse.Content.ReadAsStringAsync());

            Assert.Equal("available", (string?)metadata["availability"]);
            Assert.Equal(ObservedAt, (DateTimeOffset?)metadata["observedAtUtc"]);
            Assert.Equal("world-guid", (string?)metadata["worldId"]);
            Assert.Equal("Navezgane", (string?)metadata["worldName"]);
            Assert.Equal(-512, (float?)metadata["extent"]?["minimumX"]);
            Assert.Equal(-384, (float?)metadata["extent"]?["minimumZ"]);
            Assert.Equal(512, (float?)metadata["extent"]?["maximumX"]);
            Assert.Equal(384, (float?)metadata["extent"]?["maximumZ"]);
            Assert.Equal("east", (string?)metadata["axes"]?["xAxisDirection"]);
            Assert.Equal("north", (string?)metadata["axes"]?["zAxisDirection"]);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, metadata["availableZoomLevels"]!.Values<int>());
            Assert.Equal(128, (int?)metadata["tileSize"]);
            Assert.Null((string?)metadata["mapResourceVersion"]);

            Assert.Equal("unavailable", (string?)gameTime["availability"]);
            Assert.Null((int?)gameTime["day"]);
            Assert.Null((int?)gameTime["hour"]);
            Assert.Null((int?)gameTime["minute"]);
            Assert.Null((DateTimeOffset?)gameTime["observedAtUtc"]);
        }

        [Fact]
        public async Task Track_is_readable_without_game_readiness_and_exposes_only_identity_segments_and_points()
        {
            var store = new TrackStore(CreateTrackHistory());
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/" + PlayerId +
                "/track?fromUtc=2026-07-25T00:00:00Z&toUtc=2026-07-27T00:00:00Z",
                TestContext.Current.CancellationToken);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                new[] { "crossplatformId", "segments" },
                json.Properties().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal(PlayerId, (string?)json["crossplatformId"]);
            Assert.Equal(new[] { "points" }, ((JObject)json["segments"]![0]!).Properties()
                .Select(property => property.Name));
            Assert.Equal(41L, (long?)json["segments"]?[0]?["points"]?[0]?["snapshotId"]);
            Assert.Null(json.SelectToken("$..gap"));
            Assert.Null(json.SelectToken("$..gaps"));
            Assert.Null(json.SelectToken("$..reason"));
            Assert.Null(json.SelectToken("$..droppedCount"));
            Assert.Equal(1, store.GetTrackCallCount);
        }

        [Theory]
        [InlineData(" ", "2026-07-25T00:00:00Z", "2026-07-26T00:00:00Z")]
        [InlineData(PlayerId, "not-utc", "2026-07-26T00:00:00Z")]
        [InlineData(PlayerId, "2026-07-26T00:00:00+08:00", "2026-07-27T00:00:00Z")]
        [InlineData(PlayerId, "2026-07-27T00:00:00Z", "2026-07-26T00:00:00Z")]
        [InlineData(PlayerId, "2026-06-01T00:00:00Z", "2026-07-26T00:00:00Z")]
        public async Task Track_rejects_invalid_identity_utc_and_range(
            string playerId,
            string fromUtc,
            string toUtc)
        {
            using var host = CreateHost("Owner", new TrackStore(CreateTrackHistory()));

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/" + playerId + "/track?fromUtc=" +
                Uri.EscapeDataString(fromUtc) + "&toUtc=" + Uri.EscapeDataString(toUtc),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Unknown_player_returns_not_found()
        {
            using var host = CreateHost("Owner", new TrackStore((PlayerTrackHistory?)null));

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/" + PlayerId +
                "/track?fromUtc=2026-07-25T00:00:00Z&toUtc=2026-07-26T00:00:00Z",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task Track_limit_exceeded_maps_to_bad_request()
        {
            using var host = CreateHost(
                "Owner",
                new TrackStore(new PlayerTrackLimitExceededException()));

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/" + PlayerId +
                "/track?fromUtc=2026-07-25T00:00:00Z&toUtc=2026-07-26T00:00:00Z",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Store_failure_returns_deidentified_internal_server_error()
        {
            using var host = CreateHost(
                "Owner",
                new TrackStore(new InvalidOperationException("private sqlite path")));

            using var response = await host.Client.GetAsync(
                "api/v1/map/players/" + PlayerId +
                "/track?fromUtc=2026-07-25T00:00:00Z&toUtc=2026-07-26T00:00:00Z",
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("private sqlite path", body, StringComparison.Ordinal);
        }

        private static void AssertRoute(string methodName, string expectedTemplate)
        {
            var method = typeof(MapController).GetMethod(methodName);
            var route = method?.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .SingleOrDefault();
            Assert.NotNull(method);
            Assert.NotNull(route);
            Assert.Equal(expectedTemplate, route!.Template);
        }

        private static PlayerTrackHistory CreateTrackHistory() => new PlayerTrackHistory(
            new[]
            {
                new PlayerTrackObservation(
                    41,
                    PlayerId,
                    "Alice",
                    10,
                    70,
                    -20,
                    ObservedAt)
            },
            Array.Empty<PlayerHistoryGap>());

        private static HttpTestHost CreateHost(
            string? role,
            IPlayerHistoryStore store,
            MapMetadataProjectionSnapshot? metadata = null,
            MapGameTimeProjectionSnapshot? gameTime = null,
            IMapLayerProjection? layerProjection = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton(store);
            services.AddSingleton<IPlayerHistoryStore>(store);
            services.AddSingleton<IMapMetadataQuery>(new MetadataQuery(
                metadata ?? MapMetadataProjectionSnapshot.Available(
                    "world-guid",
                    new MapMetadata(
                        "Navezgane",
                        new MapExtent(-512, -512, 512, 512),
                        new MapAxisConvention("east", "north"),
                        new[] { 0, 1, 2, 3, 4 },
                        128,
                        null),
                    ObservedAt)));
            services.AddSingleton<IMapGameTimeQuery>(new GameTimeQuery(
                gameTime ?? MapGameTimeProjectionSnapshot.Unavailable()));
            services.AddSingleton<GetMapMetadataUseCase>();
            services.AddSingleton<GetMapGameTimeUseCase>();
            services.AddSingleton<GetPlayerTrackUseCase>();
            services.AddSingleton<IMapLayerProjection>(layerProjection ??
                new StubMapLayerProjection(query => MapLayerProjectionSnapshot.Unavailable(query.Layer)));
            services.AddSingleton<GetMapLayerUseCase>();
            services.AddSingleton<IOnlinePlayerQuery>(new EmptyOnlinePlayerQuery());
            services.AddSingleton<GetHistoricalPlayerLastLocationsUseCase>();
            services.AddSingleton<IPlayerMapSpatialQueryStore>((IPlayerMapSpatialQueryStore)store);
            services.AddSingleton<SearchPlayersInAreaUseCase>();
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

        private sealed class MetadataQuery : IMapMetadataQuery
        {
            private readonly MapMetadataProjectionSnapshot snapshot;
            public MetadataQuery(MapMetadataProjectionSnapshot snapshot) { this.snapshot = snapshot; }
            public MapMetadataProjectionSnapshot Query() => snapshot;
        }

        private sealed class GameTimeQuery : IMapGameTimeQuery
        {
            private readonly MapGameTimeProjectionSnapshot snapshot;
            public GameTimeQuery(MapGameTimeProjectionSnapshot snapshot) { this.snapshot = snapshot; }
            public MapGameTimeProjectionSnapshot Query() => snapshot;
        }

        private sealed class StubMapLayerProjection : IMapLayerProjection
        {
            private readonly Func<MapLayerQuery, MapLayerProjectionSnapshot> query;

            public StubMapLayerProjection(Func<MapLayerQuery, MapLayerProjectionSnapshot> query) =>
                this.query = query;

            public MapLayerProjectionSnapshot Query(MapLayerQuery value) => query(value);
        }

        private sealed class EmptyOnlinePlayerQuery : IOnlinePlayerQuery
        {
            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken) =>
                Task.FromResult(new OnlinePlayersSnapshot(Array.Empty<PlayerSnapshot>()));
        }

        private sealed class TrackStore : IPlayerHistoryStore, IPlayerMapSpatialQueryStore
        {
            private readonly PlayerTrackHistory? history;
            private readonly Exception? exception;

            public TrackStore(PlayerTrackHistory? history) { this.history = history; }
            public TrackStore(Exception exception) { this.exception = exception; }
            public int GetTrackCallCount { get; private set; }
            public IReadOnlyList<PlayerAreaObservationCandidate> AreaCandidates { get; set; } =
                Array.Empty<PlayerAreaObservationCandidate>();
            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();
            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();
            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) => throw new NotSupportedException();
            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) => throw new NotSupportedException();
            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) => throw new NotSupportedException();
            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => throw new NotSupportedException();

            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query)
            {
                GetTrackCallCount++;
                if (exception != null) throw exception;
                return history;
            }

            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) =>
                Array.Empty<HistoricalPlayerLastRetainedLocation>();

            public IReadOnlyList<PlayerAreaObservationCandidate> GetPlayerAreaCandidates(
                PlayerAreaCandidateQuery query) => AreaCandidates;
        }

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
            public PrincipalHandler(string? role) { this.role = role; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
