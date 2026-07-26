using System;
using System.Collections.Generic;
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
    public sealed class GamePermissionHttpTests
    {
        [Theory]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData("Owner", HttpStatusCode.OK)]
        public async Task Game_permissions_are_owner_only(string? role, HttpStatusCode expected)
        {
            using var host = CreateHost(role, GamePermissionMutationResult.Succeeded());

            using var response = await host.Client.GetAsync("api/v1/game-permissions/admins");

            Assert.Equal(expected, response.StatusCode);
        }

        [Theory]
        [InlineData(GamePermissionMutationStatus.Invalid, HttpStatusCode.BadRequest, "invalid_game_permission_request")]
        [InlineData(GamePermissionMutationStatus.NotFound, HttpStatusCode.NotFound, "game_permission_not_found")]
        [InlineData(GamePermissionMutationStatus.Conflict, HttpStatusCode.Conflict, "game_permission_conflict")]
        [InlineData(GamePermissionMutationStatus.GameNotReady, HttpStatusCode.ServiceUnavailable, "game_not_ready")]
        [InlineData(GamePermissionMutationStatus.NativeRejected, HttpStatusCode.BadGateway, "native_game_permission_rejected")]
        [InlineData(GamePermissionMutationStatus.Unknown, HttpStatusCode.InternalServerError, "game_permission_update_failed")]
        public async Task Mutation_statuses_map_to_stable_problems(
            GamePermissionMutationStatus status,
            HttpStatusCode expected,
            string code)
        {
            using var host = CreateHost("Owner", Result(status));

            using var response = await host.Client.DeleteAsync("api/v1/game-permissions/commands/tele");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(expected, response.StatusCode);
            Assert.Equal(code, (string?)problem["code"]);
        }

        [Fact]
        public async Task Route_identity_is_authoritative_and_unknown_body_members_are_rejected()
        {
            var port = new RecordingControl(GamePermissionMutationResult.Succeeded());
            using var host = CreateHost("Owner", port);

            using var response = await host.Client.PutAsync(
                "api/v1/game-permissions/admins/EOS_route",
                new StringContent("{\"playerId\":\"EOS_body\",\"displayName\":\"P\",\"permissionLevel\":0}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(port.AdminEntries);
        }

        private static GamePermissionMutationResult Result(GamePermissionMutationStatus status) => status switch
        {
            GamePermissionMutationStatus.Invalid => GamePermissionMutationResult.Invalid(),
            GamePermissionMutationStatus.NotFound => GamePermissionMutationResult.NotFound(),
            GamePermissionMutationStatus.Conflict => GamePermissionMutationResult.Conflict(),
            GamePermissionMutationStatus.GameNotReady => GamePermissionMutationResult.GameNotReady(),
            GamePermissionMutationStatus.NativeRejected => GamePermissionMutationResult.NativeRejected(),
            _ => GamePermissionMutationResult.Unknown()
        };

        private static Host CreateHost(string? role, GamePermissionMutationResult result) =>
            CreateHost(role, new RecordingControl(result));

        private static Host CreateHost(string? role, RecordingControl port)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IGamePermissionControl>(port);
            services.AddSingleton<IRecentActivityWriter, NoOpActivityWriter>();
            services.AddSingleton<GamePermissionUseCases>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration { DependencyResolver = new MicrosoftDependencyResolver(provider) };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            configuration.Formatters.JsonFormatter.SerializerSettings.MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error;
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.MessageHandlers.Add(new ApiProblemDetailsHandler());
            configuration.EnsureInitialized();
            return new Host(provider, configuration);
        }

        private sealed class Host : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;
            public Host(ServiceProvider provider, HttpConfiguration configuration)
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
                    new Claim(ClaimTypes.NameIdentifier, "subject-1"), new Claim(ClaimTypes.Role, role)
                }, "Test");
                var principal = new ClaimsPrincipal(identity);
                var owin = new OwinContext();
                owin.Authentication.User = principal;
                request.SetOwinContext(owin);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }

        private sealed class RecordingControl : IGamePermissionControl
        {
            private readonly GamePermissionMutationResult result;
            public RecordingControl(GamePermissionMutationResult result) { this.result = result; }
            public List<GameAdminEntry> AdminEntries { get; } = new List<GameAdminEntry>();
            public Task<IReadOnlyList<GameAdminEntry>> GetAdminsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GameAdminEntry>>(Array.Empty<GameAdminEntry>());
            public Task<IReadOnlyList<CommandPermissionEntry>> GetCommandsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CommandPermissionEntry>>(Array.Empty<CommandPermissionEntry>());
            public Task<GamePermissionMutationResult> UpsertAdminAsync(GameAdminEntry entry, CancellationToken cancellationToken) { AdminEntries.Add(entry); return Task.FromResult(result); }
            public Task<GamePermissionMutationResult> RemoveAdminAsync(string playerId, CancellationToken cancellationToken) => Task.FromResult(result);
            public Task<GamePermissionMutationResult> UpsertCommandAsync(string command, int level, CancellationToken cancellationToken) => Task.FromResult(result);
            public Task<GamePermissionMutationResult> RemoveCommandAsync(string command, CancellationToken cancellationToken) => Task.FromResult(result);
        }

        private sealed class NoOpActivityWriter : IRecentActivityWriter
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
