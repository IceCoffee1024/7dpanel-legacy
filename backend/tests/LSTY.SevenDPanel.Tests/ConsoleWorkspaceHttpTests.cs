using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Web")]
    public sealed partial class OwinWebHostTests
    {
        [Fact]
        public async Task Console_read_endpoints_enforce_roles_and_return_the_approved_contracts()
        {
            var window = new ServerEventLiveWindow(1100);
            for (var index = 1; index <= 1001; index++)
                window.AppendConsoleLog(CreateConsoleLogEntry(index.ToString()));
            var capturedAtUtc = new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);
            var catalog = new TestConsoleCommandCatalogQuery(new ConsoleCommandCatalog(
                capturedAtUtc,
                new[]
                {
                    new ConsoleCommandCatalogEntry(
                        "version",
                        new[] { "ver" },
                        "Show version",
                        "version help",
                        0)
                }));
            var hub = new ServerEventHub(window);
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                recentConsoleLogs: window,
                consoleCommandCatalog: catalog);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();

                foreach (var user in new[]
                {
                    (Username: "test-owner", Password: "test-password"),
                    (Username: "test-admin", Password: "test-admin-password")
                })
                {
                    var token = await IssueAccessTokenAsync(
                        client,
                        url,
                        user.Username,
                        user.Password);
                    using var logsRequest = new HttpRequestMessage(
                        HttpMethod.Get,
                        url + "api/v1/console/logs/recent");
                    logsRequest.Headers.Authorization = CreateBearerAuthorization(token);
                    using var logsResponse = await client.SendAsync(
                        logsRequest,
                        TestContext.Current.CancellationToken);
                    var logsPayload = JObject.Parse(await logsResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);
                    Assert.Equal(1000, logsPayload["entries"]!.Count());
                    Assert.Equal(2L, (long?)logsPayload["entries"]![0]!["sequence"]);
                    Assert.Equal(
                        new[]
                        {
                            "formattedMessage",
                            "logType",
                            "message",
                            "sequence",
                            "timestamp",
                            "trace",
                            "uptimeMilliseconds"
                        },
                        ((JObject)logsPayload["entries"]![0]!).Properties()
                            .Select(property => property.Name)
                            .OrderBy(name => name)
                            .ToArray());

                    using var catalogRequest = new HttpRequestMessage(
                        HttpMethod.Get,
                        url + "api/v1/console/commands/catalog");
                    catalogRequest.Headers.Authorization = CreateBearerAuthorization(token);
                    using var catalogResponse = await client.SendAsync(
                        catalogRequest,
                        TestContext.Current.CancellationToken);
                    var catalogPayload = JObject.Parse(await catalogResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
                    Assert.Equal(capturedAtUtc, (DateTimeOffset?)catalogPayload["capturedAtUtc"]);
                    Assert.Equal("version", (string?)catalogPayload["commands"]?[0]?["name"]);
                }

                var viewerToken = await IssueAccessTokenAsync(
                    client,
                    url,
                    "test-viewer",
                    "test-viewer-password");
                foreach (var path in new[]
                {
                    "api/v1/console/logs/recent",
                    "api/v1/console/commands/catalog"
                })
                {
                    using var anonymous = await client.GetAsync(
                        url + path,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

                    using var viewerRequest = new HttpRequestMessage(HttpMethod.Get, url + path);
                    viewerRequest.Headers.Authorization = CreateBearerAuthorization(viewerToken);
                    using var viewer = await client.SendAsync(
                        viewerRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
                }
            }
        }

        [Theory]
        [InlineData("0")]
        [InlineData("5001")]
        [InlineData("invalid")]
        [InlineData("")]
        public async Task Recent_console_logs_rejects_invalid_limit_with_stable_problem_details(
            string limit)
        {
            var window = new ServerEventLiveWindow(4);
            var hub = new ServerEventHub(window);
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                recentConsoleLogs: window);
            using var host = new OwinWebHost(url, app => OwinStartup.Configure(app, provider));
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler);
            host.Start();
            var token = await IssueAccessTokenAsync(client, url);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/console/logs/recent?limit=" + limit);
            request.Headers.Authorization = CreateBearerAuthorization(token);

            using var response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertProblemDetailsAsync(
                response,
                "invalid_console_log_limit",
                "/api/v1/console/logs/recent");
        }

        [Fact]
        public async Task Console_read_services_map_expected_unavailability_to_503()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(1));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                recentConsoleLogs: new UnavailableRecentConsoleLogQuery(),
                consoleCommandCatalog: new TestConsoleCommandCatalogQuery(
                    new ConsoleCommandCatalogUnavailableException()));
            using var host = new OwinWebHost(url, app => OwinStartup.Configure(app, provider));
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler);
            host.Start();
            var token = await IssueAccessTokenAsync(client, url);

            foreach (var expected in new[]
            {
                (Path: "api/v1/console/logs/recent", Code: "console_logs_unavailable"),
                (Path: "api/v1/console/commands/catalog", Code: "console_command_catalog_unavailable")
            })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url + expected.Path);
                request.Headers.Authorization = CreateBearerAuthorization(token);
                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(response, expected.Code, "/" + expected.Path);
            }
        }

        [Fact]
        public async Task Openapi_describes_console_read_endpoints_with_stable_operations_and_schemas()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);
            using var host = new OwinWebHost(url, app => OwinStartup.Configure(app, provider));
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler);
            host.Start();

            var document = await GetOpenApiDocumentAsync(client, url);
            var logs = document["paths"]?["/api/v1/console/logs/recent"]?["get"];
            var catalog = document["paths"]?["/api/v1/console/commands/catalog"]?["get"];
            Assert.Equal("ConsoleLogs_GetRecent", (string?)logs?["operationId"]);
            Assert.Equal("ConsoleCommands_GetCatalog", (string?)catalog?["operationId"]);
            AssertBearerSecurity(document, "/api/v1/console/logs/recent", "get");
            AssertBearerSecurity(document, "/api/v1/console/commands/catalog", "get");
            AssertProblemResponses(
                document,
                "/api/v1/console/logs/recent",
                "get",
                "400", "401", "403", "500", "503");
            AssertProblemResponses(
                document,
                "/api/v1/console/commands/catalog",
                "get",
                "401", "403", "500", "503");
            AssertSchemaProperties(document, "RecentConsoleLogsResponse", "entries");
            AssertSchemaProperties(
                document,
                "ConsoleLogEventData",
                "formattedMessage",
                "logType",
                "message",
                "sequence",
                "timestamp",
                "trace",
                "uptimeMilliseconds");
            AssertSchemaProperties(document, "ConsoleCommandCatalog", "capturedAtUtc", "commands");
            AssertSchemaProperties(
                document,
                "ConsoleCommandCatalogEntry",
                "aliases",
                "description",
                "help",
                "name",
                "permissionLevel");
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class TestConsoleCommandCatalogQuery : IConsoleCommandCatalogQuery
        {
            private readonly ConsoleCommandCatalog catalog;
            private readonly Exception? failure;

            public TestConsoleCommandCatalogQuery()
                : this(new ConsoleCommandCatalog(
                    DateTimeOffset.UtcNow,
                    Array.Empty<ConsoleCommandCatalogEntry>()))
            {
            }

            public TestConsoleCommandCatalogQuery(ConsoleCommandCatalog catalog)
            {
                this.catalog = catalog;
            }

            public TestConsoleCommandCatalogQuery(Exception failure)
            {
                catalog = null!;
                this.failure = failure;
            }

            public Task<ConsoleCommandCatalog> GetCatalogAsync(CancellationToken cancellationToken) =>
                failure == null
                    ? Task.FromResult(catalog)
                    : Task.FromException<ConsoleCommandCatalog>(failure);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class UnavailableRecentConsoleLogQuery : IRecentConsoleLogQuery
        {
            public System.Collections.Generic.IReadOnlyList<ConsoleLogEventData> ReadRecentConsoleLogs(
                int limit) => throw new RecentConsoleLogsUnavailableException();
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Web")]

        private sealed class NullRecentActivityWriter : IRecentActivityWriter
        {
            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
