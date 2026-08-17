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
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
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
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Web")]
    public sealed partial class OwinWebHostTests
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
                Assert.NotNull(document["paths"]?["/api/v1/audit"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/game-events"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/chat/mutes"]?["get"]);
                Assert.NotNull(document["paths"]?["/api/v1/chat/mutes"]?["post"]);
                Assert.NotNull(document["paths"]?["/api/v1/chat/mutes/{crossplatformId}"]?["put"]);
                Assert.NotNull(document["paths"]?["/api/v1/chat/mutes/{crossplatformId}"]?["delete"]);
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
                Assert.NotNull(successSchema?["properties"]?["username"]);
                Assert.NotNull(successSchema?["properties"]?["role"]);
                Assert.Equal(
                    new[] { "access_token", "expires_in", "role", "token_type", "username" },
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
        public async Task Openapi_document_describes_bearer_security_without_basic()
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

                Assert.Null(document["components"]?["securitySchemes"]?["Basic"]);
                Assert.Equal("http", (string?)document["components"]?["securitySchemes"]?["Bearer"]?["type"]);
                Assert.Equal("bearer", (string?)document["components"]?["securitySchemes"]?["Bearer"]?["scheme"]);

                Assert.Null(document["paths"]?["/health"]?["get"]?["security"]);
                Assert.Null(document["paths"]?["/api/v1/health"]?["get"]?["security"]);
                Assert.Null(document["paths"]?["/api/v1/auth/token"]?["post"]?["security"]);
                AssertBearerSecurity(document, "/api/v1/events/stream", "get");
                AssertBearerSecurity(document, "/api/v1/console/commands", "post");
                AssertBearerSecurity(document, "/api/v1/players/online", "get");
                AssertBearerSecurity(document, "/api/v1/players/{entityId}/kick", "post");
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
        public async Task Openapi_document_describes_api_key_management_contract()
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
                var post = document["paths"]?["/api/v1/api-keys"]?["post"];
                var delete = document["paths"]?["/api/v1/api-keys/{keyId}"]?["delete"];

                Assert.NotNull(document["paths"]?["/api/v1/api-keys"]?["get"]);
                Assert.NotNull(post);
                Assert.NotNull(delete);
                AssertBearerSecurity(document, "/api/v1/api-keys", "get");
                AssertBearerSecurity(document, "/api/v1/api-keys", "post");
                AssertBearerSecurity(document, "/api/v1/api-keys/{keyId}", "delete");
                Assert.Contains("Access Token", Assert.IsType<string>((string?)post!["description"]));
                Assert.Contains("Access Token", Assert.IsType<string>((string?)delete!["description"]));
                Assert.Equal(
                    new[] { "apiKey", "createdAtUtc", "expiresAtUtc", "id", "name" },
                    post["responses"]?["201"]?["content"]?["application/json"]?["schema"]?
                        ["properties"]?
                        .Children<JProperty>()
                        .Select(property => property.Name)
                        .OrderBy(name => name)
                        .ToArray());
                Assert.Equal(
                    new[] { "apiKey", "createdAtUtc", "expiresAtUtc", "id", "name" },
                    post["responses"]?["201"]?["content"]?["application/json"]?["schema"]?
                        ["required"]?
                        .Values<string>()
                        .OrderBy(name => name)
                        .ToArray());
                Assert.Equal(
                    "no-store",
                    (string?)post["responses"]?["201"]?["headers"]?["Cache-Control"]?["example"]);
                Assert.True((bool?)post["responses"]?["201"]?["headers"]?["Cache-Control"]?["required"]);
                Assert.True((bool?)post["responses"]?["201"]?["content"]?["application/json"]?
                    ["schema"]?["properties"]?["expiresAtUtc"]?["nullable"]);
                var metadataSchema = Assert.IsType<JObject>(
                    document["paths"]?["/api/v1/api-keys"]?["get"]?
                        ["responses"]?["200"]?["content"]?["application/json"]?["schema"]?["items"]);
                Assert.Equal(
                    new[]
                    {
                        "createdAtUtc",
                        "displayPrefix",
                        "expiresAtUtc",
                        "id",
                        "lastUsedAtUtc",
                        "name",
                        "status"
                    },
                    metadataSchema?["properties"]?
                        .Children<JProperty>()
                        .Select(property => property.Name)
                        .OrderBy(name => name)
                        .ToArray());
                Assert.Equal(
                    new[]
                    {
                        "createdAtUtc",
                        "displayPrefix",
                        "expiresAtUtc",
                        "id",
                        "lastUsedAtUtc",
                        "name",
                        "status"
                    },
                    metadataSchema?["required"]?
                        .Values<string>()
                        .OrderBy(name => name)
                        .ToArray());
                Assert.True((bool?)metadataSchema?["properties"]?["lastUsedAtUtc"]?["nullable"]);
                Assert.True((bool?)metadataSchema?["properties"]?["expiresAtUtc"]?["nullable"]);
                Assert.Equal(
                    new[] { "active", "expired", "revoked" },
                    metadataSchema?["properties"]?["status"]?["enum"]?
                        .Values<string>()
                        .OrderBy(status => status)
                        .ToArray());
                AssertProblemResponses(document, "/api/v1/api-keys", "get", "401", "403", "500");
                AssertProblemResponses(document, "/api/v1/api-keys", "post", "400", "401", "403", "409", "415", "500");
                AssertProblemResponses(document, "/api/v1/api-keys/{keyId}", "delete", "401", "403", "404", "500");
                AssertResponseCodes(document, "/api/v1/api-keys", "get", "200", "401", "403", "500");
                AssertResponseCodes(document, "/api/v1/api-keys", "post", "201", "400", "401", "403", "409", "415", "500");
                AssertResponseCodes(document, "/api/v1/api-keys/{keyId}", "delete", "204", "401", "403", "404", "500");
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
                AssertProblemResponses(document, "/api/v1/server-operations/restart", "post", "400", "401", "403", "409", "500", "503");
                AssertProblemResponses(document, "/api/v1/server-operations/shutdown", "post", "400", "401", "403", "409", "500", "503");
                AssertProblemResponses(document, "/api/v1/auth/token", "post", "429", "500");
                AssertResponseCodes(document, "/api/v1/events/stream", "get", "200", "400", "401", "429", "500", "503");
                AssertResponseCodes(document, "/api/v1/console/commands", "post", "200", "400", "401", "403", "500", "503");
                AssertResponseCodes(document, "/api/v1/players/online", "get", "200", "401", "403", "500", "503");
                AssertResponseCodes(document, "/api/v1/players/{entityId}/kick", "post", "200", "400", "401", "403", "409", "500", "503");
                AssertResponseCodes(document, "/api/v1/server-operations/restart", "post", "202", "400", "401", "403", "409", "500", "503");
                AssertResponseCodes(document, "/api/v1/server-operations/shutdown", "post", "202", "400", "401", "403", "409", "500", "503");
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
        public async Task Openapi_document_describes_success_response_schemas()
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

                AssertJsonSuccessSchema(
                    document,
                    "/health",
                    "get",
                    "HealthResponse");
                AssertJsonSuccessSchema(
                    document,
                    "/api/v1/health",
                    "get",
                    "HealthResponse");
                AssertJsonSuccessSchema(
                    document,
                    "/api/v1/players/online",
                    "get",
                    "OnlinePlayersResponse");
                AssertJsonSuccessSchema(
                    document,
                    "/api/v1/players/{entityId}/kick",
                    "post",
                    "KickPlayerResponse");
                AssertJsonSuccessSchema(
                    document,
                    "/api/v1/console/commands",
                    "post",
                    "ConsoleCommandResponse");
                var requestContent = Assert.IsType<JObject>(
                    document["paths"]?["/api/v1/console/commands"]?["post"]?
                        ["requestBody"]?["content"]);
                var requestMediaType = Assert.Single(requestContent.Properties());
                Assert.Equal("application/json", requestMediaType.Name);
                AssertSchemaProperties(
                    document,
                    requestMediaType.Value["schema"],
                    "command");
                AssertSchemaProperties(
                    document,
                    "HealthResponse",
                    "product",
                    "status",
                    "version");
                AssertSchemaProperties(document, "ConsoleCommandResponse", "command", "output");
                AssertSchemaProperties(document, "OnlinePlayersResponse", "players");
                AssertSchemaProperties(
                    document,
                    "KickPlayerResponse",
                    "completedAtUtc",
                    "operationId",
                    "requestedAtUtc",
                    "status",
                    "target");
                AssertSuccessResponsesDescribeContent(document);
                var backupDownloadContent = Assert.IsType<JObject>(
                    document["paths"]?["/api/v1/backups/{backupId}/download"]?["get"]?
                        ["responses"]?["200"]?["content"]);
                var backupDownloadMediaType = Assert.Single(backupDownloadContent.Properties());
                Assert.Equal("application/zip", backupDownloadMediaType.Name);
                Assert.Equal("string", (string?)backupDownloadMediaType.Value["schema"]?["type"]);
                Assert.Equal("binary", (string?)backupDownloadMediaType.Value["schema"]?["format"]);
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
        public async Task Basic_credentials_cannot_execute_protected_rest_operations()
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
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/console/commands");
                Assert.Null(gateway.Request);
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
                host.Start();
                var accessToken = await IssueAccessTokenAsync(client, url);
                firstRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                secondRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("kick player", gateway.Request?.Command);
            }
        }

        [Theory]
        [InlineData("{\"command\":", "invalid_request_body")]
        [InlineData("{\"command\":{}}", "invalid_request_body")]
        [InlineData("{\"command\":\"   \"}", "console_command_required")]
        public async Task Invalid_console_command_request_returns_stable_problem_details(
            string body,
            string expectedCode)
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
            using (var request = CreateJsonPostRequest(url + "api/v1/console/commands", body))
            {
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var problem = await AssertProblemDetailsAsync(
                    response,
                    expectedCode,
                    "/api/v1/console/commands");
                if (expectedCode == "invalid_request_body")
                    Assert.Equal("The JSON request body is invalid.", (string?)problem["detail"]);
                Assert.Null(gateway.Request);
            }
        }

        [Fact]
        public async Task Anonymous_invalid_console_command_request_requires_authentication_first()
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
            using (var request = CreateJsonPostRequest(
                url + "api/v1/console/commands",
                "{\"command\":{}"))
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
        public async Task Invalid_console_command_body_is_rejected_before_game_readiness()
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
            using (var request = CreateJsonPostRequest(
                url + "api/v1/console/commands",
                "{\"command\":{}}"))
            {
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "invalid_request_body",
                    "/api/v1/console/commands");
                Assert.Null(gateway.Request);
            }
        }

        [Fact]
        public async Task Unhandled_web_api_exception_reaches_owin_problem_details_boundary()
        {
            const string requestId = "unhandled-console-command";
            const string failureMessage = "unexpected gateway failure";
            var logs = new List<string>();
            var gateway = new TestConsoleCommandGateway(
                new InvalidOperationException(failureMessage));
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                consoleGateway: gateway);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider, log: logs.Add)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = CreateConsoleCommandRequest(url, "version"))
            {
                request.Headers.Add(RequestCorrelationMiddleware.HeaderName, requestId);
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = await AssertProblemDetailsAsync(
                    response,
                    "internal_server_error",
                    "/api/v1/console/commands");

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal(requestId, (string?)payload["traceId"]);
                Assert.DoesNotContain(failureMessage, payload.ToString());
                var log = Assert.Single(
                    logs,
                    entry => entry.Contains("Unhandled 7DPanel API exception."));
                Assert.Contains(requestId, log);
                Assert.Contains(failureMessage, log);
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new[] { "players" }, payload.Properties().Select(property => property.Name));
                Assert.Equal(0, ((JArray?)payload["players"])?.Count ?? 0);
                Assert.Equal(1, query.CallCount);
            }
        }

        [Fact]
        public async Task Owner_with_multiple_players_returns_camel_case_fields_and_sorted_results()
        {
            var aliceObservedAt = new DateTimeOffset(2026, 7, 21, 10, 29, 0, TimeSpan.Zero);
            var zedObservedAt = new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero);
            var query = new TestOnlinePlayerQuery(new OnlinePlayersSnapshot(
                new[]
                {
                    CreateOnlinePlayer(
                        42,
                        "Zed",
                        new PlayerPlatformIdentity("steam-2", "Steam"),
                        new PlayerPlatformIdentity("cross-2", "Epic"),
                        100,
                        20,
                        90,
                        zedObservedAt,
                        PlayerDeviceType.Xbox,
                        "198.51.100.7",
                        "V 3.0.1",
                        "18446744073709551615",
                        0,
                        true,
                        score: 827),
                    CreateOnlinePlayer(
                        7,
                        "Alice",
                        new PlayerPlatformIdentity("steam-1", "Steam"),
                        null,
                        40,
                        18,
                        95,
                        aliceObservedAt)
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());
                var players = (JArray)payload["players"]!;

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.True(players.All(item => ((JObject)item).Properties().Select(property => property.Name).OrderBy(name => name).SequenceEqual(new[]
                {
                    "bedroll",
                    "compatibilityVersion",
                    "crossplatformIdentity",
                    "currentLifeMinutes",
                    "deaths",
                    "deviceType",
                    "discordUserId",
                    "distanceWalkedMeters",
                    "entityId",
                    "expToNextLevel",
                    "gameStage",
                    "health",
                    "ip",
                    "isDead",
                    "lastLoginUtc",
                    "level",
                    "longestLifeMinutes",
                    "maxHealth",
                    "name",
                    "observedAtUtc",
                    "permissionLevel",
                    "ping",
                    "platformIdentity",
                    "playerKills",
                    "playGroup",
                    "position",
                    "score",
                    "skillPoints",
                    "totalItemsCrafted",
                    "totalTimePlayedMinutes",
                    "zombieKills"
                })), "unexpected player property names");
                Assert.Equal(new[] { "players" }, payload.Properties().Select(property => property.Name));
                Assert.Equal(2, players.Count);
                Assert.Equal(7, (int?)players[0]["entityId"]);
                Assert.Equal(42, (int?)players[1]["entityId"]);
                Assert.Equal("Alice", (string?)players[0]["name"]);
                Assert.Equal("steam-1", (string?)players[0]["platformIdentity"]?["combinedId"]);
                Assert.Equal("Steam", (string?)players[0]["platformIdentity"]?["platform"]);
                Assert.Equal("cross-2", (string?)players[1]["crossplatformIdentity"]?["combinedId"]);
                Assert.Equal("Epic", (string?)players[1]["crossplatformIdentity"]?["platform"]);
                Assert.Equal(JTokenType.Null, players[0]["crossplatformIdentity"]?.Type);
                Assert.Equal("xbox", (string?)players[1]["deviceType"]);
                Assert.Equal(JTokenType.Null, players[0]["ip"]?.Type);
                Assert.Equal("198.51.100.7", (string?)players[1]["ip"]);
                Assert.Equal(JTokenType.Null, players[0]["compatibilityVersion"]?.Type);
                Assert.Equal("V 3.0.1", (string?)players[1]["compatibilityVersion"]);
                Assert.Equal(JTokenType.Null, players[0]["discordUserId"]?.Type);
                Assert.Equal("18446744073709551615", (string?)players[1]["discordUserId"]);
                Assert.Equal(1000, (int?)players[0]["permissionLevel"]);
                Assert.Equal(0, (int?)players[1]["permissionLevel"]);
                Assert.Equal(new[] { "x", "y", "z" }, players[1]["position"]!
                    .Children<JProperty>().Select(property => property.Name).OrderBy(name => name));
                Assert.Equal(100.5f, (float?)players[1]["position"]?["x"]);
                Assert.Equal(51f, (float?)players[1]["position"]?["y"]);
                Assert.Equal(200.25f, (float?)players[1]["position"]?["z"]);
                Assert.True((bool?)players[1]["isDead"]);
                Assert.Equal(40, (int?)players[0]["ping"]);
                Assert.Equal(90, (int?)players[1]["health"]);
                Assert.Equal(100, (int?)players[1]["maxHealth"]);
                Assert.Equal(827, (int?)players[1]["score"]);
                Assert.Equal(317, (int?)players[1]["zombieKills"]);
                Assert.Equal(2, (int?)players[1]["playerKills"]);
                Assert.Equal(4, (int?)players[1]["deaths"]);
                Assert.Equal(4823.5f, (float?)players[1]["totalTimePlayedMinutes"]);
                Assert.Equal(127540.75f, (float?)players[1]["distanceWalkedMeters"]);
                Assert.Equal(2360u, (uint?)players[1]["totalItemsCrafted"]);
                Assert.Equal(920.25f, (float?)players[1]["longestLifeMinutes"]);
                Assert.Equal(134.5f, (float?)players[1]["currentLifeMinutes"]);
                Assert.Equal(aliceObservedAt, (DateTimeOffset?)players[0]["observedAtUtc"]);
                Assert.Equal(zedObservedAt, (DateTimeOffset?)players[1]["observedAtUtc"]);
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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

        [Fact]
        public async Task Owner_reads_historical_players_when_the_game_is_not_ready()
        {
            var store = new TestPlayerHistoryStore();
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(
                true,
                hub,
                gameReadiness: GameReadinessState.Loading,
                playerHistoryStore: store);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/players/history?pageSize=1"))
            {
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new[] { "nextCursor", "players" }, payload.Properties()
                    .Select(property => property.Name).OrderBy(name => name));
                Assert.Equal("EOS_0002d12af0fe4add9c7de0fbc238d431", (string?)payload["players"]?[0]?["crossplatformId"]);
                Assert.Equal(1, store.GetPlayersCallCount);
            }
        }

        [Fact]
        public async Task Owner_reads_historical_player_details_and_snapshots()
        {
            var store = new TestPlayerHistoryStore();
            const string crossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, playerHistoryStore: store);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();
                var accessToken = await IssueAccessTokenAsync(client, url);

                using (var detailsRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/history/" + Uri.EscapeDataString(crossplatformId)))
                {
                    detailsRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var detailsResponse = await client.SendAsync(
                        detailsRequest,
                        TestContext.Current.CancellationToken);
                    var details = JObject.Parse(await detailsResponse.Content.ReadAsStringAsync());

                    Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
                    Assert.Equal(crossplatformId, (string?)details["player"]?["crossplatformId"]);
                    Assert.Equal(1L, (long?)details["gapSummary"]?["gapCount"]);
                }

                using (var snapshotsRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/history/" + Uri.EscapeDataString(crossplatformId) + "/snapshots?pageSize=100"))
                {
                    snapshotsRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var snapshotsResponse = await client.SendAsync(
                        snapshotsRequest,
                        TestContext.Current.CancellationToken);
                    var snapshots = JObject.Parse(await snapshotsResponse.Content.ReadAsStringAsync());

                    Assert.Equal(HttpStatusCode.OK, snapshotsResponse.StatusCode);
                    Assert.Equal(41L, (long?)snapshots["snapshots"]?[0]?["snapshotId"]);
                    Assert.Equal("Alice", (string?)snapshots["snapshots"]?[0]?["name"]);
                    Assert.Equal("queue_full", (string?)snapshots["gaps"]?[0]?["reason"]);
                    Assert.Equal(1, store.GetSnapshotsCallCount);
                }
            }
        }

        [Fact]
        public async Task Historical_player_routes_require_an_owner_and_validate_request_values()
        {
            var store = new TestPlayerHistoryStore();
            const string crossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, playerHistoryStore: store);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            {
                host.Start();

                using (var anonymousResponse = await client.GetAsync(
                    url + "api/v1/players/history",
                    TestContext.Current.CancellationToken))
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
                }

                var adminToken = await IssueAccessTokenAsync(
                    client,
                    url,
                    "test-admin",
                    "test-admin-password");
                using (var adminRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/history"))
                {
                    adminRequest.Headers.Authorization = CreateBearerAuthorization(adminToken);
                    using var adminResponse = await client.SendAsync(
                        adminRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);
                }

                var viewerToken = await IssueAccessTokenAsync(
                    client,
                    url,
                    "test-viewer",
                    "test-viewer-password");
                using (var viewerRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/history"))
                {
                    viewerRequest.Headers.Authorization = CreateBearerAuthorization(viewerToken);
                    using var viewerResponse = await client.SendAsync(
                        viewerRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, viewerResponse.StatusCode);
                }

                using (var apiKeyRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/history"))
                {
                    apiKeyRequest.Headers.Authorization = CreateBearerAuthorization(TestApiKey);
                    using var apiKeyResponse = await client.SendAsync(
                        apiKeyRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, apiKeyResponse.StatusCode);
                }

                var ownerToken = await IssueAccessTokenAsync(client, url);
                foreach (var requestUri in new[]
                {
                    url + "api/v1/players/history?pageSize=101",
                    url + "api/v1/players/history?pageSize=not-a-number",
                    url + "api/v1/players/history?cursor=not-a-cursor",
                    url + "api/v1/players/history/unknown",
                    url + "api/v1/players/history/unknown/snapshots?beforeSnapshotId=0",
                    url + "api/v1/players/history/" + Uri.EscapeDataString(crossplatformId) + "/snapshots?beforeSnapshotId=0",
                    url + "api/v1/players/history/" + Uri.EscapeDataString(crossplatformId) + "/snapshots?beforeSnapshotId=not-a-number"
                })
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                    request.Headers.Authorization = CreateBearerAuthorization(ownerToken);
                    using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
                    var expected = requestUri.EndsWith("/history/unknown", StringComparison.Ordinal)
                        ? HttpStatusCode.NotFound
                        : HttpStatusCode.BadRequest;
                    Assert.Equal(expected, response.StatusCode);
                }
            }
        }

        [Fact]
        public async Task Historical_player_store_failure_returns_a_deidentified_problem()
        {
            var store = new TestPlayerHistoryStore(typeof(InvalidOperationException));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            var hub = new ServerEventHub(new ServerEventLiveWindow(4));
            using var provider = CreateWebServiceProvider(true, hub, playerHistoryStore: store);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(HttpMethod.Get, url + "api/v1/players/history"))
            {
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var problem = await AssertProblemDetailsAsync(
                    response,
                    "historical_player_query_failed",
                    "/api/v1/players/history");

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.DoesNotContain("InvalidOperationException", problem.ToString());
            }
        }

        [Fact]
        public async Task Old_online_player_observation_returns_its_original_timestamp()
        {
            var observedAtUtc = new DateTimeOffset(2026, 7, 21, 10, 30, 0, TimeSpan.Zero);
            var query = new TestOnlinePlayerQuery(new OnlinePlayersSnapshot(
                new[]
                {
                    CreateOnlinePlayer(
                        7,
                        "Alice",
                        new PlayerPlatformIdentity("steam-1", "Steam"),
                        null,
                        40,
                        18,
                        95,
                        observedAtUtc)
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal(new[] { "players" }, payload.Properties().Select(property => property.Name));
                Assert.Equal(7, (int?)payload["players"]?[0]?["entityId"]);
                Assert.Equal(observedAtUtc, (DateTimeOffset?)payload["players"]?[0]?["observedAtUtc"]);
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
        [InlineData(7, "{\"expectedPlatformIdentity\":\"steam-1\",\"reason\":\"rule violation\",\"confirmed\":true}", "invalid_request_body")]
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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

            [Fact]
            public async Task Game_not_ready_rejects_invalid_kick_body_before_binding_errors()
            {
                var actions = new TestPlayerActions();
                var audit = new TestPlayerActionAuditTrail();
                await AssertKickProblemAsync(
                "{\"expectedPlatformIdentity\":\"steam-1\"}",
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
                host.Start();
                var accessToken = await IssueAccessTokenAsync(client, url);
                firstRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                secondRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
            const string expectedCsp =
                "default-src 'self'; base-uri 'self'; object-src 'none'; " +
                "frame-ancestors 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'";
            var assetRoot = Path.Combine(Path.GetTempPath(), "7dpanel-admin-" + Guid.NewGuid().ToString("N"));
            var assetsDirectory = Path.Combine(assetRoot, "assets");
            var playerDirectory = Path.Combine(assetRoot, "player");
            var playerAssetsDirectory = Path.Combine(playerDirectory, "assets");
            var conflictingApiDirectory = Path.Combine(assetRoot, "api", "v1");
            Directory.CreateDirectory(assetsDirectory);
            Directory.CreateDirectory(playerAssetsDirectory);
            Directory.CreateDirectory(conflictingApiDirectory);
            File.WriteAllText(Path.Combine(assetRoot, "index.html"), "<html><body>7DPanel Admin</body></html>");
            File.WriteAllText(Path.Combine(assetsDirectory, "app.js"), "window.panelLoaded = true;");
            File.WriteAllText(Path.Combine(playerDirectory, "index.html"), "<html><body>7DPanel Player</body></html>");
            File.WriteAllText(Path.Combine(playerAssetsDirectory, "app.js"), "window.playerLoaded = true;");
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
                    Assert.Equal(
                        expectedCsp,
                        rootResponse.Headers.GetValues("Content-Security-Policy").Single());

                    var spaResponse = await client.GetAsync(url + "overview", TestContext.Current.CancellationToken);
                    var spaBody = await spaResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, spaResponse.StatusCode);
                    Assert.Contains("7DPanel Admin", spaBody);
                    Assert.Equal(
                        expectedCsp,
                        spaResponse.Headers.GetValues("Content-Security-Policy").Single());

                    var indexResponse = await client.GetAsync(url + "index.html", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
                    Assert.Equal(
                        expectedCsp,
                        indexResponse.Headers.GetValues("Content-Security-Policy").Single());
                    Assert.DoesNotContain("unsafe-eval", expectedCsp);
                    Assert.DoesNotContain("http:", expectedCsp);
                    Assert.DoesNotContain("https:", expectedCsp);

                    var playerSpaResponse = await client.GetAsync(url + "player/store", TestContext.Current.CancellationToken);
                    var playerSpaBody = await playerSpaResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, playerSpaResponse.StatusCode);
                    Assert.Contains("7DPanel Player", playerSpaBody);
                    Assert.Equal(
                        expectedCsp,
                        playerSpaResponse.Headers.GetValues("Content-Security-Policy").Single());

                    var playerIndexResponse = await client.GetAsync(url + "player/index.html", TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, playerIndexResponse.StatusCode);
                    Assert.Equal(
                        expectedCsp,
                        playerIndexResponse.Headers.GetValues("Content-Security-Policy").Single());

                    var playerAssetResponse = await client.GetAsync(url + "player/assets/app.js", TestContext.Current.CancellationToken);
                    var playerAssetBody = await playerAssetResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, playerAssetResponse.StatusCode);
                    Assert.Contains("playerLoaded", playerAssetBody);
                    Assert.False(playerAssetResponse.Headers.Contains("Content-Security-Policy"));

                    var assetResponse = await client.GetAsync(url + "assets/app.js", TestContext.Current.CancellationToken);
                    var assetBody = await assetResponse.Content.ReadAsStringAsync();
                    Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
                    Assert.Contains("panelLoaded", assetBody);
                    Assert.False(assetResponse.Headers.Contains("Content-Security-Policy"));

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
                    Assert.False(apiResponse.Headers.Contains("Content-Security-Policy"));

                    var openApiResponse = await client.GetAsync(
                        url + "swagger/v1/swagger.json",
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
                    Assert.False(openApiResponse.Headers.Contains("Content-Security-Policy"));

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
                Assert.Equal(new[] { "Bearer" }, challenges);
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
        public async Task Bearer_event_stream_replays_after_last_event_id_and_releases_subscription()
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
        public async Task Event_stream_write_failure_is_logged_with_request_id()
        {
            const string requestId = "event-stream-write-failure";
            const string failureMessage = "event replay failed";
            var logs = new List<string>();
            var serverEvents = new ThrowingReplayServerEventStream(
                new InvalidOperationException(failureMessage));
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, serverEvents);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider, log: logs.Add)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/events/stream"))
            {
                request.Headers.Add(RequestCorrelationMiddleware.HeaderName, requestId);
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                Assert.True(SpinWait.SpinUntil(
                    () => logs.Any(entry =>
                        entry.Contains(requestId) && entry.Contains(failureMessage)),
                    TimeSpan.FromSeconds(5)));
                Assert.Single(
                    logs,
                    entry => entry.Contains("Unhandled 7DPanel API exception."));
            }
        }

        [Fact]
        public async Task Bearer_event_stream_reports_replay_gap()
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
        public async Task Bearer_event_stream_rejects_invalid_last_event_id()
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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

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
                Assert.Equal("test-owner", (string?)tokenPayload["username"]);
                Assert.Equal("Owner", (string?)tokenPayload["role"]);
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
        public async Task Api_key_authorizes_event_stream_only_from_the_authorization_header()
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

                using (var authorizedRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/events/stream"))
                {
                    authorizedRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestApiKey);
                    using var authorizedResponse = await client.SendAsync(
                        authorizedRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
                    Assert.Equal(
                        "text/event-stream",
                        authorizedResponse.Content.Headers.ContentType?.MediaType);

                    using var stream = await authorizedResponse.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    Assert.Equal("event: welcome", await ReadLineWithTimeoutAsync(reader));
                }

                Assert.True(SpinWait.SpinUntil(
                    () => hub.SubscriberCount == 0,
                    TimeSpan.FromSeconds(5)));

                using var queryResponse = await client.GetAsync(
                    url + "api/v1/events/stream?access_token=" + Uri.EscapeDataString(TestApiKey),
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, queryResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    queryResponse,
                    "authentication_required",
                    "/api/v1/events/stream");

                using var cookieRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/events/stream");
                cookieRequest.Headers.Add("Cookie", "access_token=" + TestApiKey);
                using var cookieResponse = await client.SendAsync(
                    cookieRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, cookieResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    cookieResponse,
                    "authentication_required",
                    "/api/v1/events/stream");
                Assert.Equal(0, hub.SubscriberCount);
            }
        }

        [Fact]
        public async Task Api_key_authorizes_protected_rest_only_from_the_authorization_header()
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

                using (var authorizedRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/online"))
                {
                    authorizedRequest.Headers.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestApiKey);
                    using var authorizedResponse = await client.SendAsync(
                        authorizedRequest,
                        TestContext.Current.CancellationToken);

                    Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
                }

                using var queryResponse = await client.GetAsync(
                    url + "api/v1/players/online?access_token=" + Uri.EscapeDataString(TestApiKey),
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, queryResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    queryResponse,
                    "authentication_required",
                    "/api/v1/players/online");

                using var cookieRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/players/online");
                cookieRequest.Headers.Add("Cookie", "access_token=" + TestApiKey);
                using var cookieResponse = await client.SendAsync(
                    cookieRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.Unauthorized, cookieResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    cookieResponse,
                    "authentication_required",
                    "/api/v1/players/online");
            }
        }

        [Fact]
        public async Task Anonymous_request_to_api_keys_is_rejected()
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

                using var response = await client.GetAsync(
                    url + "api/v1/api-keys",
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "authentication_required",
                    "/api/v1/api-keys");
            }
        }

        [Fact]
        public async Task Access_token_creates_an_api_key_once_without_caching()
        {
            var port = GetAvailablePort();
            var url = "http://127.0.0.1:" + port + "/";
            using var provider = CreateWebServiceProvider(true, out _);

            using (var host = new OwinWebHost(
                url,
                app => OwinStartup.Configure(app, provider)))
            using (var handler = new HttpClientHandler { UseProxy = false })
            using (var client = new HttpClient(handler))
            using (var request = new HttpRequestMessage(
                HttpMethod.Post,
                url + "api/v1/api-keys"))
            {
                request.Content = new StringContent(
                    "{\"name\":\" deployment \"}",
                    Encoding.UTF8,
                    "application/json");
                host.Start();
                await AuthorizeWithPasswordGrantAsync(client, url, request);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);
                var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.True(response.Headers.CacheControl?.NoStore);
                Assert.Equal(
                    new[] { "apiKey", "createdAtUtc", "expiresAtUtc", "id", "name" },
                    payload.Properties()
                        .Select(property => property.Name)
                        .OrderBy(name => name)
                        .ToArray());
                Assert.Equal("deployment", (string?)payload["name"]);
                Assert.False(string.IsNullOrWhiteSpace((string?)payload["id"]));
                Assert.StartsWith("7dp_k_", Assert.IsType<string>((string?)payload["apiKey"]));
                Assert.False(string.IsNullOrWhiteSpace((string?)payload["createdAtUtc"]));
                Assert.Equal(JTokenType.Null, payload["expiresAtUtc"]?.Type);
            }
        }

        [Fact]
        public async Task Access_token_lists_metadata_and_revokes_its_api_key_idempotently()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                string keyId;
                string createdApiKey;
                using (var createRequest = CreateApiKeyRequest(url, "deployment"))
                {
                    createRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    keyId = Assert.IsType<string>((string?)created["id"]);
                    createdApiKey = Assert.IsType<string>((string?)created["apiKey"]);
                }

                using (var listRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys"))
                {
                    listRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var listResponse = await client.SendAsync(
                        listRequest,
                        TestContext.Current.CancellationToken);
                    var list = JArray.Parse(await listResponse.Content.ReadAsStringAsync());

                    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                    var item = Assert.IsType<JObject>(Assert.Single(list));
                    Assert.Equal(
                        new[]
                        {
                            "createdAtUtc",
                            "displayPrefix",
                            "expiresAtUtc",
                            "id",
                            "lastUsedAtUtc",
                            "name",
                            "status"
                        },
                        item.Properties()
                            .Select(property => property.Name)
                            .OrderBy(name => name)
                            .ToArray());
                    Assert.Equal(keyId, (string?)item["id"]);
                    Assert.Equal("7dp_k_" + keyId, (string?)item["displayPrefix"]);
                    Assert.Equal("deployment", (string?)item["name"]);
                    Assert.Equal("active", (string?)item["status"]);
                    Assert.DoesNotContain(createdApiKey, list.ToString());
                    Assert.DoesNotContain("secret", list.ToString(), StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("identity", list.ToString(), StringComparison.OrdinalIgnoreCase);
                }

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var revokeRequest = new HttpRequestMessage(
                        HttpMethod.Delete,
                        url + "api/v1/api-keys/" + Uri.EscapeDataString(keyId));
                    revokeRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var revokeResponse = await client.SendAsync(
                        revokeRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
                }
            }
        }

        [Fact]
        public async Task Api_key_can_list_but_cannot_mutate_and_requires_a_bearer_header()
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
                using (var listRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys"))
                {
                    listRequest.Headers.Authorization = CreateBearerAuthorization(TestApiKey);
                    using var listResponse = await client.SendAsync(
                        listRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                }

                using (var createRequest = CreateApiKeyRequest(url, "not-allowed"))
                {
                    createRequest.Headers.Authorization = CreateBearerAuthorization(TestApiKey);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        createResponse,
                        "access_token_required",
                        "/api/v1/api-keys");
                }

                using (var invalidCreateRequest = CreateJsonPostRequest(
                    url + "api/v1/api-keys",
                    "{\"name\":{}}"))
                {
                    invalidCreateRequest.Headers.Authorization =
                        CreateBearerAuthorization(TestApiKey);
                    using var invalidCreateResponse = await client.SendAsync(
                        invalidCreateRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, invalidCreateResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        invalidCreateResponse,
                        "access_token_required",
                        "/api/v1/api-keys");
                }

                using (var revokeRequest = new HttpRequestMessage(
                    HttpMethod.Delete,
                    url + "api/v1/api-keys/not-a-real-key"))
                {
                    revokeRequest.Headers.Authorization = CreateBearerAuthorization(TestApiKey);
                    using var revokeResponse = await client.SendAsync(
                        revokeRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.Forbidden, revokeResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        revokeResponse,
                        "access_token_required",
                        "/api/v1/api-keys");
                }

                foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Delete })
                {
                    var route = method == HttpMethod.Delete
                        ? "api/v1/api-keys/not-a-real-key"
                        : "api/v1/api-keys";
                    foreach (var useQueryString in new[] { true, false })
                    {
                        using var request = new HttpRequestMessage(
                            method,
                            url + route + (useQueryString
                                ? "?access_token=" + Uri.EscapeDataString(TestApiKey)
                                : string.Empty));
                        if (!useQueryString)
                            request.Headers.Add("Cookie", "access_token=" + TestApiKey);

                        using var response = await client.SendAsync(
                            request,
                            TestContext.Current.CancellationToken);
                        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                        await AssertProblemDetailsAsync(
                            response,
                            "authentication_required",
                            method == HttpMethod.Delete
                                ? "/api/v1/api-keys"
                                : "/" + route);
                    }
                }
            }
        }

        [Fact]
        public async Task Access_token_cannot_revoke_another_subjects_api_key()
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
                var ownerAccessToken = await IssueAccessTokenAsync(client, url);
                var adminAccessToken = await IssueAccessTokenAsync(
                    client,
                    url,
                    "test-admin",
                    "test-admin-password");
                string keyId;
                using (var createRequest = CreateApiKeyRequest(url, "owner-key"))
                {
                    createRequest.Headers.Authorization = CreateBearerAuthorization(ownerAccessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    keyId = Assert.IsType<string>((string?)created["id"]);
                }

                using (var revokeRequest = new HttpRequestMessage(
                    HttpMethod.Delete,
                    url + "api/v1/api-keys/" + Uri.EscapeDataString(keyId)))
                {
                    revokeRequest.Headers.Authorization = CreateBearerAuthorization(adminAccessToken);
                    using var revokeResponse = await client.SendAsync(
                        revokeRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.NotFound, revokeResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        revokeResponse,
                        "api_key_not_found",
                        "/api/v1/api-keys");
                }

                using var listRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys");
                listRequest.Headers.Authorization = CreateBearerAuthorization(ownerAccessToken);
                using var listResponse = await client.SendAsync(
                    listRequest,
                    TestContext.Current.CancellationToken);
                var ownerKeys = JArray.Parse(await listResponse.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                Assert.Equal("active", (string?)ownerKeys.Single()["status"]);
            }
        }

        [Fact]
        public async Task Access_token_receives_stable_problem_details_for_invalid_api_key_creation()
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
                var accessToken = await IssueAccessTokenAsync(client, url);

                using (var blankNameRequest = CreateApiKeyRequest(url, "  "))
                {
                    blankNameRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var blankNameResponse = await client.SendAsync(
                        blankNameRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.BadRequest, blankNameResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        blankNameResponse,
                        "invalid_api_key_name",
                        "/api/v1/api-keys");
                }

                using (var longNameRequest = CreateApiKeyRequest(url, new string('a', 81)))
                {
                    longNameRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var longNameResponse = await client.SendAsync(
                        longNameRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.BadRequest, longNameResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        longNameResponse,
                        "invalid_api_key_name",
                        "/api/v1/api-keys");
                }

                using (var malformedNameRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    url + "api/v1/api-keys")
                {
                    Content = new StringContent(
                        "{\"name\":{}}",
                        Encoding.UTF8,
                        "application/json")
                })
                {
                    malformedNameRequest.Headers.Authorization =
                        CreateBearerAuthorization(accessToken);
                    using var malformedNameResponse = await client.SendAsync(
                        malformedNameRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.BadRequest, malformedNameResponse.StatusCode);
                    await AssertProblemDetailsAsync(
                        malformedNameResponse,
                        "invalid_request_body",
                        "/api/v1/api-keys");
                }

                using var expiredRequest = CreateApiKeyRequest(
                    url,
                    "expired",
                    DateTimeOffset.UtcNow.AddMinutes(-1));
                expiredRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                using var expiredResponse = await client.SendAsync(
                    expiredRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.BadRequest, expiredResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    expiredResponse,
                    "invalid_api_key_expiration",
                    "/api/v1/api-keys");

                using var malformedExpirationRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    url + "api/v1/api-keys")
                {
                    Content = new StringContent(
                        "{\"name\":\"must-not-be-permanent\",\"expiresAtUtc\":\"not-a-date\"}",
                        Encoding.UTF8,
                        "application/json")
                };
                malformedExpirationRequest.Headers.Authorization =
                    CreateBearerAuthorization(accessToken);
                using var malformedExpirationResponse = await client.SendAsync(
                    malformedExpirationRequest,
                    TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.BadRequest, malformedExpirationResponse.StatusCode);
                await AssertProblemDetailsAsync(
                    malformedExpirationResponse,
                    "invalid_request_body",
                    "/api/v1/api-keys");
            }
        }

        [Fact]
        public async Task Access_token_receives_problem_details_for_unsupported_api_key_creation_content_type()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    url + "api/v1/api-keys")
                {
                    Content = new StringContent(
                        "{\"name\":\"unsupported-content-type\"}",
                        Encoding.UTF8,
                        "text/plain")
                };
                request.Headers.Authorization = CreateBearerAuthorization(accessToken);

                using var response = await client.SendAsync(
                    request,
                    TestContext.Current.CancellationToken);

                Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
                await AssertProblemDetailsAsync(
                    response,
                    "unsupported_media_type",
                    "/api/v1/api-keys");
            }
        }

        [Fact]
        public async Task Access_token_reaches_api_key_capacity_without_disclosing_existing_keys()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                var createdApiKeys = new List<string>();
                for (var index = 0; index < 32; index++)
                {
                    using var createRequest = CreateApiKeyRequest(url, "key-" + index.ToString());
                    createRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    createdApiKeys.Add(Assert.IsType<string>((string?)created["apiKey"]));
                }

                using var exhaustedRequest = CreateApiKeyRequest(url, "one-too-many");
                exhaustedRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                using var exhaustedResponse = await client.SendAsync(
                    exhaustedRequest,
                    TestContext.Current.CancellationToken);
                var problem = await AssertProblemDetailsAsync(
                    exhaustedResponse,
                    "api_key_capacity_reached",
                    "/api/v1/api-keys");

                Assert.Equal(HttpStatusCode.Conflict, exhaustedResponse.StatusCode);
                foreach (var apiKey in createdApiKeys)
                    Assert.DoesNotContain(apiKey, problem.ToString());
            }
        }

        [Fact]
        public async Task Access_token_lists_api_keys_by_creation_time_then_id_descending()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                var createdIds = new List<string>();
                foreach (var name in new[] { "first", "second", "third" })
                {
                    using var createRequest = CreateApiKeyRequest(url, name);
                    createRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    createdIds.Add(Assert.IsType<string>((string?)created["id"]));
                }

                using var listRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys");
                listRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                using var listResponse = await client.SendAsync(
                    listRequest,
                    TestContext.Current.CancellationToken);
                var listed = JArray.Parse(await listResponse.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
                Assert.Equal(
                    createdIds.OrderByDescending(id => id, StringComparer.Ordinal).ToArray(),
                    listed.Values<JObject>()
                        .Select(item =>
                        {
                            var metadata = Assert.IsType<JObject>(item);
                            return Assert.IsType<string>((string?)metadata["id"]);
                        })
                        .ToArray());
            }
        }

        [Fact]
        public async Task Concurrent_access_token_revocations_are_idempotent()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                string keyId;
                using (var createRequest = CreateApiKeyRequest(url, "concurrent"))
                {
                    createRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    keyId = Assert.IsType<string>((string?)created["id"]);
                }

                var requests = Enumerable.Range(0, 4)
                    .Select(_ => SendApiKeyRevokeAsync(client, url, accessToken, keyId))
                    .ToArray();
                var responses = await Task.WhenAll(requests);
                foreach (var response in responses)
                {
                    using (response)
                        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                }
            }
        }

        [Fact]
        public async Task Delete_does_not_echo_a_complete_api_key_mistakenly_sent_as_an_id()
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
                var accessToken = await IssueAccessTokenAsync(client, url);
                var completeApiKey = "7dp_k_test-key_" + new string('s', 43);
                using var revokeRequest = new HttpRequestMessage(
                    HttpMethod.Delete,
                    url + "api/v1/api-keys/" + Uri.EscapeDataString(completeApiKey));
                revokeRequest.Headers.Authorization = CreateBearerAuthorization(accessToken);
                using var revokeResponse = await client.SendAsync(
                    revokeRequest,
                    TestContext.Current.CancellationToken);
                var problem = await AssertProblemDetailsAsync(
                    revokeResponse,
                    "api_key_not_found",
                    "/api/v1/api-keys");

                Assert.Equal(HttpStatusCode.NotFound, revokeResponse.StatusCode);
                Assert.DoesNotContain(completeApiKey, problem.ToString());

                foreach (var authorization in new[]
                {
                    null,
                    CreateBearerAuthorization(TestApiKey)
                })
                {
                    using var protectedRequest = new HttpRequestMessage(
                        HttpMethod.Delete,
                        url + "api/v1/api-keys/" + Uri.EscapeDataString(completeApiKey));
                    protectedRequest.Headers.Authorization = authorization;
                    using var protectedResponse = await client.SendAsync(
                        protectedRequest,
                        TestContext.Current.CancellationToken);
                    var protectedProblem = JObject.Parse(
                        await protectedResponse.Content.ReadAsStringAsync());

                    Assert.Equal(
                        authorization == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                        protectedResponse.StatusCode);
                    Assert.DoesNotContain(completeApiKey, protectedProblem.ToString());
                    Assert.Equal("/api/v1/api-keys", (string?)protectedProblem["instance"]);
                }
            }
        }

        [Fact]
        public async Task Created_api_key_authenticates_its_owner_and_cannot_list_another_subjects_keys()
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
                var ownerAccessToken = await IssueAccessTokenAsync(client, url);
                var adminAccessToken = await IssueAccessTokenAsync(
                    client,
                    url,
                    "test-admin",
                    "test-admin-password");
                string createdApiKey;
                string keyId;
                using (var createRequest = CreateApiKeyRequest(url, "usable"))
                {
                    createRequest.Headers.Authorization = CreateBearerAuthorization(ownerAccessToken);
                    using var createResponse = await client.SendAsync(
                        createRequest,
                        TestContext.Current.CancellationToken);
                    var created = JObject.Parse(await createResponse.Content.ReadAsStringAsync());
                    Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
                    createdApiKey = Assert.IsType<string>((string?)created["apiKey"]);
                    keyId = Assert.IsType<string>((string?)created["id"]);
                }

                using (var ownerListRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys"))
                {
                    ownerListRequest.Headers.Authorization = CreateBearerAuthorization(createdApiKey);
                    using var ownerListResponse = await client.SendAsync(
                        ownerListRequest,
                        TestContext.Current.CancellationToken);
                    Assert.Equal(HttpStatusCode.OK, ownerListResponse.StatusCode);
                    var ownerKeys = JArray.Parse(await ownerListResponse.Content.ReadAsStringAsync());
                    Assert.Equal(keyId, (string?)Assert.Single(ownerKeys)["id"]);
                }

                using var adminListRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    url + "api/v1/api-keys");
                adminListRequest.Headers.Authorization = CreateBearerAuthorization(adminAccessToken);
                using var adminListResponse = await client.SendAsync(
                    adminListRequest,
                    TestContext.Current.CancellationToken);
                var adminKeys = JArray.Parse(await adminListResponse.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.OK, adminListResponse.StatusCode);
                Assert.Empty(adminKeys);
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
                Assert.Null(payload["username"]);
                Assert.Null(payload["role"]);
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

        private static PlayerSnapshot CreateOnlinePlayer(
            int entityId,
            string name,
            PlayerPlatformIdentity platformIdentity,
            PlayerPlatformIdentity? crossplatformIdentity,
            int ping,
            int level,
            int health,
            DateTimeOffset observedAtUtc,
            PlayerDeviceType deviceType = PlayerDeviceType.Windows,
            string? ip = null,
            string? compatibilityVersion = null,
            string? discordUserId = null,
            int permissionLevel = 1000,
            bool isDead = false,
            int score = 0)
        {
            return new PlayerSnapshot(
                entityId,
                name,
                platformIdentity,
                crossplatformIdentity,
                deviceType,
                ip,
                ping,
                compatibilityVersion,
                discordUserId,
                permissionLevel,
                new PlayerPosition(100.5f, 51f, 200.25f),
                isDead,
                health,
                100,
                level,
                score,
                317,
                2,
                4,
                4823.5f,
                127540.75f,
                2360u,
                920.25f,
                134.5f,
                observedAtUtc);
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

        private static void AssertBearerSecurity(
            JObject document,
            string path,
            string method)
        {
            var security = Assert.IsType<JArray>(document["paths"]?[path]?[method]?["security"]);
            var requirement = Assert.Single(security);
            Assert.IsType<JArray>(requirement?["Bearer"]);
            Assert.Single(((JObject)requirement!).Properties());
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

        private static void AssertJsonSuccessSchema(
            JObject document,
            string path,
            string method,
            string schemaName)
        {
            var content = Assert.IsType<JObject>(
                document["paths"]?[path]?[method]?["responses"]?["200"]?["content"]);
            var mediaType = Assert.Single(content.Properties());
            Assert.Equal("application/json", mediaType.Name);
            Assert.Equal(
                "#/components/schemas/" + schemaName,
                (string?)mediaType.Value["schema"]?["$ref"]);
        }

        private static void AssertSchemaProperties(
            JObject document,
            string schemaName,
            params string[] propertyNames)
        {
            AssertSchemaProperties(
                document,
                document["components"]?["schemas"]?[schemaName],
                propertyNames);
        }

        private static void AssertSchemaProperties(
            JObject document,
            JToken? schema,
            params string[] propertyNames)
        {
            if (schema?["oneOf"] is JArray alternatives)
                schema = Assert.Single(alternatives);
            var reference = (string?)schema?["$ref"];
            if (!string.IsNullOrEmpty(reference))
            {
                const string prefix = "#/components/schemas/";
                Assert.StartsWith(prefix, reference, StringComparison.Ordinal);
                schema = document["components"]?["schemas"]?[reference!.Substring(prefix.Length)];
            }
            var schemaObject = Assert.IsType<JObject>(schema);
            var properties = Assert.IsType<JObject>(schemaObject["properties"]);
            Assert.Equal(
                propertyNames.OrderBy(propertyName => propertyName).ToArray(),
                properties.Children<JProperty>()
                    .Select(property => property.Name)
                    .OrderBy(propertyName => propertyName)
                    .ToArray());
        }

        private static void AssertSuccessResponsesDescribeContent(JObject document)
        {
            var operations = document["paths"]!
                .Children<JProperty>()
                .SelectMany(path => path.Value.Children<JProperty>())
                .Where(operation => operation.Value["responses"] != null);

            foreach (var operation in operations)
            {
                var successResponses = operation.Value["responses"]!
                    .Children<JProperty>()
                    .Where(response =>
                        int.TryParse(response.Name, out var statusCode) &&
                        statusCode >= 200 &&
                        statusCode < 300)
                    .ToArray();
                Assert.NotEmpty(successResponses);
                foreach (var response in successResponses)
                {
                    if (response.Name == "204")
                    {
                        var noContent = response.Value["content"];
                        Assert.True(noContent == null || !noContent.HasValues,
                            "A 204 response must not describe a response body.");
                        continue;
                    }

                    if (response.Name == "202" && response.Value["content"] == null)
                        continue;

                    var content = Assert.IsType<JObject>(response.Value["content"]);
                    Assert.NotEmpty(content.Properties());
                    Assert.Null(content["application/octet-stream"]);
                    Assert.All(
                        content.Properties(),
                        mediaType => Assert.NotNull(mediaType.Value["schema"]));
                }
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

        private static System.Net.Http.Headers.AuthenticationHeaderValue CreateBearerAuthorization(
            string accessToken) =>
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        private static async Task AuthorizeWithPasswordGrantAsync(
            HttpClient client,
            string url,
            HttpRequestMessage request)
        {
            request.Headers.Authorization = CreateBearerAuthorization(
                await IssueAccessTokenAsync(client, url));
        }

        private static HttpRequestMessage CreateConsoleCommandRequest(
            string url,
            string command)
        {
            return CreateJsonPostRequest(
                url + "api/v1/console/commands",
                "{\"command\":" + Newtonsoft.Json.JsonConvert.SerializeObject(command) + "}");
        }

        private static HttpRequestMessage CreateJsonPostRequest(string url, string body) =>
            new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

        private static HttpRequestMessage CreatePlayersRequest(string url)
        {
            return new HttpRequestMessage(
                HttpMethod.Get,
                url + "api/v1/players/online");
        }

        private static HttpRequestMessage CreateApiKeyRequest(
            string url,
            string name,
            DateTimeOffset? expiresAtUtc = null)
        {
            return new HttpRequestMessage(HttpMethod.Post, url + "api/v1/api-keys")
            {
                Content = new StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(new { name, expiresAtUtc }),
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private static async Task<HttpResponseMessage> SendApiKeyRevokeAsync(
            HttpClient client,
            string url,
            string accessToken,
            string keyId)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                url + "api/v1/api-keys/" + Uri.EscapeDataString(keyId));
            request.Headers.Authorization = CreateBearerAuthorization(accessToken);
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        private const string TestApiKey =
            "7dp_k_0123456789012345678901_0123456789012345678901234567890123456789012";

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
            CreateTokenContent("test-owner", password);

        private static FormUrlEncodedContent CreateTokenContent(string username, string password) =>
            new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "password"),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password)
            });

        private static async Task<string> IssueAccessTokenAsync(
            HttpClient client,
            string url,
            string username = "test-owner",
            string password = "test-password")
        {
            using var tokenContent = CreateTokenContent(username, password);
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
            IServerEventStream hub,
            bool allowInsecureHttp = true,
            IConsoleCommandGateway? consoleGateway = null,
            GameReadinessState gameReadiness = GameReadinessState.Ready,
            IOnlinePlayerQuery? onlinePlayerQuery = null,
            IPlayerActions? playerActions = null,
            IPlayerActionAuditTrail? playerActionAuditTrail = null,
            IPlayerHistoryStore? playerHistoryStore = null,
            IRecentConsoleLogQuery? recentConsoleLogs = null,
            IConsoleCommandCatalogQuery? consoleCommandCatalog = null)
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
            services.AddSingleton(authentication);
            services.AddSingleton<IServerEventStream>(hub);
            services.AddSingleton<IRecentConsoleLogQuery>(
                recentConsoleLogs ?? new ServerEventLiveWindow(1));
            services.AddSingleton<IPanelRuntimeStatus>(
                new TestPanelRuntimeStatus(ModHostState.Running, gameReadiness));
            services.AddSingleton(
                consoleGateway ?? new TestConsoleCommandGateway());
            services.AddSingleton<ExecuteConsoleCommandUseCase>();
            services.AddSingleton<IConsoleCommandCatalogQuery>(
                consoleCommandCatalog ?? new TestConsoleCommandCatalogQuery());
            services.AddSingleton<IOnlinePlayerQuery>(onlinePlayerQuery ?? new TestOnlinePlayerQuery());
            services.AddSingleton<GetOnlinePlayersUseCase>();
            services.AddSingleton<IPlayerHistoryStore>(playerHistoryStore ?? new TestPlayerHistoryStore());
            services.AddSingleton<GetHistoricalPlayersUseCase>();
            services.AddSingleton<GetHistoricalPlayerUseCase>();
            services.AddSingleton<GetPlayerHistorySnapshotsUseCase>();
            services.AddSingleton<IPlayerActions>(playerActions ?? new TestPlayerActions());
            services.AddSingleton<IPlayerActionAuditTrail>(
                playerActionAuditTrail ?? new TestPlayerActionAuditTrail());
            services.AddSingleton<KickPlayerUseCase>();
            var authenticationStore = new TestPanelAuthenticationStore();
            services.AddSingleton<IPanelCredentialStore>(authenticationStore);
            services.AddSingleton<IPanelAccessTokenStore>(authenticationStore);
            services.AddSingleton<IPanelApiKeyStore>(authenticationStore);
            services.AddSingleton<IRecentActivityWriter>(new NullRecentActivityWriter());
            OwinStartup.RegisterAuthenticationServices(services, _ => { });
            services.AddScoped<ServerEventSseSession>();
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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
                    Array.Empty<PlayerSnapshot>()));
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

        private sealed class TestPlayerHistoryStore : IPlayerHistoryStore
        {
            private const string CrossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";
            private readonly HistoricalPlayerSummary summary = new HistoricalPlayerSummary(
                CrossplatformId,
                "Alice",
                new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero),
                3,
                2,
                1,
                true);
            private readonly HistoricalPlayerSnapshot snapshot;
            private readonly Exception? failure;
            private readonly PlayerHistoryGap gap = new PlayerHistoryGap(
                "gap-1",
                CrossplatformId,
                new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 24, 9, 5, 0, TimeSpan.Zero),
                2,
                PlayerHistoryGapReason.QueueFull,
                new DateTimeOffset(2026, 7, 24, 9, 6, 0, TimeSpan.Zero));

            public TestPlayerHistoryStore(Type? failureType = null)
            {
                failure = failureType == null ? null : (Exception)Activator.CreateInstance(failureType)!;
                snapshot = new HistoricalPlayerSnapshot(
                    41,
                    CreateOnlinePlayer(
                        7,
                        "Alice",
                        new PlayerPlatformIdentity("steam-1", "Steam"),
                        new PlayerPlatformIdentity(CrossplatformId, "EOS"),
                        40,
                        18,
                        95,
                        new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero)));
            }

            public int GetPlayersCallCount { get; private set; }

            public int GetSnapshotsCallCount { get; private set; }

            public void Append(PlayerSnapshot player)
            {
            }

            public void AppendGap(PlayerHistoryGap historyGap)
            {
            }

            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => 0;

            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query)
            {
                ThrowIfFaulted();
                GetPlayersCallCount++;
                return new HistoricalPlayersPage(new[] { summary }, null);
            }

            public HistoricalPlayerDetails? GetPlayer(string crossplatformId)
            {
                ThrowIfFaulted();
                return string.Equals(crossplatformId, CrossplatformId, StringComparison.Ordinal)
                    ? new HistoricalPlayerDetails(summary, new PlayerHistoryGapSummary(1, 2))
                    : null;
            }

            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query)
            {
                ThrowIfFaulted();
                GetSnapshotsCallCount++;
                if (!string.Equals(query.CrossplatformId, CrossplatformId, StringComparison.Ordinal))
                    return new PlayerHistorySnapshotsPage(
                        Array.Empty<HistoricalPlayerSnapshot>(),
                        null,
                        Array.Empty<PlayerHistoryGap>());

                return new PlayerHistorySnapshotsPage(new[] { snapshot }, null, new[] { gap });
            }

            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) =>
                throw new NotSupportedException();

            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) =>
                Array.Empty<HistoricalPlayerLastRetainedLocation>();

            private void ThrowIfFaulted()
            {
                if (failure != null) throw failure;
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

        private sealed class TestPanelAuthenticationStore :
            IPanelCredentialStore,
            IPanelAccessTokenStore,
            IPanelApiKeyStore
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, StoredAccessToken> tokens =
                new Dictionary<string, StoredAccessToken>(StringComparer.Ordinal);
            private readonly List<TestApiKeyRecord> apiKeys = new List<TestApiKeyRecord>();
            private readonly Dictionary<string, TestUser> users =
                new Dictionary<string, TestUser>(StringComparer.Ordinal)
                {
                    ["test-owner"] = new TestUser(
                        new PanelUserIdentity(
                            "test-owner-subject",
                            "test-owner",
                            PanelUserIdentity.OwnerRole),
                        "test-password"),
                    ["test-admin"] = new TestUser(
                        new PanelUserIdentity(
                            "test-admin-subject",
                            "test-admin",
                            PanelUserIdentity.AdminRole),
                        "test-admin-password"),
                    ["test-viewer"] = new TestUser(
                        new PanelUserIdentity(
                            "test-viewer-subject",
                            "test-viewer",
                            PanelUserIdentity.ViewerRole),
                        "test-viewer-password")
                };
            private int nextApiKeySequence;

            public bool TryVerify(
                string username,
                string password,
                out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                if (!users.TryGetValue(username, out var user) ||
                    !string.Equals(password, user.Password, StringComparison.Ordinal))
                {
                    return false;
                }

                panelIdentity = user.Identity;
                return true;
            }

            public bool TryGetActive(string subject, out PanelUserIdentity panelIdentity)
            {
                panelIdentity = null!;
                var user = users.Values.FirstOrDefault(candidate =>
                    string.Equals(candidate.Identity.Subject, subject, StringComparison.Ordinal));
                if (user == null) return false;

                panelIdentity = user.Identity;
                return true;
            }

            public string Issue(
                PanelUserIdentity panelIdentity,
                DateTimeOffset issuedUtc,
                DateTimeOffset expiresUtc)
            {
                var token = "7dp_t_test-token-" + Guid.NewGuid().ToString("N");
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

            public ApiKeyCreateResult Create(
                string subject,
                string name,
                DateTimeOffset createdUtc,
                DateTimeOffset? expiresUtc)
            {
                var normalizedName = (name ?? string.Empty).Trim();
                if (GetUnicodeScalarCount(normalizedName) is < 1 or > 80)
                    return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.InvalidName);
                if (expiresUtc.HasValue && expiresUtc.Value <= createdUtc)
                    return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.InvalidExpiration);

                lock (sync)
                {
                    var user = users.Values.FirstOrDefault(candidate =>
                        string.Equals(candidate.Identity.Subject, subject, StringComparison.Ordinal));
                    if (user == null)
                        return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.SubjectNotFound);
                    if (apiKeys.Count(key =>
                            string.Equals(
                                key.Identity.Subject,
                                subject,
                                StringComparison.Ordinal) &&
                            key.RevokedUtc == null) >= 32)
                        return ApiKeyCreateResult.Failed(ApiKeyCreateStatus.CapacityReached);

                    var keyId = "test-key-" + (++nextApiKeySequence).ToString();
                    var apiKey = "7dp_k_" + keyId + "_" + Guid.NewGuid().ToString("N");
                    var record = new TestApiKeyRecord(
                        keyId,
                        apiKey,
                        user.Identity,
                        normalizedName,
                        createdUtc,
                        expiresUtc);
                    apiKeys.Add(record);
                    return ApiKeyCreateResult.Created(new CreatedApiKey(
                        apiKey,
                        record.ToStoredApiKey(createdUtc)));
                }
            }

            public IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow)
            {
                lock (sync)
                {
                    return apiKeys
                        .Where(key => string.Equals(key.Identity.Subject, subject, StringComparison.Ordinal))
                        .OrderByDescending(key => key.CreatedUtc)
                        .ThenByDescending(key => key.KeyId, StringComparer.Ordinal)
                        .Select(key => key.ToStoredApiKey(utcNow))
                        .ToArray();
                }
            }

            public bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc)
            {
                lock (sync)
                {
                    var key = apiKeys.FirstOrDefault(candidate =>
                        string.Equals(candidate.Identity.Subject, subject, StringComparison.Ordinal) &&
                        string.Equals(candidate.KeyId, keyId, StringComparison.Ordinal));
                    if (key == null) return false;
                    key.RevokedUtc ??= revokedUtc;
                    return true;
                }
            }

            public bool TryValidate(
                string apiKey,
                DateTimeOffset utcNow,
                out StoredApiKey storedApiKey)
            {
                storedApiKey = null!;
                lock (sync)
                {
                    var key = apiKeys.FirstOrDefault(candidate =>
                        string.Equals(candidate.ApiKey, apiKey, StringComparison.Ordinal) &&
                        candidate.RevokedUtc == null &&
                        (!candidate.ExpiresUtc.HasValue || candidate.ExpiresUtc.Value > utcNow));
                    if (key != null)
                    {
                        storedApiKey = key.ToStoredApiKey(utcNow);
                        return true;
                    }
                }

                if (!string.Equals(apiKey, TestApiKey, StringComparison.Ordinal)) return false;

                storedApiKey = new StoredApiKey(
                    "0123456789012345678901",
                    users["test-owner"].Identity,
                    "integration",
                    new DateTimeOffset(2026, 7, 23, 0, 0, 0, TimeSpan.Zero),
                    null,
                    null,
                    null,
                    utcNow);
                return true;
            }

            private static int GetUnicodeScalarCount(string value)
            {
                var count = 0;
                for (var index = 0; index < value.Length; index++)
                {
                    if (char.IsHighSurrogate(value[index]) &&
                        index + 1 < value.Length &&
                        char.IsLowSurrogate(value[index + 1]))
                    {
                        index++;
                    }

                    count++;
                }

                return count;
            }

            [Trait("Capability", "Platform")]

            [Trait("Boundary", "Web")]

            private sealed class TestUser
            {
                public TestUser(PanelUserIdentity identity, string password)
                {
                    Identity = identity;
                    Password = password;
                }

                public PanelUserIdentity Identity { get; }
                public string Password { get; }
            }

            [Trait("Capability", "Platform")]

            [Trait("Boundary", "Web")]

            private sealed class TestApiKeyRecord
            {
                public TestApiKeyRecord(
                    string keyId,
                    string apiKey,
                    PanelUserIdentity identity,
                    string name,
                    DateTimeOffset createdUtc,
                    DateTimeOffset? expiresUtc)
                {
                    KeyId = keyId;
                    ApiKey = apiKey;
                    Identity = identity;
                    Name = name;
                    CreatedUtc = createdUtc;
                    ExpiresUtc = expiresUtc;
                }

                public string KeyId { get; }
                public string ApiKey { get; }
                public PanelUserIdentity Identity { get; }
                public string Name { get; }
                public DateTimeOffset CreatedUtc { get; }
                public DateTimeOffset? ExpiresUtc { get; }
                public DateTimeOffset? RevokedUtc { get; set; }

                public StoredApiKey ToStoredApiKey(DateTimeOffset utcNow) =>
                    new StoredApiKey(
                        KeyId,
                        Identity,
                        Name,
                        CreatedUtc,
                        null,
                        ExpiresUtc,
                        RevokedUtc,
                        utcNow);
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

        private sealed class ThrowingDataProtectionProvider : IDataProtectionProvider
        {
            public IDataProtector Create(params string[] purposes) =>
                throw new PlatformNotSupportedException(
                    "The test host does not provide a default data protector.");
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

        private sealed class ThrowingReplayServerEventStream : IServerEventStream
        {
            private readonly Exception failure;

            public ThrowingReplayServerEventStream(Exception failure)
            {
                this.failure = failure;
            }

            public IReadOnlyList<ServerEvent> ReadAfter(
                long? afterSequence,
                int limit,
                out bool hasGap)
            {
                hasGap = false;
                throw failure;
            }

            public bool TrySubscribe(
                int capacity,
                out IServerEventSubscription? subscription)
            {
                subscription = new PendingServerEventSubscription();
                return true;
            }
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Web")]

        private sealed class PendingServerEventSubscription : IServerEventSubscription
        {
            public bool IsOverflowed => false;

            public Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken) =>
                Task.Delay(Timeout.Infinite, cancellationToken)
                    .ContinueWith(
                        _ => (ServerEvent?)null,
                        cancellationToken,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);

            public void Dispose()
            {
            }
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
