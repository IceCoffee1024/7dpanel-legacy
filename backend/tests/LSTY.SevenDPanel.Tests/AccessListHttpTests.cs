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
using LSTY.SevenDPanel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class AccessListHttpTests
    {
        [Theory]
        [InlineData("Owner", HttpStatusCode.NoContent)]
        [InlineData("Admin", HttpStatusCode.NoContent)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        public async Task All_roles_read_but_only_owner_and_admin_write(
            string role,
            HttpStatusCode expectedWriteStatus)
        {
            using var host = CreateHost(role, AccessListMutationResult.Succeeded());

            using var read = await host.Client.GetAsync("api/v1/access-lists/bans");
            using var write = await PutJsonAsync(
                host.Client,
                "api/v1/access-lists/bans/EOS_1",
                "{\"displayName\":\"Player\",\"bannedUntilUtc\":\"2027-07-26T08:00:00Z\",\"reason\":\"reason\"}");

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            Assert.Equal(expectedWriteStatus, write.StatusCode);
        }

        [Theory]
        [InlineData(AccessListMutationStatus.NotFound, HttpStatusCode.NotFound, "access_list_entry_not_found")]
        [InlineData(AccessListMutationStatus.Conflict, HttpStatusCode.Conflict, "access_list_conflict")]
        [InlineData(AccessListMutationStatus.GameNotReady, HttpStatusCode.ServiceUnavailable, "game_not_ready")]
        [InlineData(AccessListMutationStatus.NativeRejected, HttpStatusCode.BadGateway, "native_access_list_rejected")]
        [InlineData(AccessListMutationStatus.Unknown, HttpStatusCode.InternalServerError, "access_list_update_failed")]
        public async Task Mutation_results_map_to_stable_problem_details(
            AccessListMutationStatus status,
            HttpStatusCode expectedStatus,
            string expectedCode)
        {
            using var host = CreateHost("Owner", Result(status));

            using var response = await host.Client.DeleteAsync("api/v1/access-lists/whitelist/EOS_1");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal(expectedCode, (string?)problem["code"]);
        }

        [Fact]
        public async Task Route_player_id_is_authoritative()
        {
            var port = new RecordingAccessControl(AccessListMutationResult.Succeeded());
            using var host = CreateHost("Owner", port);

            using var response = await PutJsonAsync(
                host.Client,
                "api/v1/access-lists/whitelist/EOS_route",
                "{\"displayName\":\"Player\",\"playerId\":\"EOS_body\"}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(port.WhitelistRequests);
        }

        private static AccessListMutationResult Result(AccessListMutationStatus status)
        {
            return status switch
            {
                AccessListMutationStatus.NotFound => AccessListMutationResult.NotFound(),
                AccessListMutationStatus.Conflict => AccessListMutationResult.Conflict(),
                AccessListMutationStatus.GameNotReady => AccessListMutationResult.GameNotReady(),
                AccessListMutationStatus.NativeRejected => AccessListMutationResult.NativeRejected(),
                _ => AccessListMutationResult.Unknown()
            };
        }

        private static HttpTestHost CreateHost(string? role, AccessListMutationResult result) =>
            CreateHost(role, new RecordingAccessControl(result));

        private static HttpTestHost CreateHost(string? role, RecordingAccessControl port)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IPlayerAccessControl>(port);
            services.AddSingleton<IRecentActivityWriter, NoOpActivityWriter>();
            services.AddSingleton<AccessListUseCases>();
            var provider = services.BuildServiceProvider();
            var configuration = new HttpConfiguration
            {
                DependencyResolver = new MicrosoftDependencyResolver(provider)
            };
            configuration.MapHttpAttributeRoutes();
            configuration.Formatters.Remove(configuration.Formatters.XmlFormatter);
            configuration.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            configuration.Formatters.JsonFormatter.SerializerSettings.MissingMemberHandling =
                Newtonsoft.Json.MissingMemberHandling.Error;
            configuration.MessageHandlers.Add(new PrincipalHandler(role));
            configuration.EnsureInitialized();
            return new HttpTestHost(provider, configuration);
        }

        private static Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, string json) =>
            client.PutAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

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
                var identity = role == null
                    ? new ClaimsIdentity()
                    : new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, "subject-1"),
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

        private sealed class RecordingAccessControl : IPlayerAccessControl
        {
            private readonly AccessListMutationResult result;
            public RecordingAccessControl(AccessListMutationResult result) { this.result = result; }
            public List<WhitelistRequest> WhitelistRequests { get; } = new List<WhitelistRequest>();
            public Task<IReadOnlyList<BanEntry>> GetBansAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BanEntry>>(Array.Empty<BanEntry>());
            public Task<IReadOnlyList<WhitelistEntry>> GetWhitelistAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WhitelistEntry>>(Array.Empty<WhitelistEntry>());
            public Task<AccessListMutationResult> UpsertBanAsync(BanRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
            public Task<AccessListMutationResult> RemoveBanAsync(string playerId, CancellationToken cancellationToken) => Task.FromResult(result);
            public Task<AccessListMutationResult> UpsertWhitelistAsync(WhitelistRequest request, CancellationToken cancellationToken) { WhitelistRequests.Add(request); return Task.FromResult(result); }
            public Task<AccessListMutationResult> RemoveWhitelistAsync(string playerId, CancellationToken cancellationToken) => Task.FromResult(result);
        }

        private sealed class NoOpActivityWriter : IRecentActivityWriter, IServerGovernanceActivityWriter
        {
            public Task RecordAccessListChangedAsync(string actorSubject, string list, string action, string playerId, string outcome, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPanelLoginSucceededAsync(string actorSubject, string actorDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerJoinedAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordPlayerLeftAsync(string playerDisplayName, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordRestartScriptStartedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordShutdownRequestedAsync(string actorSubject, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task RecordServerOperationFailedAsync(string actorSubject, string operationCode, string failureCode, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
