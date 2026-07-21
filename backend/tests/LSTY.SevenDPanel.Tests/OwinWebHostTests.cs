using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin.Security.DataProtection;
using Newtonsoft.Json.Linq;
using Owin;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Category", "Integration")]
    [Trait("Host", "InProcessKatana")]
    public sealed class OwinWebHostTests
    {
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
            bool allowInsecureHttp = true)
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
                new TestPanelRuntimeStatus(ModHostState.Running, GameReadinessState.Ready));
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
