using System;
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
using LSTY.SevenDPanel.Application.Mods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Web")]
    public sealed class ModManagementHttpTests
    {
        [Theory]
        [InlineData("Owner")]
        [InlineData("Admin")]
        [InlineData("Viewer")]
        public async Task Each_panel_role_can_list_mods(string role)
        {
            using var host = CreateHost(role);

            using var response = await host.Client.GetAsync("api/v1/mods");
            var json = JArray.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Example", (string?)json[0]?["directoryId"]);
            Assert.True((bool?)json[0]?["isLoadedNow"]);
            Assert.False((bool?)json[0]?["isEnabledNextStart"]);
        }

        [Fact]
        public void Only_owner_can_change_next_start_state()
        {
            var method = typeof(ModsController).GetMethod("Put");
            Assert.NotNull(method);
            var authorize = Assert.Single(method!
                .GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>());

            Assert.Equal("Owner", authorize.Roles);
        }

        [Fact]
        public async Task Owner_change_maps_domain_conflict_without_disclosing_paths()
        {
            using var host = CreateHost("Owner", ModStateChangeResult.Conflict());

            using var response = await host.Client.PutAsync(
                "api/v1/mods/Example/state",
                Json("{\"enabled\":true}"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.DoesNotContain("\\Mods\\", body, StringComparison.OrdinalIgnoreCase);
        }

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static Host CreateHost(string? role, ModStateChangeResult? result = null)
        {
            var catalog = new Catalog(result ?? ModStateChangeResult.Changed());
            var services = new ServiceCollection();
            services.AddSingleton(new ListModsUseCase(catalog,
                new LoadedQuery(new LoadedModSnapshot(true, new[] { "Example" }))));
            services.AddSingleton(new SetModStateUseCase(catalog));
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

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class Catalog : IModCatalog
        {
            private readonly ModStateChangeResult result;
            public Catalog(ModStateChangeResult result) { this.result = result; }
            public System.Collections.Generic.IReadOnlyList<ModDiskEntry> List() => new[]
            {
                new ModDiskEntry("Example", "Example", "Example", "Author", "1.0", null, null, false, false)
            };
            public ModStateChangeResult SetEnabled(string directoryId, bool enabled) => result;
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class LoadedQuery : ILoadedModQuery
        {
            private readonly LoadedModSnapshot snapshot;
            public LoadedQuery(LoadedModSnapshot snapshot) { this.snapshot = snapshot; }
            public LoadedModSnapshot GetLoadedNames() => snapshot;
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

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

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

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
            public void Dispose()
            {
                Client.Dispose();
                configuration.Dispose();
                provider.Dispose();
            }
        }
    }
}
