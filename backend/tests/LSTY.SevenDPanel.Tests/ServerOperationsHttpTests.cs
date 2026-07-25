using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ServerOperationsHttpTests
    {
        [Theory]
        [InlineData("api/v1/server-operations/restart")]
        [InlineData("api/v1/server-operations/shutdown")]
        public async Task Server_operations_require_authentication(string path)
        {
            using var host = CreateHost(null);

            using var response = await PostAsync(host.Client, path, "{\"confirmed\":true}");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Admin", "api/v1/server-operations/restart")]
        [InlineData("Viewer", "api/v1/server-operations/restart")]
        [InlineData("Admin", "api/v1/server-operations/shutdown")]
        [InlineData("Viewer", "api/v1/server-operations/shutdown")]
        public async Task Server_operations_forbid_non_owners(string role, string path)
        {
            using var host = CreateHost(role);

            using var response = await PostAsync(host.Client, path, "{\"confirmed\":true}");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("api/v1/server-operations/restart")]
        [InlineData("api/v1/server-operations/shutdown")]
        public async Task Each_operation_requires_explicit_confirmation(string path)
        {
            using var host = CreateHost("Owner");

            using var response = await PostAsync(host.Client, path, "{\"confirmed\":false}");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("confirmation_required", (string?)problem["code"]);
        }

        [Theory]
        [InlineData("api/v1/server-operations/restart")]
        [InlineData("api/v1/server-operations/shutdown")]
        public async Task Server_operations_reject_unknown_request_members(string path)
        {
            using var host = CreateHost("Owner");

            using var response = await PostAsync(
                host.Client,
                path,
                "{\"confirmed\":true,\"path\":\"C:\\\\private\\\\restart.cmd\"}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_request_body", (string?)problem["code"]);
            Assert.DoesNotContain("path", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Restart_returns_script_started_without_claiming_server_restart_success()
        {
            var audit = new RecordingAuditTrail();
            var launcher = new RecordingLauncher();
            using var host = CreateHost("Owner", launcher: launcher, audit: audit);

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/restart",
                "{\"confirmed\":true}");
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("restart_script_started", (string?)payload["code"]);
            Assert.DoesNotContain("restarted", payload.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, launcher.Calls);
            Assert.Equal("subject-1", audit.LastIntent?.ActorSubject);
        }

        [Fact]
        public async Task Shutdown_returns_its_independent_success_code()
        {
            var audit = new RecordingAuditTrail();
            var gateway = new RecordingShutdownGateway();
            using var host = CreateHost("Owner", gateway: gateway, audit: audit);

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/shutdown",
                "{\"confirmed\":true}");
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("shutdown_requested", (string?)payload["code"]);
            Assert.NotEqual("restart_script_started", (string?)payload["code"]);
            Assert.Equal(1, gateway.Calls);
            Assert.Equal("subject-1", audit.LastIntent?.ActorSubject);
        }

        [Fact]
        public async Task Restart_failure_uses_stable_problem_details_without_exception_leakage()
        {
            using var host = CreateHost(
                "Owner",
                launcher: new ThrowingLauncher(
                    new InvalidOperationException("C:\\secret\\restart.cmd user=Administrator")));

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/restart",
                "{\"confirmed\":true}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("restart_script_start_failed", (string?)problem["code"]);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Administrator", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Shutdown_failure_never_uses_restart_codes_or_exception_details()
        {
            using var host = CreateHost(
                "Owner",
                gateway: new RecordingShutdownGateway(
                    new InvalidOperationException("shutdown command /private/path user=root")));

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/shutdown",
                "{\"confirmed\":true}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("shutdown_failed", (string?)problem["code"]);
            Assert.DoesNotContain("restart_script_started", body, StringComparison.Ordinal);
            Assert.DoesNotContain("private/path", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("root", (string?)problem["detail"] ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Shutdown_busy_uses_stable_conflict_problem()
        {
            var gateway = new BlockingShutdownGateway();
            using var host = CreateHost("Owner", gateway: gateway);
            var first = PostAsync(
                host.Client,
                "api/v1/server-operations/shutdown",
                "{\"confirmed\":true}");
            await gateway.Entered.Task;

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/shutdown",
                "{\"confirmed\":true}");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());
            gateway.Release.TrySetResult(true);
            using var firstResponse = await first;

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("operation_in_progress", (string?)problem["code"]);
            Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        }

        [Fact]
        public async Task Audit_failure_is_stable_and_does_not_leak_inner_exception()
        {
            using var host = CreateHost(
                "Owner",
                audit: new ThrowingAuditTrail(
                    new InvalidOperationException("database=/private/audit.db user=operator")));

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/restart",
                "{\"confirmed\":true}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("audit_unavailable", (string?)problem["code"]);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("operator", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Shutdown_cancellation_uses_shutdown_code_without_exception_leakage()
        {
            using var host = CreateHost(
                "Owner",
                gateway: new RecordingShutdownGateway(
                    new OperationCanceledException("cancelled by /secret/user")));

            using var response = await PostAsync(
                host.Client,
                "api/v1/server-operations/shutdown",
                "{\"confirmed\":true}");
            var body = await response.Content.ReadAsStringAsync();
            var problem = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("shutdown_cancelled", (string?)problem["code"]);
            Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
        }

        private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json)
        {
            return client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        }

        private static HttpTestHost CreateHost(
            string? role,
            IRestartScriptLauncher? launcher = null,
            IShutdownServerGateway? gateway = null,
            IServerOperationAuditTrail? audit = null)
        {
            launcher ??= new RecordingLauncher();
            gateway ??= new RecordingShutdownGateway();
            audit ??= new RecordingAuditTrail();
            var activity = new NullRecentActivityWriter();
            var services = new ServiceCollection();
            services.AddSingleton(new RestartServerUseCase(launcher, audit, activity));
            services.AddSingleton(new ShutdownServerUseCase(gateway, audit, activity));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.MessageHandlers.Add(new ApiProblemDetailsHandler());
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
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

        private sealed class RecordingLauncher : IRestartScriptLauncher
        {
            public int Calls { get; private set; }
            public DateTimeOffset StartConfiguredScript()
            {
                Calls++;
                return new DateTimeOffset(2026, 7, 25, 2, 3, 4, TimeSpan.Zero);
            }
        }

        private sealed class ThrowingLauncher : IRestartScriptLauncher
        {
            private readonly Exception exception;
            public ThrowingLauncher(Exception exception) { this.exception = exception; }
            public DateTimeOffset StartConfiguredScript() => throw exception;
        }

        private sealed class RecordingShutdownGateway : IShutdownServerGateway
        {
            private readonly Exception? exception;
            public RecordingShutdownGateway(Exception? exception = null) { this.exception = exception; }
            public int Calls { get; private set; }
            public Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                Calls++;
                return exception == null ? Task.CompletedTask : Task.FromException(exception);
            }
        }

        private sealed class BlockingShutdownGateway : IShutdownServerGateway
        {
            public TaskCompletionSource<bool> Entered { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Release { get; } =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task RequestShutdownAsync(CancellationToken cancellationToken)
            {
                Entered.TrySetResult(true);
                await Release.Task;
            }
        }

        private sealed class RecordingAuditTrail : IServerOperationAuditTrail
        {
            public ServerOperationAuditIntent? LastIntent { get; private set; }
            public void CreatePending(ServerOperationAuditIntent intent) { LastIntent = intent; }
            public bool TryMarkStarted(string operationId, DateTimeOffset startedAtUtc) => true;
            public bool TryMarkFailed(ServerOperationAuditFailure failure) => true;
        }

        private sealed class ThrowingAuditTrail : IServerOperationAuditTrail
        {
            private readonly Exception exception;

            public ThrowingAuditTrail(Exception exception)
            {
                this.exception = exception;
            }

            public void CreatePending(ServerOperationAuditIntent intent) => throw exception;
            public bool TryMarkStarted(string operationId, DateTimeOffset startedAtUtc) => false;
            public bool TryMarkFailed(ServerOperationAuditFailure failure) => false;
        }

        private sealed class NullRecentActivityWriter : IRecentActivityWriter
        {
            public Task RecordPanelLoginSucceededAsync(string subject, string username, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string displayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string displayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
