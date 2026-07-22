using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin.Security.DataProtection;
using Newtonsoft.Json.Linq;
using NSwag;
using Owin;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Category", "Integration")]
    [Trait("Host", "InProcessKatana")]
    public sealed class OwinWebHostTests
    {
        [Fact]
        public async Task Anonymous_openapi_document_is_public()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                using var response = await client.GetAsync(
                    url + "swagger/v1/swagger.json",
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
                Assert.StartsWith("3.", (string?)payload["openapi"]);
                Assert.Equal("7DPanel API", (string?)payload["info"]?["title"]);
                Assert.Equal("v1", (string?)payload["info"]?["version"]);
                Assert.NotNull(payload["paths"]?["/health"]?["get"]);
                Assert.NotNull(payload["paths"]?["/api/v1/health"]?["get"]);
            }
        }

        [Fact]
        public async Task Anonymous_swagger_ui_uses_fixed_document_path()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                using var response = await client.GetAsync(
                    url + "swagger",
                    TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains("/swagger/v1/swagger.json", body);
            }
        }

        [Fact]
        public async Task Openapi_document_covers_controller_and_owin_routes()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var document = await GetOpenApiDocumentAsync(client, url);

                Assert.NotNull(document["paths"]?["/health"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/health"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/events/stream"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/console/commands"]?["post"]);
                Assert.NotNull(document["paths"]?["/api/v1/players/online"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/players/{entityId}/kick"]?["post"]);
                var tokenOperation = document["paths"]?["/api/v1/auth/token"]?["post"];
                Assert.NotNull(tokenOperation);
                var formSchema = tokenOperation!["requestBody"]?["content"]?
                    ["application/x-www-form-urlencoded"]?["schema"];
                Assert.NotNull(formSchema);
                Assert.Equal(
                    new[] { "grant_type", "password", "username" },
                    formSchema!["required"]!.Values<string>().OrderBy(value => value).ToArray());
                Assert.Equal("password", (string?)formSchema["properties"]?["grant_type"]?["enum"]?[0]);
                Assert.NotNull(formSchema["properties"]?["username"]);
                Assert.NotNull(formSchema["properties"]?["password"]);

                var successSchema = tokenOperation["responses"]?["200"]?["content"]?
                    ["application/json"]?["schema"];
                Assert.NotNull(successSchema?["properties"]?["access_token"]);
                Assert.NotNull(successSchema?["properties"]?["token_type"]);
                Assert.NotNull(successSchema?["properties"]?["expires_in"]);
                Assert.Equal(
                    new[] { "access_token", "expires_in", "token_type" },
                    successSchema!["required"]!.Values<string>().OrderBy(value => value).ToArray());
                var errorSchema = tokenOperation["responses"]?["400"]?["content"]?
                    ["application/json"]?["schema"];
                Assert.Equal(
                    new[] { "error" },
                    errorSchema!["required"]!.Values<string>().OrderBy(value => value).ToArray());
                Assert.Null(formSchema["properties"]?["refresh_token"]);
                Assert.Null(successSchema["properties"]?["refresh_token"]);
                Assert.Equal("Authentication", (string?)tokenOperation["tags"]?[0]);
                Assert.Contains("Refresh tokens are not supported", (string?)tokenOperation["description"]);
                Assert.Contains("password-grant form data", (string?)tokenOperation["requestBody"]?["description"]);
                Assert.Null(tokenOperation["security"]);
                Assert.Null(tokenOperation["responses"]?["400"]?["content"]?
                    ["application/problem+json"]);
            }
        }

        [Fact]
        public void Openapi_token_operation_rejects_duplicate_post_registration()
        {
            var document = new OpenApiDocument();
            document.Paths[HttpRoutes.TokenEndpoint] = new OpenApiPathItem
            {
                [OpenApiOperationMethod.Post] = new OpenApiOperation()
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                PanelOpenApiDocumentProcessor.AddOAuthTokenEndpoint(document));

            Assert.Contains("already contains POST", exception.Message);
            Assert.Contains(HttpRoutes.TokenEndpoint, exception.Message);
        }

        [Fact]
        public void Openapi_token_operation_rejects_case_insensitive_duplicate_post_registration()
        {
            var document = new OpenApiDocument();
            document.Paths[HttpRoutes.TokenEndpoint.ToUpperInvariant()] = new OpenApiPathItem
            {
                [OpenApiOperationMethod.Post] = new OpenApiOperation()
            };

            Assert.Throws<InvalidOperationException>(() =>
                PanelOpenApiDocumentProcessor.AddOAuthTokenEndpoint(document));
        }

        [Fact]
        public void Openapi_token_operation_preserves_other_methods_on_the_same_path()
        {
            var document = new OpenApiDocument();
            var path = new OpenApiPathItem
            {
                [OpenApiOperationMethod.Get] = new OpenApiOperation()
            };
            document.Paths[HttpRoutes.TokenEndpoint] = path;

            PanelOpenApiDocumentProcessor.AddOAuthTokenEndpoint(document);

            Assert.Same(path, document.Paths[HttpRoutes.TokenEndpoint]);
            Assert.NotNull(path[OpenApiOperationMethod.Get]);
            Assert.NotNull(path[OpenApiOperationMethod.Post]);
        }

        [Fact]
        public async Task Openapi_document_describes_basic_and_bearer_security()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var document = await GetOpenApiDocumentAsync(client, url);

                Assert.Equal("http", (string?)document["components"]?["securitySchemes"]?["Basic"]?["type"]);
                Assert.Equal("basic", (string?)document["components"]?["securitySchemes"]?["Basic"]?["scheme"]);
                Assert.Equal("http", (string?)document["components"]?["securitySchemes"]?["Bearer"]?["type"]);
                Assert.Equal("bearer", (string?)document["components"]?["securitySchemes"]?["Bearer"]?["scheme"]);

                Assert.Null(document["paths"]?["/health"]?["get"]?["security"]);
                Assert.Null(document["paths"]?["/api/v1/health"]?["get"]?["security"]);
                Assert.Null(document["paths"]?["/api/v1/auth/token"]?["post"]?["security"]);
                AssertAlternativeSecurity(document, "/api/v1/events/stream", "get");
                AssertAlternativeSecurity(document, "/api/v1/console/commands", "post");
                AssertAlternativeSecurity(document, "/api/v1/players/online", "get");
                AssertAlternativeSecurity(document, "/api/v1/players/{entityId}/kick", "post");
            }
        }

        [Fact]
        public async Task Openapi_document_describes_server_sent_event_stream()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var document = await GetOpenApiDocumentAsync(client, url);
                var operation = document["paths"]?["/api/v1/events/stream"]?["get"];
                Assert.NotNull(operation);

                var lastEventId = operation!["parameters"]?
                    .Single(parameter => (string?)parameter?["name"] == "Last-Event-ID");
                Assert.Equal("header", (string?)lastEventId?["in"]);
                Assert.NotEqual(true, (bool?)lastEventId?["required"]);
                Assert.NotNull(operation["responses"]?["200"]?["content"]?["text/event-stream"]);
                Assert.Contains("long-lived named event stream", (string?)operation["description"]);
                Assert.Contains("cannot be rewritten as JSON", (string?)operation["description"]);
            }
        }

        [Fact]
        public async Task Openapi_document_reuses_problem_details_for_actual_api_errors()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var document = await GetOpenApiDocumentAsync(client, url);

                AssertProblemResponses(document, "/api/v1/events/stream", "get", "400", "401", "429", "500", "503");
                AssertProblemResponses(document, "/api/v1/console/commands", "post", "400", "401", "403", "500", "503");
                AssertProblemResponses(document, "/api/v1/players/online", "get", "401", "403", "500", "503");
                AssertProblemResponses(document, "/api/v1/players/{entityId}/kick", "post", "400", "401", "403", "409", "500", "503");
                AssertProblemResponses(document, "/api/v1/auth/token", "post", "429", "500");
                AssertResponseCodes(document, "/api/v1/events/stream", "get", "200", "400", "401", "429", "500", "503");
                AssertResponseCodes(document, "/api/v1/console/commands", "post", "200", "400", "401", "403", "500", "503");
                AssertResponseCodes(document, "/api/v1/players/online", "get", "200", "401", "403", "500", "503");
                AssertResponseCodes(document, "/api/v1/players/{entityId}/kick", "post", "200", "400", "401", "403", "409", "500", "503");
                AssertResponseCodes(document, "/api/v1/auth/token", "post", "200", "400", "429", "500");

                var schema = document["components"]?["schemas"]?["ApiProblemDetails"];
                Assert.NotNull(schema);
                Assert.Equal(
                    new[] { "code", "detail", "instance", "status", "title", "traceId", "type" },
                    schema!["properties"]!.Children<JProperty>()
                        .Select(property => property.Name)
                        .OrderBy(name => name)
                        .ToArray());
                Assert.DoesNotContain("exception", schema.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("stack", schema.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Null(document["paths"]?["/api/v1/auth/token"]?["post"]?["responses"]?["400"]?
                    ["content"]?["application/problem+json"]);
            }
        }

        [Fact]
        public async Task Swagger_requests_do_not_invoke_game_or_audit_dependencies()
        {
            var consoleGateway = new TestConsoleCommandGateway();
            var onlinePlayers = new TestOnlinePlayerQuery();
            var playerActions = new TestPlayerActions();
            var auditTrail = new TestPlayerActionAuditTrail();
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                false,
                hub,
                consoleGateway: consoleGateway,
                onlinePlayerQuery: onlinePlayers,
                playerActions: playerActions,
                playerActionAuditTrail: auditTrail);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                using var documentResponse = await client.GetAsync(
                    url + "swagger/v1/swagger.json",
                    TestContext.Current.CancellationToken);
                using var uiResponse = await client.GetAsync(
                    url + "swagger",
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
                Assert.Equal(HttpStatusCode.OK, uiResponse.StatusCode);
                Assert.Null(consoleGateway.Request);
                Assert.Equal(0, onlinePlayers.CallCount);
                Assert.Equal(0, playerActions.CallCount);
                Assert.Equal(0, auditTrail.CreatePendingCallCount);
            }
        }

        [Theory]
        [InlineData("health")]
        [InlineData("api/v1/health")]
        public async Task Health_endpoint_runs_in_real_katana_host(string route)
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var response = await client.GetAsync(url + route, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                AssertHealthContract(body);
            }

            var rebound = new TcpListener(IPAddress.Loopback, port);
            try
            {
                rebound.Start();
            }
            finally
            {
                rebound.Stop();
            }
        }

        [Fact]
        public async Task Console_command_requires_authentication()
        {
            var gateway = new TestConsoleCommandGateway();
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "version"))
            {
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/console/commands");
                Assert.Null(gateway.Request);
            }
        }

        [Fact]
        public async Task Authenticated_owner_executes_arbitrary_command_without_normalization()
        {
            const string rawCommand = "  thirdparty.sample  alpha  ";
            var gateway = new TestConsoleCommandGateway();
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, rawCommand))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(rawCommand, (string?)payload["command"]);
                Assert.Equal("command output", (string?)payload["output"]?[0]);
                Assert.Equal("test-owner-subject", gateway.Request?.ActorSubject);
                Assert.Equal(rawCommand, gateway.Request?.Command);
            }
        }

        [Fact]
        public async Task Concurrent_console_requests_keep_independent_results_without_command_events()
        {
            var gateway = new ConcurrentConsoleCommandGateway();
            var window = new ServerEventLiveWindow(4);
            var hub = new ServerEventHub(window);
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var firstRequest = CreateConsoleCommandRequest(url, "version"))
            using (var secondRequest = CreateConsoleCommandRequest(url, "thirdparty.sample alpha"))
            {
                firstRequest.Headers.Authorization = CreateBasicAuthorization();
                secondRequest.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                var firstResponseTask = client.SendAsync(
                    firstRequest,
                    TestContext.Current.CancellationToken);
                var secondResponseTask = client.SendAsync(
                    secondRequest,
                    TestContext.Current.CancellationToken);
                Assert.True(gateway.BothReceived.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken));
                gateway.Complete("thirdparty.sample alpha");
                gateway.Complete("version");

                using var firstResponse = await firstResponseTask;
                using var secondResponse = await secondResponseTask;
                var firstPayload = JObject.Parse(await firstResponse.Content.ReadAsStringAsync());
                var secondPayload = JObject.Parse(await secondResponse.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
                Assert.Equal("version-output", (string?)firstPayload["output"]?[0]);
                Assert.Equal(
                    "thirdparty.sample alpha-output",
                    (string?)secondPayload["output"]?[0]);
                Assert.Equal(
                    new[] { "thirdparty.sample alpha", "version" },
                    gateway.Commands.OrderBy(command => command));
                Assert.Empty(window.ReadAfter(null, 10).Entries);
            }
        }

        [Fact]
        public async Task State_changing_console_command_is_forwarded()
        {
            var gateway = new TestConsoleCommandGateway();
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "kick player"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("kick player", gateway.Request?.Command);
            }
        }

        [Fact]
        public async Task Console_command_rejects_requests_before_game_ready()
        {
            var gateway = new TestConsoleCommandGateway();
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway,
                gameReadiness: GameReadinessState.Loading);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "version"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "game_not_ready",
                    "/api/v1/console/commands");
                Assert.Null(gateway.Request);
            }
        }

        [Fact]
        public async Task Full_console_command_queue_returns_problem_details()
        {
            var gateway = new TestConsoleCommandGateway(
                new ConsoleCommandQueueFullException());
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "version"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "console_command_queue_full",
                    "/api/v1/console/commands");
            }
        }

        [Fact]
        public async Task Stopped_console_command_service_returns_problem_details()
        {
            var gateway = new TestConsoleCommandGateway(
                new ConsoleCommandUnavailableException());
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "version"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "console_command_unavailable",
                    "/api/v1/console/commands");
            }
        }

        [Fact]
        public async Task Online_players_requires_authentication()
        {
            var query = new TestOnlinePlayerQuery();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, onlinePlayerQuery: query);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreatePlayersRequest(url))
            {
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/players/online");
                Assert.Equal(0, query.CallCount);
            }
        }

        [Fact]
        public async Task Owner_with_empty_snapshot_returns_200_and_empty_array()
        {
            var query = new TestOnlinePlayerQuery(new OnlinePlayersSnapshot(
                new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero),
                Array.Empty<PlayerSnapshot>()));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, onlinePlayerQuery: query);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreatePlayersRequest(url))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(
                    new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero),
                    (DateTimeOffset?)payload["capturedAtUtc"]);
                Assert.Equal(0, ((JArray?)payload["players"])?.Count ?? 0);
                Assert.Equal(1, query.CallCount);
            }
        }

        [Fact]
        public async Task Owner_with_multiple_players_returns_camel_case_fields_and_sorted_results()
        {
            var query = new TestOnlinePlayerQuery(new OnlinePlayersSnapshot(
                new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero),
                new[]
                {
                    new PlayerSnapshot(
                        42,
                        "Zed",
                        new PlayerPlatformIdentity("steam-2", "Steam"),
                        new PlayerPlatformIdentity("cross-2", "Epic"),
                        100,
                        20,
                        90),
                    new PlayerSnapshot(
                        7,
                        "Alice",
                        new PlayerPlatformIdentity("steam-1", "Steam"),
                        null,
                        40,
                        18,
                        95)
                }));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, onlinePlayerQuery: query);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreatePlayersRequest(url))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
                var players = (JArray)payload["players"]!;

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(players.All(item => ((JObject)item).Properties().Select(property => property.Name).OrderBy(name => name).SequenceEqual(new[]
                {
                    "crossplatformIdentity",
                    "entityId",
                    "health",
                    "level",
                    "name",
                    "ping",
                    "platformIdentity"
                })), "unexpected player property names");
                Assert.Equal(
                    new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero),
                    (DateTimeOffset?)payload["capturedAtUtc"]);
                Assert.Equal(2, players.Count);
                Assert.Equal(7, (int?)players[0]["entityId"]);
                Assert.Equal(42, (int?)players[1]["entityId"]);
                Assert.Equal("Alice", (string?)players[0]["name"]);
                Assert.Equal("steam-1", (string?)players[0]["platformIdentity"]?["combinedId"]);
                Assert.Equal("Steam", (string?)players[0]["platformIdentity"]?["platform"]);
                Assert.Equal("cross-2", (string?)players[1]["crossplatformIdentity"]?["combinedId"]);
                Assert.Equal("Epic", (string?)players[1]["crossplatformIdentity"]?["platform"]);
                Assert.Equal(40, (int?)players[0]["ping"]);
                Assert.Equal(90, (int?)players[1]["health"]);
            }
        }

        [Fact]
        public async Task Game_not_ready_rejects_online_player_query()
        {
            var query = new TestOnlinePlayerQuery();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                gameReadiness: GameReadinessState.Loading,
                onlinePlayerQuery: query);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreatePlayersRequest(url))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "game_not_ready",
                    "/api/v1/players/online");
                Assert.Equal(0, query.CallCount);
            }
        }

        [Theory]
        [InlineData(typeof(OnlinePlayerQueryBusyException), "online_player_query_busy")]
        [InlineData(typeof(TimeoutException), "game_thread_timeout")]
        [InlineData(typeof(OnlinePlayerSnapshotUnavailableException), "online_player_snapshot_unavailable")]
        public async Task Online_player_query_errors_return_stable_problem_details(
            Type exceptionType,
            string expectedCode)
        {
            var query = new TestOnlinePlayerQuery(exceptionType);
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, onlinePlayerQuery: query);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreatePlayersRequest(url))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    expectedCode,
                    "/api/v1/players/online");
                Assert.Equal(1, query.CallCount);
            }
        }

        [Fact]
        public async Task Kick_player_requires_authentication_without_audit_or_action()
        {
            var actions = new TestPlayerActions();
            var audit = new TestPlayerActionAuditTrail();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                playerActions: actions,
                playerActionAuditTrail: audit);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateKickPlayerRequest(url, 7, ValidKickBody))
            {
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/players/7/kick");
                Assert.Equal(0, actions.CallCount);
                Assert.Equal(0, audit.CreatePendingCallCount);
            }
        }

        [Theory]
        [InlineData(7, "{\"expectedPlatformIdentity\":{\"combinedId\":\"steam-1\",\"platform\":\"Steam\"},\"reason\":\"rule violation\"}", "player_kick_confirmation_required")]
        [InlineData(7, "{\"expectedPlatformIdentity\":{\"combinedId\":\"steam-1\",\"platform\":\"Steam\"},\"reason\":\"rule violation\",\"confirmed\":false}", "player_kick_confirmation_required")]
        [InlineData(7, "{\"expectedPlatformIdentity\":{\"combinedId\":\"steam-1\",\"platform\":\"Steam\"},\"reason\":\"   \",\"confirmed\":true}", "invalid_player_kick_reason")]
        [InlineData(7, "{\"reason\":\"rule violation\",\"confirmed\":true}", "invalid_player_identity")]
        [InlineData(-1, "{\"expectedPlatformIdentity\":{\"combinedId\":\"steam-1\",\"platform\":\"Steam\"},\"reason\":\"rule violation\",\"confirmed\":true}", "invalid_player_identity")]
        public async Task Invalid_kick_request_returns_stable_problem_without_audit_or_action(
            int entityId,
            string body,
            string expectedCode)
        {
            var actions = new TestPlayerActions();
            var audit = new TestPlayerActionAuditTrail();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                playerActions: actions,
                playerActionAuditTrail: audit);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateKickPlayerRequest(url, entityId, body))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    expectedCode,
                    "/api/v1/players/" + entityId + "/kick");
                Assert.Equal(0, actions.CallCount);
                Assert.Equal(0, audit.CreatePendingCallCount);
            }
        }

        [Fact]
        public void Kick_player_action_is_owner_authorized()
        {
            var controllerAuthorization = Assert.Single(
                typeof(PlayersController).GetCustomAttributes<AuthorizeAttribute>());
            var action = Assert.Single(
                typeof(PlayersController).GetMethods(),
                method => method.Name == "Kick");

            Assert.Equal("Owner", controllerAuthorization.Roles);
            Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
            Assert.Equal(
                "{entityId:int}/kick",
                action.GetCustomAttribute<RouteAttribute>()?.Template);
        }

        [Fact]
        public async Task Owner_kicks_player_with_subject_trimmed_reason_and_exact_success_contract()
        {
            var actions = new TestPlayerActions();
            var audit = new TestPlayerActionAuditTrail();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                playerActions: actions,
                playerActionAuditTrail: audit);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateKickPlayerRequest(
                url,
                7,
                ValidKickBody.Replace("rule violation", "  rule violation  ")))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(
                    new[] { "completedAtUtc", "operationId", "requestedAtUtc", "status", "target" },
                    payload.Properties().Select(property => property.Name).OrderBy(name => name));
                Assert.Matches("^[0-9a-f]{32}$", (string?)payload["operationId"] ?? string.Empty);
                Assert.Equal("succeeded", (string?)payload["status"]);
                Assert.Equal(7, (int?)payload["target"]?["entityId"]);
                Assert.Equal("Alice", (string?)payload["target"]?["name"]);
                Assert.Equal("steam-1", (string?)payload["target"]?["platformIdentity"]?["combinedId"]);
                Assert.Equal("Steam", (string?)payload["target"]?["platformIdentity"]?["platform"]);
                Assert.NotNull((DateTimeOffset?)payload["requestedAtUtc"]);
                Assert.NotNull((DateTimeOffset?)payload["completedAtUtc"]);
                Assert.Equal("test-owner-subject", audit.Intent?.ActorSubject);
                Assert.Equal("rule violation", audit.Intent?.Reason);
                Assert.Equal(7, actions.Command?.EntityId);
                Assert.Equal("steam-1", actions.Command?.ExpectedPlatformIdentity.CombinedId);
            }
        }

        [Fact]
        public async Task Overlong_kick_reason_is_rejected_without_audit_or_action()
        {
            var body = ValidKickBody.Replace("rule violation", new string('x', 201));
            await AssertKickProblemAsync(
                body,
                HttpStatusCode.BadRequest,
                "invalid_player_kick_reason",
                new TestPlayerActions(),
                new TestPlayerActionAuditTrail(),
                expectedActionCalls: 0,
                expectedAuditCalls: 0);
        }

        [Fact]
        public async Task Game_not_ready_rejects_kick_without_audit_or_action()
        {
            var actions = new TestPlayerActions();
            var audit = new TestPlayerActionAuditTrail();
            await AssertKickProblemAsync(
                ValidKickBody,
                HttpStatusCode.ServiceUnavailable,
                "game_not_ready",
                actions,
                audit,
                expectedActionCalls: 0,
                expectedAuditCalls: 0,
                gameReadiness: GameReadinessState.Loading);
        }

        public static IEnumerable<object[]> KickFailureCases()
        {
            yield return new object[]
            {
                new TestPlayerActions(result: KickPlayerActionResult.PlayerNotOnline()),
                new TestPlayerActionAuditTrail(),
                HttpStatusCode.Conflict,
                "player_not_online",
                null!
            };
            yield return new object[]
            {
                new TestPlayerActions(result: KickPlayerActionResult.PlayerIdentityChanged(
                    7,
                    "Other",
                    new PlayerPlatformIdentity("steam-2", "Steam"))),
                new TestPlayerActionAuditTrail(),
                HttpStatusCode.Conflict,
                "player_identity_changed",
                null!
            };
            yield return new object[]
            {
                new TestPlayerActions(failure: new TimeoutException("internal timeout detail")),
                new TestPlayerActionAuditTrail(),
                HttpStatusCode.ServiceUnavailable,
                "game_thread_timeout",
                "internal timeout detail"
            };
            yield return new object[]
            {
                new TestPlayerActions(),
                new TestPlayerActionAuditTrail(createFailure: new InvalidOperationException("database path detail")),
                HttpStatusCode.ServiceUnavailable,
                "audit_unavailable",
                "database path detail"
            };
            yield return new object[]
            {
                new TestPlayerActions(),
                new TestPlayerActionAuditTrail(completeResult: false),
                HttpStatusCode.ServiceUnavailable,
                "audit_completion_unavailable",
                null!
            };
            yield return new object[]
            {
                new TestPlayerActions(failure: new InvalidOperationException("native failure detail")),
                new TestPlayerActionAuditTrail(),
                HttpStatusCode.InternalServerError,
                "player_kick_failed",
                "native failure detail"
            };
        }

        [Theory]
        [MemberData(nameof(KickFailureCases))]
        public async Task Kick_failures_return_stable_problem_details_without_internal_messages(
            TestPlayerActions actions,
            TestPlayerActionAuditTrail audit,
            HttpStatusCode expectedStatus,
            string expectedCode,
            string? forbiddenDetail)
        {
            var payload = await AssertKickProblemAsync(
                ValidKickBody,
                expectedStatus,
                expectedCode,
                actions,
                audit,
                expectedActionCalls: expectedCode == "audit_unavailable" ? 0 : 1,
                expectedAuditCalls: 1);

            if (forbiddenDetail != null)
                Assert.DoesNotContain(forbiddenDetail, payload.ToString());
        }

        [Fact]
        public async Task Concurrent_kick_returns_busy_without_a_second_audit_or_action()
        {
            var pendingResult = new TaskCompletionSource<KickPlayerActionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var actions = new TestPlayerActions(pendingResult: pendingResult);
            var audit = new TestPlayerActionAuditTrail();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                playerActions: actions,
                playerActionAuditTrail: audit);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var firstRequest = CreateKickPlayerRequest(url, 7, ValidKickBody))
            using (var secondRequest = CreateKickPlayerRequest(url, 8, ValidKickBody))
            {
                firstRequest.Headers.Authorization = CreateBasicAuthorization();
                secondRequest.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                var firstResponseTask = client.SendAsync(
                    firstRequest,
                    TestContext.Current.CancellationToken);
                Assert.True(SpinWait.SpinUntil(
                    () => actions.CallCount == 1,
                    TimeSpan.FromSeconds(5)));

                using var secondResponse = await client.SendAsync(
                    secondRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, secondResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    secondResponse,
                    "player_action_busy",
                    "/api/v1/players/8/kick");
                Assert.Equal(1, actions.CallCount);
                Assert.Equal(1, audit.CreatePendingCallCount);

                pendingResult.SetResult(KickPlayerActionResult.Succeeded(
                    7,
                    "Alice",
                    new PlayerPlatformIdentity("steam-1", "Steam")));
                using var firstResponse = await firstResponseTask;
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            }
        }

        private static async Task<JObject> AssertKickProblemAsync(
            string body,
            HttpStatusCode expectedStatus,
            string expectedCode,
            TestPlayerActions actions,
            TestPlayerActionAuditTrail audit,
            int expectedActionCalls,
            int expectedAuditCalls,
            GameReadinessState gameReadiness = GameReadinessState.Ready)
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                gameReadiness: gameReadiness,
                playerActions: actions,
                playerActionAuditTrail: audit);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateKickPlayerRequest(url, 7, body))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(expectedStatus, response.StatusCode);
                var payload = await AssertProblemDetailsAsync(
                    response,
                    expectedCode,
                    "/api/v1/players/7/kick");
                Assert.Equal(expectedActionCalls, actions.CallCount);
                Assert.Equal(expectedAuditCalls, audit.CreatePendingCallCount);
                return payload;
            }
        }

        [Fact]
        public async Task Admin_assets_spa_routes_and_api_precedence_run_in_real_katana_host()
        {
            var assetRoot = Path.Combine(Path.GetTempPath(), "7dpanel-admin-" + Guid.NewGuid().ToString("N"));
            var assetsDirectory = Path.Combine(assetRoot, "assets");
            var conflictingApiDirectory = Path.Combine(assetRoot, "api", "v1");
            Directory.CreateDirectory(assetsDirectory);
            Directory.CreateDirectory(conflictingApiDirectory);
            File.WriteAllText(Path.Combine(assetRoot, "index.html"), "<html><body>7DPanel Admin</body></html>");
            File.WriteAllText(Path.Combine(assetsDirectory, "app.js"), "window.panelLoaded = true;");
            File.WriteAllText(Path.Combine(conflictingApiDirectory, "health"), "static content must not win");

            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            try
            {
                using (var host = new OwinWebHost(
                    url,
                    app => OwinStartup.Configure(app, provider, assetRoot)))
                using (var handler = new HttpClientHandler { UseProxy = false })
                using (var client = new HttpClient(handler))
                {
                    host.Start();

                    var rootResponse = await client.GetAsync(url, TestContext.Current.CancellationToken);
                    var rootBody = await rootResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
                    Assert.Contains("7DPanel Admin", rootBody);

                    var spaResponse = await client.GetAsync(url + "overview", TestContext.Current.CancellationToken);
                    var spaBody = await spaResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, spaResponse.StatusCode);
                    Assert.Contains("7DPanel Admin", spaBody);

                    var assetResponse = await client.GetAsync(url + "assets/app.js", TestContext.Current.CancellationToken);
                    var assetBody = await assetResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
                    Assert.Contains("panelLoaded", assetBody);

                    var missingAssetResponse = await client.GetAsync(url + "assets/missing.js", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingAssetResponse.StatusCode);

                    var missingExtensionAssetResponse = await client.GetAsync(url + "assets/missing", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingExtensionAssetResponse.StatusCode);

                    var assetsDirectoryResponse = await client.GetAsync(url + "assets/", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, assetsDirectoryResponse.StatusCode);

                    var apiResponse = await client.GetAsync(url + "api/v1/health", TestContext.Current.CancellationToken);
                    var apiBody = await apiResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
                    AssertHealthContract(apiBody);
                    Assert.DoesNotContain("static content must not win", apiBody);

                    var missingApiResponse = await client.GetAsync(url + "api/v1/missing", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, missingApiResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        missingApiResponse,
                        "resource_not_found",
                        "/api/v1/missing");

                    var missingSwaggerResponse = await client.GetAsync(
                        url + "swagger/missing",
                        TestContext.Current.CancellationToken);
                    var missingSwaggerBody = await missingSwaggerResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.NotFound, missingSwaggerResponse.StatusCode);
                    Assert.DoesNotContain("7DPanel Admin", missingSwaggerBody);
                }
            }
            finally
            {
                Directory.Delete(assetRoot, true);
            }
        }

        [Theory]
        [InlineData("client-request.123", "client-request.123")]
        [InlineData("invalid request id", null)]
        [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789---", null)]
        public async Task Api_problem_details_use_validated_request_id(
            string suppliedRequestId,
            string? expectedRequestId)
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url + "api/v1/missing?secret=value"))
            {
                request.Headers.TryAddWithoutValidation("X-Request-ID", suppliedRequestId);
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var problem = await AssertProblemDetailsAsync(
                    response,
                    "resource_not_found",
                    "/api/v1/missing");
                var responseRequestId = Assert.Single(response.Headers.GetValues("X-Request-ID"));

                Assert.Equal(responseRequestId, (string?)problem["traceId"]);
                if (expectedRequestId == null)
                {
                    Assert.Matches("^[0-9a-f]{32}$", responseRequestId);
                    Assert.NotEqual(suppliedRequestId, responseRequestId);
                }
                else
                {
                    Assert.Equal(expectedRequestId, responseRequestId);
                }
            }
        }

        [Fact]
        public async Task Health_endpoint_remains_available_when_admin_assets_are_missing()
        {
            var missingAssetRoot = Path.Combine(Path.GetTempPath(), "7dpanel-missing-admin-" + Guid.NewGuid().ToString("N"));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(false, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider, missingAssetRoot)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();

                var apiResponse = await client.GetAsync(url + "api/v1/health", TestContext.Current.CancellationToken);
                var apiBody = await apiResponse.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
                AssertHealthContract(apiBody);

                var rootResponse = await client.GetAsync(url, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NotFound, rootResponse.StatusCode);
            }
        }

        [Fact]
        public async Task Production_event_stream_rejects_unauthenticated_request()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out var hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();

                var response = await client.GetAsync(
                    url + "api/v1/events/stream",
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/events/stream");
                var challenges = response.Headers.WwwAuthenticate
                    .Select(value => value.Scheme)
                    .ToArray();
                Assert.Contains("Basic", challenges);
                Assert.Contains("Bearer", challenges);
                Assert.Equal(0, hub.SubscriberCount);
            }
        }

        [Theory]
        [InlineData("Basic", "dGVzdC1vd25lcjp3cm9uZy1wYXNzd29yZA==")]
        [InlineData("Bearer", "not-a-valid-token")]
        public async Task Production_event_stream_rejects_invalid_credentials(
            string scheme,
            string parameter)
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out var hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(scheme, parameter);
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/events/stream");
                Assert.Equal(0, hub.SubscriberCount);
            }
        }

        [Fact]
        public async Task Basic_credentials_are_rejected_over_http_by_default()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                out var hub,
                allowInsecureHttp: false);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/events/stream");
                Assert.Equal(0, hub.SubscriberCount);
            }
        }

        [Fact]
        public async Task Basic_event_stream_replays_after_last_event_id_and_releases_subscription()
        {
            var window = new ServerEventLiveWindow(4);
            var hub = new ServerEventHub(window);
            hub.Publish(window.AppendConsoleLog(CreateConsoleLogEntry("one")));
            hub.Publish(window.AppendConsoleLog(CreateConsoleLogEntry("two")));
            hub.Publish(window.AppendGameReady(
                new DateTime(2026, 7, 21, 8, 9, 10, DateTimeKind.Utc)));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", "1");
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    TestContext.Current.CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                    Assert.True(response.Headers.CacheControl?.NoCache);
                    Assert.True(response.Headers.CacheControl?.NoStore);
                    Assert.Equal(
                        "no",
                        Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        Assert.Equal("event: welcome", await ReadLineWithTimeoutAsync(reader));
                        var welcome = await ReadLineWithTimeoutAsync(reader);
                        Assert.Contains("\"product\":\"7DPanel\"", welcome);
                        Assert.Contains("\"version\":\"0.1.0\"", welcome);
                        Assert.Contains("\"hostState\":\"running\"", welcome);
                        Assert.Contains("\"gameReadiness\":\"ready\"", welcome);
                        Assert.Contains("\"connectedAtUtc\":", welcome);
                        Assert.Equal(string.Empty, await ReadLineWithTimeoutAsync(reader));
                        Assert.Equal("id: 2", await ReadLineWithTimeoutAsync(reader));
                        Assert.Equal("event: console-log", await ReadLineWithTimeoutAsync(reader));
                        var data = await ReadLineWithTimeoutAsync(reader);
                        Assert.StartsWith("data: ", data);
                        Assert.Contains("\"sequence\":2", data);
                        Assert.Contains("\"message\":\"two\"", data);
                        Assert.Equal(string.Empty, await ReadLineWithTimeoutAsync(reader));
                        Assert.Equal("id: 3", await ReadLineWithTimeoutAsync(reader));
                        Assert.Equal("event: game-ready", await ReadLineWithTimeoutAsync(reader));
                        Assert.Contains("\"sequence\":3", await ReadLineWithTimeoutAsync(reader));
                        Assert.Equal(string.Empty, await ReadLineWithTimeoutAsync(reader));
                    }
                }

                Assert.True(SpinWait.SpinUntil(
                    () => hub.SubscriberCount == 0,
                    TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public async Task Basic_event_stream_reports_replay_gap()
        {
            var window = new ServerEventLiveWindow(2);
            var hub = new ServerEventHub(window);
            hub.Publish(window.AppendConsoleLog(CreateConsoleLogEntry("one")));
            hub.Publish(window.AppendConsoleLog(CreateConsoleLogEntry("two")));
            hub.Publish(window.AppendConsoleLog(CreateConsoleLogEntry("three")));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", "0");
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using (var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    TestContext.Current.CancellationToken))
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    Assert.Equal("event: welcome", await ReadLineWithTimeoutAsync(reader));
                    Assert.StartsWith("data: ", await ReadLineWithTimeoutAsync(reader));
                    Assert.Equal(string.Empty, await ReadLineWithTimeoutAsync(reader));
                    Assert.Equal("event: gap", await ReadLineWithTimeoutAsync(reader));
                    Assert.Contains("\"afterSequence\":0", await ReadLineWithTimeoutAsync(reader));
                    Assert.Equal(string.Empty, await ReadLineWithTimeoutAsync(reader));
                    Assert.Equal("id: 2", await ReadLineWithTimeoutAsync(reader));
                }
            }
        }

        [Fact]
        public async Task Basic_event_stream_rejects_invalid_last_event_id()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(2));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.TryAddWithoutValidation("Last-Event-ID", "invalid");
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "invalid_event_cursor",
                    "/api/v1/events/stream");
            }
        }

        [Fact]
        public async Task Event_stream_capacity_is_rejected_before_the_response_body_starts()
        {
            var hub = new ServerEventHub(new ServerEventLiveWindow(2), 1);
            Assert.True(hub.TrySubscribe(1, out var occupied));
            using var occupiedSubscription = Assert.IsAssignableFrom<IServerEventSubscription>(occupied);
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.Authorization = CreateBasicAuthorization();
                host.Start();

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "stream_capacity_exhausted",
                    "/api/v1/events/stream");
                Assert.Equal(1, hub.SubscriberCount);
            }
        }

        [Fact]
        public async Task Password_grant_bearer_token_authorizes_event_stream()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out var hub);

            using (var host = new OwinWebHost(
                url,
                app =>
                {
                    app.SetDataProtectionProvider(new ThrowingDataProtectionProvider());
                    OwinStartup.Configure(app, provider);
                }))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                using var tokenContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "password"),
                    new KeyValuePair<string, string>("username", "test-owner"),
                    new KeyValuePair<string, string>("password", "test-password")
                });
                using var tokenResponse = await client.PostAsync(
                    url + "api/v1/auth/token",
                    tokenContent,
                    TestContext.Current.CancellationToken);
                var tokenPayload = JObject.Parse(await tokenResponse.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
                Assert.Equal("bearer", ((string?)tokenPayload["token_type"])?.ToLowerInvariant());
                var accessToken = (string?)tokenPayload["access_token"];
                Assert.False(string.IsNullOrWhiteSpace(accessToken));

                using var request = new HttpRequestMessage(HttpMethod.Get, url + "api/v1/events/stream");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer",
                    accessToken!);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                response.Dispose();
                Assert.True(SpinWait.SpinUntil(
                    () => hub.SubscriberCount == 0,
                    TimeSpan.FromSeconds(5)));
            }
        }

        [Fact]
        public async Task Query_string_bearer_token_does_not_authorize_event_stream()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out var hub);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var accessToken = await IssueAccessTokenAsync(client, url);

                using var response = await client.GetAsync(
                    url + "api/v1/events/stream?access_token=" + Uri.EscapeDataString(accessToken),
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/events/stream");
                Assert.Equal(0, hub.SubscriberCount);
            }
        }

        [Fact]
        public async Task OAuth_invalid_grant_remains_an_oauth_protocol_response()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                using var tokenContent = CreateTokenContent("wrong-password");
                using var response = await client.PostAsync(
                    url + "api/v1/auth/token",
                    tokenContent,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal("invalid_grant", (string?)payload["error"]);
                Assert.False(string.IsNullOrWhiteSpace((string?)payload["error_description"]));
                Assert.Null(payload["code"]);
                Assert.NotEqual(
                    "application/problem+json",
                    response.Content.Headers.ContentType?.MediaType);
            }
        }

        [Fact]
        public async Task Authentication_rate_limit_returns_problem_details_with_retry_after()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    using var tokenContent = CreateTokenContent("wrong-password");
                    using var response = await client.PostAsync(
                        url + "api/v1/auth/token",
                        tokenContent,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                }

                using var limitedContent = CreateTokenContent("wrong-password");
                using var limitedResponse = await client.PostAsync(
                    url + "api/v1/auth/token",
                    limitedContent,
                    TestContext.Current.CancellationToken);

                Assert.Equal((HttpStatusCode)429, limitedResponse.StatusCode);
                Assert.True(limitedResponse.Headers.TryGetValues("Retry-After", out var values));
                Assert.True(int.Parse(Assert.Single(values)) >= 1);
                await AssertProblemDetailsAsync(
                    limitedResponse,
                    "too_many_requests",
                    "/api/v1/auth/token");
            }
        }

        private static void AssertHealthContract(string body)
        {
            var payload = JObject.Parse(body);
            var propertyNames = payload.Properties().Select(property => property.Name).ToArray();

            Assert.Equal(3, propertyNames.Length);
            Assert.Contains("status", propertyNames);
            Assert.Contains("product", propertyNames);
            Assert.Contains("version", propertyNames);
            Assert.DoesNotContain("Status", propertyNames);
            Assert.DoesNotContain("Product", propertyNames);
            Assert.DoesNotContain("Version", propertyNames);
            Assert.Equal("ok", (string?)payload["status"]);
            Assert.Equal("7DPanel", (string?)payload["product"]);
            Assert.Equal("0.1.0", (string?)payload["version"]);
        }

        private static async Task<JObject> AssertProblemDetailsAsync(
            HttpResponseMessage response,
            string expectedCode,
            string expectedInstance)
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal((int)response.StatusCode, (int?)payload["status"]);
            Assert.Equal(expectedCode, (string?)payload["code"]);
            Assert.Equal(expectedInstance, (string?)payload["instance"]);
            Assert.Equal("about:blank", (string?)payload["type"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)payload["title"]));
            Assert.False(string.IsNullOrWhiteSpace((string?)payload["detail"]));
            Assert.False(string.IsNullOrWhiteSpace((string?)payload["traceId"]));
            return payload;
        }

        private static async Task<JObject> GetOpenApiDocumentAsync(
            HttpClient client,
            string url)
        {
            using var response = await client.GetAsync(
                url + "swagger/v1/swagger.json",
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JObject.Parse(body);
        }

        private static void AssertAlternativeSecurity(
            JObject document,
            string path,
            string method)
        {
            var security = Assert.IsType<JArray>(document["paths"]?[path]?[method]?["security"]);
            Assert.Equal(2, security.Count);
            Assert.Contains(security, requirement => requirement?["Basic"] is JArray);
            Assert.Contains(security, requirement => requirement?["Bearer"] is JArray);
            Assert.All(security, requirement => Assert.Single(((JObject)requirement!).Properties()));
        }

        private static void AssertProblemResponses(
            JObject document,
            string path,
            string method,
            params string[] statusCodes)
        {
            foreach (var statusCode in statusCodes)
            {
                Assert.Equal(
                    "#/components/schemas/ApiProblemDetails",
                    (string?)document["paths"]?[path]?[method]?["responses"]?[statusCode]?
                        ["content"]?["application/problem+json"]?["schema"]?["$ref"]);
            }
        }

        private static void AssertResponseCodes(
            JObject document,
            string path,
            string method,
            params string[] statusCodes)
        {
            Assert.Equal(
                statusCodes.OrderBy(statusCode => statusCode).ToArray(),
                document["paths"]?[path]?[method]?["responses"]!
                    .Children<JProperty>()
                    .Select(property => property.Name)
                    .OrderBy(statusCode => statusCode)
                    .ToArray());
        }

        private static ConsoleLogEntry CreateConsoleLogEntry(string message) =>
            new ConsoleLogEntry(
                "formatted:" + message,
                message,
                string.Empty,
                ConsoleLogType.Log,
                new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
                1L);

        private static System.Net.Http.Headers.AuthenticationHeaderValue CreateBasicAuthorization()
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-owner:test-password"));
            return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
        }

        private static HttpRequestMessage CreateConsoleCommandRequest(
            string url,
            string command)
        {
            return new HttpRequestMessage(
                HttpMethod.Post,
                url + "api/v1/console/commands")
            {
                Content = new StringContent(
                    "{\"command\":" + Newtonsoft.Json.JsonConvert.SerializeObject(command) + "}",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static HttpRequestMessage CreatePlayersRequest(string url)
        {
            return new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/players/online");
        }

        private const string ValidKickBody =
            "{\"expectedPlatformIdentity\":{\"combinedId\":\"steam-1\",\"platform\":\"Steam\"},\"reason\":\"rule violation\",\"confirmed\":true}";

        private static HttpRequestMessage CreateKickPlayerRequest(
            string url,
            int entityId,
            string body)
        {
            return new HttpRequestMessage(
                HttpMethod.Post,
                url + "api/v1/players/" + entityId + "/kick")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }

        private static FormUrlEncodedContent CreateTokenContent(string password) =>
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", "test-owner"),
                new KeyValuePair<string, string>("password", password)
            });

        private static async Task<string> IssueAccessTokenAsync(HttpClient client, string url)
        {
            using var tokenContent = CreateTokenContent("test-password");
            using var tokenResponse = await client.PostAsync(
                url + "api/v1/auth/token",
                tokenContent,
                TestContext.Current.CancellationToken);
            var tokenPayload = JObject.Parse(await tokenResponse.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            return Assert.IsType<string>((string?)tokenPayload["access_token"]);
        }

        private static ServiceProvider CreateWebServiceProvider(
            bool enableConsoleLogStream,
            out ServerEventHub hub,
            bool allowInsecureHttp = true)
        {
            hub = new ServerEventHub(new ServerEventLiveWindow(4));
            return CreateWebServiceProvider(enableConsoleLogStream, hub, allowInsecureHttp);
        }

        private static ServiceProvider CreateWebServiceProvider(
            bool enableConsoleLogStream,
            ServerEventHub hub,
            bool allowInsecureHttp = true,
            IConsoleCommandGateway? consoleGateway = null,
            GameReadinessState gameReadiness = GameReadinessState.Ready,
            IOnlinePlayerQuery? onlinePlayerQuery = null,
            IPlayerActions? playerActions = null,
            IPlayerActionAuditTrail? playerActionAuditTrail = null)
        {
            var services = new ServiceCollection();
            var authentication = enableConsoleLogStream
                ? PanelAuthenticationOptions.FromBinding(
                    true,
                    "test-owner",
                    "test-password",
                    allowInsecureHttp: allowInsecureHttp)
                : PanelAuthenticationOptions.Disabled;
            services.AddSingleton(PanelHostOptions.FromBinding(
                18080,
                "127.0.0.1",
                "http",
                authentication));
            services.AddSingleton<IServerEventStream>(hub);
            services.AddSingleton<IPanelRuntimeStatus>(
                new TestPanelRuntimeStatus(ModHostState.Running, gameReadiness));
            services.AddSingleton(
                consoleGateway ?? new TestConsoleCommandGateway());
            services.AddSingleton<ExecuteConsoleCommandUseCase>();
            services.AddSingleton<IOnlinePlayerQuery>(onlinePlayerQuery ?? new TestOnlinePlayerQuery());
            services.AddSingleton<GetOnlinePlayersUseCase>();
            services.AddSingleton<IPlayerActions>(playerActions ?? new TestPlayerActions());
            services.AddSingleton<IPlayerActionAuditTrail>(
                playerActionAuditTrail ?? new TestPlayerActionAuditTrail());
            services.AddSingleton<KickPlayerUseCase>();
            var authenticationStore = new TestPanelAuthenticationStore();
            services.AddSingleton<IPanelCredentialStore>(authenticationStore);
            services.AddSingleton<IPanelAccessTokenStore>(authenticationStore);
            services.AddScoped<ServerEventSseSession>();
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        private sealed class TestOnlinePlayerQuery : IOnlinePlayerQuery
        {
            private readonly OnlinePlayersSnapshot? snapshot;
            private readonly Exception? failure;

            public TestOnlinePlayerQuery()
            {
            }

            public TestOnlinePlayerQuery(OnlinePlayersSnapshot snapshot)
            {
                this.snapshot = snapshot;
            }

            public TestOnlinePlayerQuery(Type exceptionType)
            {
                failure = (Exception)Activator.CreateInstance(exceptionType)!;
            }

            public int CallCount { get; private set; }

            public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken)
            {
                CallCount++;
                if (failure != null)
                    return Task.FromException<OnlinePlayersSnapshot>(failure);
                return Task.FromResult(snapshot ?? new OnlinePlayersSnapshot(
                    DateTimeOffset.UtcNow,
                    Array.Empty<PlayerSnapshot>()));
            }
        }

        public sealed class TestPlayerActions : IPlayerActions
        {
            private readonly KickPlayerActionResult? result;
            private readonly Exception? failure;
            private readonly TaskCompletionSource<KickPlayerActionResult>? pendingResult;

            public TestPlayerActions(
                KickPlayerActionResult? result = null,
                Exception? failure = null,
                TaskCompletionSource<KickPlayerActionResult>? pendingResult = null)
            {
                this.result = result;
                this.failure = failure;
                this.pendingResult = pendingResult;
            }

            public int CallCount { get; private set; }

            public KickPlayerCommand? Command { get; private set; }

            public Task<KickPlayerActionResult> KickAsync(
                KickPlayerCommand command,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Command = command;
                if (failure != null)
                    return Task.FromException<KickPlayerActionResult>(failure);
                if (pendingResult != null)
                    return pendingResult.Task;
                return Task.FromResult(result ?? KickPlayerActionResult.Succeeded(
                    command.EntityId,
                    "Alice",
                    command.ExpectedPlatformIdentity));
            }
        }

        public sealed class TestPlayerActionAuditTrail : IPlayerActionAuditTrail
        {
            private readonly Exception? createFailure;
            private readonly bool completeResult;

            public TestPlayerActionAuditTrail(
                Exception? createFailure = null,
                bool completeResult = true)
            {
                this.createFailure = createFailure;
                this.completeResult = completeResult;
            }

            public int CreatePendingCallCount { get; private set; }

            public PlayerActionAuditIntent? Intent { get; private set; }

            public void CreatePending(PlayerActionAuditIntent intent)
            {
                CreatePendingCallCount++;
                if (createFailure != null) throw createFailure;
                Intent = intent;
            }

            public bool TryComplete(PlayerActionAuditCompletion completion) => completeResult;

            public int MarkPendingUnknown(DateTimeOffset completedAtUtc) => 0;
        }

        private sealed class TestConsoleCommandGateway : IConsoleCommandGateway
        {
            private readonly Exception? failure;

            public TestConsoleCommandGateway(Exception? failure = null)
            {
                this.failure = failure;
            }

            public LSTY.SevenDPanel.Application.ConsoleCommands.ConsoleCommandRequest?
                Request { get; private set; }

            public Task<ConsoleCommandResult> ExecuteAsync(
                LSTY.SevenDPanel.Application.ConsoleCommands.ConsoleCommandRequest request,
                CancellationToken cancellationToken)
            {
                Request = request;
                if (failure != null)
                    return Task.FromException<ConsoleCommandResult>(failure);
                return Task.FromResult(new ConsoleCommandResult(
                    request.Command,
                    new[] { "command output" }));
            }
        }

        private sealed class ConcurrentConsoleCommandGateway : IConsoleCommandGateway
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, TaskCompletionSource<ConsoleCommandResult>> pending =
                new Dictionary<string, TaskCompletionSource<ConsoleCommandResult>>(StringComparer.Ordinal);

            public ManualResetEventSlim BothReceived { get; } = new ManualResetEventSlim();
            public IReadOnlyCollection<string> Commands
            {
                get
                {
                    lock (sync) return pending.Keys.ToArray();
                }
            }

            public Task<ConsoleCommandResult> ExecuteAsync(
                LSTY.SevenDPanel.Application.ConsoleCommands.ConsoleCommandRequest request,
                CancellationToken cancellationToken)
            {
                lock (sync)
                {
                    var completion = new TaskCompletionSource<ConsoleCommandResult>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    pending.Add(request.Command, completion);
                    if (pending.Count == 2) BothReceived.Set();
                    return completion.Task;
                }
            }

            public void Complete(string command)
            {
                TaskCompletionSource<ConsoleCommandResult> completion;
                lock (sync) completion = pending[command];
                completion.SetResult(new ConsoleCommandResult(
                    command,
                    new[] { command + "-output" }));
            }
        }

        private sealed class TestPanelRuntimeStatus : IPanelRuntimeStatus
        {
            public TestPanelRuntimeStatus(ModHostState state, GameReadinessState gameReadiness)
            {
                State = state;
                GameReadiness = gameReadiness;
            }

            public ModHostState State { get; }
            public GameReadinessState GameReadiness { get; }
        }

        private sealed class TestPanelAuthenticationStore :
            IPanelCredentialStore,
            IPanelAccessTokenStore
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, StoredAccessToken> tokens =
                new Dictionary<string, StoredAccessToken>(StringComparer.Ordinal);
            private readonly PanelUserIdentity identity =
                new PanelUserIdentity("test-owner-subject", "test-owner");

            public bool TryVerify(
                string username,
                string password,
                out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!string.Equals(username, identity.Username, StringComparison.Ordinal) ||
                    !string.Equals(password, "test-password", StringComparison.Ordinal))
                {
                    return false;
                }

                panelIdentity = identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!string.Equals(subject, identity.Subject, StringComparison.Ordinal))
                    return false;

                panelIdentity = identity;
                return true;
            }

            public string Issue(
                PanelUserIdentity panelIdentity,
                DateTimeOffset issuedUtc,
                DateTimeOffset expiresUtc)
            {
                var token = "test-token-" + Guid.NewGuid().ToString("N");
                lock (sync)
                {
                    tokens.Add(
                        token,
                        new StoredAccessToken(panelIdentity, issuedUtc, expiresUtc));
                }

                return token;
            }

            public bool TryValidate(
                string token,
                DateTimeOffset utcNow,
                out StoredAccessToken accessToken)
            {
                lock (sync)
                {
                    accessToken = null!;
                    if (!tokens.TryGetValue(token, out var candidate) ||
                        candidate.ExpiresUtc <= utcNow)
                    {
                        return false;
                    }

                    accessToken = candidate;
                    return true;
                }
            }
        }

        private sealed class ThrowingDataProtectionProvider : IDataProtectionProvider
        {
            public IDataProtector Create(params string[] purposes) =>
                throw new PlatformNotSupportedException(
                    "The test host does not provide a default data protector.");
        }

        private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader)
        {
            var read = reader.ReadLineAsync();
            var completed = await Task.WhenAny(
                read,
                Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Same(read, completed);
            return await read;
        }

        private static int GetAvailablePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
