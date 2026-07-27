using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.GameEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class GameEventsHttpTests
    {
        [Fact]
        public void Controller_exposes_the_owner_only_list_route_and_explicit_response()
        {
            var type = typeof(GameEventsController);
            Assert.Equal("Owner", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            Assert.Equal("api/v1/game-events", type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);
            var get = type.GetMethod("Get");
            Assert.Equal("", get!.GetCustomAttribute<RouteAttribute>()?.Template);
            Assert.Equal(
                "GameEventPageHttpResponse",
                get.GetCustomAttribute<System.Web.Http.Description.ResponseTypeAttribute>()?.ResponseType?.Name);
            Assert.Equal(typeof(string), get.GetParameters().Single(parameter => parameter.Name == "limit").ParameterType);
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        public async Task List_is_owner_only_over_the_real_http_pipeline(
            string? role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role, new StubStore(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/game-events",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-number")]
        [InlineData("0")]
        [InlineData("201")]
        [InlineData("+1")]
        public async Task Invalid_limit_returns_stable_invalidGameEventQuery(string limit)
        {
            using var host = CreateHost("Owner", new StubStore(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/game-events?limit=" + limit,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidGameEventQuery", (string?)problem["code"]);
        }

        [Fact]
        public async Task Valid_limit_is_parsed_manually_and_gap_metadata_stays_separate()
        {
            var store = new StubStore(Page());
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/game-events?limit=17",
                TestContext.Current.CancellationToken);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(17, store.LastQuery!.PageSize);
            Assert.Single(payload["events"]!);
            Assert.Single(payload["gaps"]!);
            Assert.Null(payload["events"]![0]!["reason"]);
        }

        [Fact]
        public void Cursor_round_trips_only_with_the_same_filters_and_rejects_bad_input()
        {
            var cursor = new GameEventCursor(Utc(1), "00000000-0000-0000-0000-000000000001");
            var filters = new GameEventCursorFilters(Utc(0), Utc(2), GameEventType.PlayerJoined, "EOS_1");
            var encoded = GameEventCursorCodec.Encode(cursor, filters);

            Assert.True(GameEventCursorCodec.TryDecode(encoded, filters, out var decoded));
            Assert.Equal(cursor.EventId, decoded!.EventId);
            Assert.False(GameEventCursorCodec.TryDecode("not-a-cursor", filters, out _));
            Assert.False(GameEventCursorCodec.TryDecode(encoded, new GameEventCursorFilters(Utc(0), Utc(2), GameEventType.PlayerJoined, "EOS_2"), out _));
            Assert.False(GameEventCursorCodec.TryDecode(encoded, new GameEventCursorFilters(Utc(1), Utc(2), GameEventType.PlayerJoined, "EOS_1"), out _));
            Assert.False(GameEventCursorCodec.TryDecode(encoded, new GameEventCursorFilters(Utc(0), Utc(2), GameEventType.PlayerLeft, "EOS_1"), out _));
        }

        [Fact]
        public void Controller_returns_stable_problem_details_for_invalid_cursor_and_store_failure()
        {
            using var controller = new GameEventsController(new ThrowingStore());
            controller.Request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/v1/game-events");

            using var invalid = controller.Get(null, null, null, null, null, "bad");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("invalidGameEventCursor", invalid.Content.ReadAsStringAsync().Result);
            using var unavailable = controller.Get(null, null, null, null, null, null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Contains("gameEventsUnavailable", unavailable.Content.ReadAsStringAsync().Result);
        }

        private static DateTimeOffset Utc(int minute) => new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private static GameEventPage Page() => new GameEventPage(
            new[]
            {
                new GameEventRecord(
                    "00000000-0000-0000-0000-000000000001",
                    GameEventType.PlayerJoined,
                    Utc(1),
                    Utc(1),
                    new GameEventSubject("EOS_1", "Steam_1", 1, "Player"),
                    null,
                    null)
            },
            null,
            new[]
            {
                new GameEventGap(
                    "00000000-0000-0000-0000-000000000002",
                    GameEventGapReason.QueueFull,
                    Utc(0),
                    Utc(1),
                    1)
            });

        private static HttpTestHost CreateHost(string? role, IGameEventStore store)
        {
            var services = new ServiceCollection();
            services.AddSingleton(store);
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

        private sealed class StubStore : IGameEventStore
        {
            private readonly GameEventPage page;
            public StubStore(GameEventPage page) => this.page = page;
            public GameEventQuery? LastQuery { get; private set; }
            public void Append(GameEventRecord record) => throw new NotSupportedException();
            public void AppendGap(GameEventGap gap) => throw new NotSupportedException();
            public GameEventPage Query(GameEventQuery query)
            {
                LastQuery = query;
                return page;
            }
        }

        private sealed class ThrowingStore : IGameEventStore
        {
            public void Append(GameEventRecord record) => throw new NotSupportedException();
            public void AppendGap(GameEventGap gap) => throw new NotSupportedException();
            public GameEventPage Query(GameEventQuery query) => throw new InvalidOperationException("unavailable");
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
