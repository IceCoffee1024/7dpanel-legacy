using System;
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
    public sealed class AuditHttpTests
    {
        [Theory]
        [InlineData("Owner", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        public async Task List_is_owner_only(string? role, HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role, new StubQuery(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/audit",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Theory]
        [InlineData("?fromUtc=not-a-time")]
        [InlineData("?fromUtc=2026-07-26T08%3A00%3A00")]
        [InlineData("?toUtc=2026-07-26T16%3A00%3A00%2B08%3A00")]
        [InlineData("?sourceKind=not-a-source")]
        [InlineData("?status=not-a-status")]
        [InlineData("?limit=201")]
        public async Task Invalid_query_values_return_stable_problem_details(string queryString)
        {
            using var host = CreateHost("Owner", new StubQuery(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/audit" + queryString,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidAuditQuery", (string?)problem["code"]);
        }

        [Fact]
        public async Task Malformed_cursor_returns_its_distinct_problem_code()
        {
            using var host = CreateHost("Owner", new StubQuery(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/audit?cursor=not-a-cursor",
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidAuditCursor", (string?)problem["code"]);
        }

        [Fact]
        public void Cursor_codec_rejects_a_stale_unknown_source_kind()
        {
            var stale = AuditCursorCodec.Encode(new UnifiedAuditCursor(
                DateTimeOffset.Parse("2026-07-26T08:00:00Z"),
                "legacySource",
                "legacy-1"));

            Assert.False(AuditCursorCodec.TryDecode(stale, out _));
        }

        [Fact]
        public async Task Stale_cursor_shape_returns_invalid_cursor_problem()
        {
            var stale = AuditCursorCodec.Encode(new UnifiedAuditCursor(
                DateTimeOffset.Parse("2026-07-26T08:00:00Z"),
                "legacySource",
                "legacy-1"));
            using var host = CreateHost("Owner", new StubQuery(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/audit?cursor=" + Uri.EscapeDataString(stale),
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalidAuditCursor", (string?)problem["code"]);
        }

        [Fact]
        public async Task Utc_roundtrip_timestamp_is_passed_to_the_query_unchanged()
        {
            var query = new StubQuery(Page());
            using var host = CreateHost("Owner", query);

            using var response = await host.Client.GetAsync(
                "api/v1/audit?fromUtc=2026-07-26T08%3A00%3A00.1234567Z",
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                DateTimeOffset.Parse("2026-07-26T08:00:00.1234567Z"),
                query.LastFilter!.FromUtc);
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("Viewer")]
        public async Task Authenticated_non_owner_receives_forbidden_problem_details(string role)
        {
            using var host = CreateHost(role, new StubQuery(Page()));

            using var response = await host.Client.GetAsync(
                "api/v1/audit",
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("owner_required", (string?)problem["code"]);
        }

        [Fact]
        public async Task List_returns_only_projection_fields_and_an_opaque_next_cursor()
        {
            var cursor = new UnifiedAuditCursor(DateTimeOffset.Parse("2026-07-26T08:00:00Z"), "consoleCommand", "audit-1");
            using var host = CreateHost("Owner", new StubQuery(Page(cursor)));

            using var response = await host.Client.GetAsync(
                "api/v1/audit?limit=20",
                TestContext.Current.CancellationToken);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
            var entry = (JObject)Assert.Single(payload["entries"]!);
            var nextCursor = (string?)payload["nextCursor"];

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "action", "actorSubject", "correlationId", "hasDetails", "occurredAtUtc", "sourceId", "sourceKind", "status", "targetRef" }, entry.Properties().Select(property => property.Name).OrderBy(name => name));
            Assert.False((bool?)entry["hasDetails"] ?? true);
            Assert.DoesNotContain("Secret", payload.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(nextCursor);
            Assert.DoesNotContain("|", nextCursor!);
            Assert.True(AuditCursorCodec.TryDecode(nextCursor!, out var decoded));
            Assert.Equal(cursor.OccurredAtUtc, decoded!.OccurredAtUtc);
            Assert.Equal(cursor.SourceKind, decoded.SourceKind);
            Assert.Equal(cursor.SourceId, decoded.SourceId);
        }

        [Fact]
        public async Task Query_failure_returns_auditUnavailable_problem()
        {
            using var host = CreateHost("Owner", new ThrowingQuery());

            using var response = await host.Client.GetAsync(
                "api/v1/audit",
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("auditUnavailable", (string?)problem["code"]);
        }

        private static UnifiedAuditPage Page(UnifiedAuditCursor? cursor = null) =>
            new UnifiedAuditPage(
                new[]
                {
                    new UnifiedAuditEntry(
                        "consoleCommand", "audit-1", "owner", null, "say",
                        DateTimeOffset.Parse("2026-07-26T08:00:00Z"), "Completed", "corr-1", false)
                },
                cursor,
                Array.Empty<AuditSourceGap>());

        private static HttpTestHost CreateHost(string? role, IUnifiedAuditQuery query)
        {
            var services = new ServiceCollection();
            services.AddSingleton(query);
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration { DependencyResolver = new MicrosoftDependencyResolver(provider) };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        private sealed class StubQuery : IUnifiedAuditQuery
        {
            private readonly UnifiedAuditPage page;
            public StubQuery(UnifiedAuditPage page) { this.page = page; }
            public UnifiedAuditFilter? LastFilter { get; private set; }
            public UnifiedAuditPage Query(UnifiedAuditFilter filter)
            {
                LastFilter = filter;
                return page;
            }
        }

        private sealed class ThrowingQuery : IUnifiedAuditQuery
        {
            public UnifiedAuditPage Query(UnifiedAuditFilter filter) => throw new InvalidOperationException("database unavailable");
        }

        private sealed class HttpTestHost : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;
            public HttpTestHost(ServiceProvider provider, HttpConfiguration configuration)
            {
                this.provider = provider;
                this.configuration = configuration;
                Client = new HttpClient(new HttpServer(configuration)) { BaseAddress = new Uri("http://localhost/") };
            }
            public HttpClient Client { get; }
            public void Dispose() { Client.Dispose(); configuration.Dispose(); provider.Dispose(); }
        }

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;
            public PrincipalHandler(string? role) { this.role = role; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var identity = role == null ? new ClaimsIdentity() : new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "owner-1"),
                    new Claim(ClaimTypes.Role, role)
                }, "Test");
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
