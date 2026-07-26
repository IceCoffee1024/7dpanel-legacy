using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PanelUserAdministrationHttpTests
    {
        [Fact]
        public void Controller_is_restricted_to_owner_role()
        {
            var authorize = Assert.Single(typeof(PanelUsersController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

            Assert.Equal("Owner", authorize.Roles);
        }

        [Fact]
        public async Task List_response_contains_identity_metadata_but_no_password_material()
        {
            using var host = CreateHost("Owner", new StubStore());

            using var response = await host.Client.GetAsync("api/v1/panel-users");
            var body = await response.Content.ReadAsStringAsync();
            var users = JArray.Parse(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("owner-subject", (string?)users[0]?["subject"]);
            Assert.Equal("Owner", (string?)users[0]?["role"]);
            Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Last_enabled_owner_maps_to_stable_conflict_problem()
        {
            var store = new StubStore
            {
                UpdateResult = PanelUserMutationResult.With(PanelUserMutationStatus.LastOwner)
            };
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.PutAsync(
                "api/v1/panel-users/owner-subject",
                new StringContent(
                    "{\"username\":\"owner\",\"role\":\"Admin\",\"enabled\":true}",
                    Encoding.UTF8,
                    "application/json"));
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("last_owner_required", (string?)problem["code"]);
        }

        private static Host CreateHost(string? role, IPanelUserAdministrationStore store)
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
            return new Host(provider, configuration);
        }

        private sealed class StubStore : IPanelUserAdministrationStore
        {
            public PanelUserMutationResult UpdateResult { get; set; } =
                PanelUserMutationResult.With(PanelUserMutationStatus.Updated);

            public IReadOnlyList<PanelUserRecord> ListUsers() => new[]
            {
                new PanelUserRecord(
                    "owner-subject",
                    "owner",
                    "Owner",
                    true,
                    new DateTimeOffset(2026, 7, 26, 1, 2, 3, TimeSpan.Zero))
            };

            public PanelUserMutationResult CreateUser(string username, string password, string role, bool enabled) =>
                PanelUserMutationResult.With(PanelUserMutationStatus.Created);

            public PanelUserMutationResult UpdateUser(string subject, string username, string role, bool enabled) =>
                UpdateResult;

            public PanelUserMutationResult ResetPassword(string subject, string password) =>
                PanelUserMutationResult.With(PanelUserMutationStatus.Updated);

            public PanelUserMutationResult DeleteUser(string subject) =>
                PanelUserMutationResult.With(PanelUserMutationStatus.Deleted);
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

        private sealed class Host : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public Host(ServiceProvider provider, HttpConfiguration configuration)
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
    }
}
