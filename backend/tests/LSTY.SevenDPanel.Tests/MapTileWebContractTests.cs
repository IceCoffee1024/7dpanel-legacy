using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Web")]
    public sealed class MapTileWebContractTests
    {
        private const string TileUrl = "api/v1/map/tiles/world-guid/4/0/0";
        private const string ETag = "\"0123456789abcdef\"";
        private static readonly byte[] PngContent = { 0x89, 0x50, 0x4e, 0x47, 1, 2, 3 };

        [Fact]
        public void Controller_exposes_path_free_typed_tile_route()
        {
            var method = typeof(MapController).GetMethod(nameof(MapController.GetTile));
            var route = method?.GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>()
                .SingleOrDefault();

            Assert.NotNull(method);
            Assert.Equal("tiles/{worldId}/{z:int}/{x:int}/{y:int}", route?.Template);
            Assert.DoesNotContain(
                method!.GetParameters(),
                parameter => parameter.Name?.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             parameter.Name?.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             parameter.Name?.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Theory]
        [InlineData(null, false, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", true, HttpStatusCode.Forbidden)]
        [InlineData("Viewer", true, HttpStatusCode.Forbidden)]
        [InlineData("Owner", true, HttpStatusCode.OK)]
        public async Task Tile_route_requires_owner_bearer_header(
            string? role,
            bool includeBearer,
            HttpStatusCode expected)
        {
            var store = TileStore.Returning(Png());
            using var host = CreateHost(role, store);
            using var request = new HttpRequestMessage(HttpMethod.Get, TileUrl);
            if (includeBearer)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, response.StatusCode);
            Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, store.ReadCount);
        }

        [Fact]
        public async Task Query_token_is_ignored_and_never_reaches_the_tile_store()
        {
            var store = TileStore.Returning(Png());
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                TileUrl + "?access_token=test-token&path=C%3A%5Cprivate%5Cmap.png",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(0, store.ReadCount);
        }

        [Fact]
        public async Task Png_tile_returns_private_cache_etag_and_exact_bytes()
        {
            var store = TileStore.Returning(Png());
            using var host = CreateHost("Owner", store);

            using var response = await host.SendOwnerAsync(TileUrl);
            var content = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(PngContent, content);
            Assert.Equal(ETag, response.Headers.ETag?.Tag);
            Assert.True(response.Headers.CacheControl?.Private);
            Assert.True(response.Headers.CacheControl?.MustRevalidate);
            Assert.Equal(TimeSpan.Zero, response.Headers.CacheControl?.MaxAge);
        }

        [Fact]
        public async Task Webp_tile_uses_the_approved_content_type()
        {
            var store = TileStore.Returning(MapTileReadResult.Available(
                new byte[] { 0x52, 0x49, 0x46, 0x46 },
                "image/webp",
                ETag,
                null));
            using var host = CreateHost("Owner", store);

            using var response = await host.SendOwnerAsync(TileUrl);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        }

        [Fact]
        public async Task Matching_if_none_match_returns_304_without_a_body()
        {
            using var host = CreateHost("Owner", TileStore.Returning(Png()));
            using var request = OwnerRequest(TileUrl);
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(ETag));

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Null(response.Content);
            Assert.Equal(ETag, response.Headers.ETag?.Tag);
            Assert.True(response.Headers.CacheControl?.Private);
        }

        [Theory]
        [InlineData(MapTileReadStatus.Missing, HttpStatusCode.NotFound, "map_tile_not_found")]
        [InlineData(MapTileReadStatus.Unavailable, HttpStatusCode.ServiceUnavailable, "map_tile_unavailable")]
        public async Task Missing_and_unavailable_tiles_have_explicit_problem_responses(
            MapTileReadStatus status,
            HttpStatusCode expectedStatus,
            string expectedCode)
        {
            var result = status == MapTileReadStatus.Missing
                ? MapTileReadResult.Missing()
                : MapTileReadResult.Unavailable();
            using var host = CreateHost("Owner", TileStore.Returning(result));

            using var response = await host.SendOwnerAsync(TileUrl);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(expectedCode, (string?)problem["code"]);
            Assert.True(response.Headers.CacheControl?.Private);
        }

        [Theory]
        [InlineData("api/v1/map/tiles/../4/0/0")]
        [InlineData("api/v1/map/tiles/world.name/4/0/0")]
        [InlineData("api/v1/map/tiles/other-world/4/0/0")]
        [InlineData("api/v1/map/tiles/world-guid/-1/0/0")]
        [InlineData("api/v1/map/tiles/world-guid/5/0/0")]
        [InlineData("api/v1/map/tiles/world-guid/4/-5/0")]
        [InlineData("api/v1/map/tiles/world-guid/4/0/4")]
        public async Task Invalid_world_zoom_and_coordinates_never_reach_storage(string url)
        {
            var store = TileStore.Returning(Png());
            using var host = CreateHost("Owner", store);

            using var response = await host.SendOwnerAsync(url);

            Assert.True(
                response.StatusCode == HttpStatusCode.BadRequest ||
                response.StatusCode == HttpStatusCode.NotFound);
            Assert.Equal(0, store.ReadCount);
        }

        [Fact]
        public async Task Unexpected_failure_returns_sanitized_500_and_preserves_private_cache_policy()
        {
            using var host = CreateHost(
                "Owner",
                TileStore.Throwing(new IOException("C:\\private\\save\\map\\4\\0\\0.png")));

            using var response = await host.SendOwnerAsync(TileUrl);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("save", body, StringComparison.OrdinalIgnoreCase);
            Assert.True(response.Headers.CacheControl?.Private);
        }

        private static MapTileReadResult Png() => MapTileReadResult.Available(
            PngContent,
            "image/png",
            ETag,
            null);

        private static HttpRequestMessage OwnerRequest(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            return request;
        }

        private static HttpTestHost CreateHost(string? role, IMapTileStore store)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IMapTileStore>(store);
            services.AddSingleton<IMapMetadataQuery>(new MetadataQuery());
            services.AddSingleton<IMapGameTimeQuery>(new GameTimeQuery());
            services.AddSingleton<IPlayerHistoryStore>(new TrackStore());
            services.AddSingleton<GetMapMetadataUseCase>();
            services.AddSingleton<GetMapGameTimeUseCase>();
            services.AddSingleton<GetPlayerTrackUseCase>();
            services.AddSingleton<GetMapTileUseCase>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new BearerPrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class MetadataQuery : IMapMetadataQuery
        {
            public MapMetadataProjectionSnapshot Query() => MapMetadataProjectionSnapshot.Available(
                "world-guid",
                new MapMetadata(
                    "Navezgane",
                    new MapExtent(-512, -512, 512, 512),
                    new MapAxisConvention("east", "north"),
                    new[] { 0, 1, 2, 3, 4 },
                    128,
                    null),
                new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero));
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class GameTimeQuery : IMapGameTimeQuery
        {
            public MapGameTimeProjectionSnapshot Query() => MapGameTimeProjectionSnapshot.Unavailable();
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class TileStore : IMapTileStore
        {
            private readonly MapTileReadResult? result;
            private readonly Exception? exception;

            private TileStore(MapTileReadResult? result, Exception? exception)
            {
                this.result = result;
                this.exception = exception;
            }

            public int ReadCount { get; private set; }

            public static TileStore Returning(MapTileReadResult result) => new TileStore(result, null);

            public static TileStore Throwing(Exception exception) => new TileStore(null, exception);

            public Task<MapTileReadResult> ReadAsync(MapTileKey key, CancellationToken cancellationToken)
            {
                ReadCount++;
                if (exception != null) throw exception;
                return Task.FromResult(result!);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class TrackStore : IPlayerHistoryStore
        {
            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();
            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();
            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) => throw new NotSupportedException();
            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) => throw new NotSupportedException();
            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) => throw new NotSupportedException();
            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) => null;
            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) => Array.Empty<HistoricalPlayerLastRetainedLocation>();
            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => throw new NotSupportedException();
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

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

            public Task<HttpResponseMessage> SendOwnerAsync(string url)
            {
                var request = OwnerRequest(url);
                return Client.SendAsync(request, TestContext.Current.CancellationToken);
            }

            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Web")]

        private sealed class BearerPrincipalHandler : DelegatingHandler
        {
            private readonly string? role;

            public BearerPrincipalHandler(string? role) => this.role = role;

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var authorization = request.Headers.Authorization;
                var accepted = string.Equals(authorization?.Scheme, "Bearer", StringComparison.Ordinal) &&
                    string.Equals(authorization?.Parameter, "test-token", StringComparison.Ordinal);
                var identity = !accepted || role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, "subject-1"),
                            new Claim(ClaimTypes.Role, role)
                        },
                        "Bearer");
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
