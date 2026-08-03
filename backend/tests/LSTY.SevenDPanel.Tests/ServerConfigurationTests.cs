using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.ServerConfiguration;
using LSTY.SevenDPanel.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ServerConfigurationTests
    {
        [Fact]
        public void Catalog_covers_every_enabled_v3_0_1_b4_official_field_with_typed_metadata()
        {
            var catalog = ServerConfigurationFieldCatalog.Create();
            var officialKeys = ("ServerName ServerDescription ServerWebsiteURL ServerPassword ServerLoginConfirmationText " +
                "Region Language ServerPort ServerVisibility ServerDisabledNetworkProtocols ServerMaxWorldTransferSpeedKiBs " +
                "ServerMaxPlayerCount ServerReservedSlots ServerReservedSlotsPermission ServerAdminSlots ServerAdminSlotsPermission " +
                "WebDashboardEnabled WebDashboardPort WebDashboardUrl EnableMapRendering TelnetEnabled TelnetPort TelnetPassword " +
                "TelnetFailedLoginLimit TelnetFailedLoginsBlocktime TerminalWindowEnabled AdminFileName ServerAllowCrossplay " +
                "EACEnabled IgnoreEOSSanctions HideCommandExecutionLog MaxUncoveredMapChunksPerPlayer PersistentPlayerProfiles " +
                "MaxChunkAge SaveDataLimit GameWorld WorldGenSeed WorldGenSize GameName GameMode PlayerSafeZoneLevel " +
                "PlayerSafeZoneHours BuildCreate BedrollDeadZoneSize BedrollExpiryTime AllowSpawnNearFriend CameraRestrictionMode " +
                "MaxSpawnedZombies MaxSpawnedAnimals ServerMaxAllowedViewDistance MaxQueuedMeshLayers PartySharedKillRange " +
                "PlayerKillingMode LandClaimCount LandClaimSize LandClaimDeadZone LandClaimExpiryTime LandClaimDecayMode " +
                "LandClaimOnlineDurabilityModifier LandClaimOfflineDurabilityModifier LandClaimOfflineDelay DynamicMeshEnabled " +
                "DynamicMeshLandClaimOnly DynamicMeshLandClaimBuffer DynamicMeshMaxItemCache TwitchServerPermission " +
                "TwitchBloodMoonAllowed SandboxCode").Split(' ');

            Assert.Equal(68, officialKeys.Length);
            Assert.All(officialKeys, key => Assert.True(catalog.TryGet(key, out _), key));
            Assert.True(catalog.TryGet("WebDashboardEnabled", out var booleanField));
            Assert.Equal(ServerConfigurationValueType.Boolean, booleanField.ValueType);
            Assert.True(catalog.TryGet("Region", out var enumField));
            Assert.Equal(ServerConfigurationValueType.Enum, enumField.ValueType);
            Assert.Contains("Oceania", enumField.AllowedValues);
            Assert.True(catalog.TryGet("ServerMaxPlayerCount", out var integerField));
            Assert.Equal(ServerConfigurationValueType.Integer, integerField.ValueType);
            Assert.True(catalog.TryGet("ServerDescription", out var textField));
            Assert.Equal(ServerConfigurationValueType.Text, textField.ValueType);
        }

        [Fact]
        public void Store_edits_existing_advanced_fields_redacts_secrets_and_rejects_missing_or_stale_fields()
        {
            using var fixture = new ConfigurationFixture(
                "<ServerSettings><!--keep--><property name=\"ServerName\" value=\"Old\"/>" +
                "<property name=\"ServerPassword\" value=\"secret\"/>" +
                "<property name=\"FutureToken\" value=\"hidden\"/>" +
                "<property name=\"FutureField\" value=\"keep\"/></ServerSettings>");
            var before = fixture.Store.Read(fixture.Catalog);

            Assert.Equal(string.Empty, before.Fields.Single(field => field.Key == "ServerPassword").Value);
            Assert.True(before.Fields.Single(field => field.Key == "ServerPassword").Sensitive);
            Assert.True(before.Fields.Single(field => field.Key == "ServerPassword").IsSet);
            Assert.True(before.Fields.Single(field => field.Key == "FutureField").Editable);
            Assert.True(before.Fields.Single(field => field.Key == "FutureField").Advanced);
            Assert.Equal(string.Empty, before.Fields.Single(field => field.Key == "FutureToken").Value);
            Assert.False(before.Fields.Single(field => field.Key == "FutureToken").Editable);

            var result = fixture.Store.Update(
                new UpdateServerConfigurationRequest("FutureField", "changed", before.Version),
                fixture.Catalog);
            var after = fixture.Store.Read(fixture.Catalog);

            Assert.Equal(ServerConfigurationUpdateStatus.Updated, result.Status);
            Assert.Equal("changed", after.Fields.Single(field => field.Key == "FutureField").Value);
            Assert.Contains("FutureField", File.ReadAllText(fixture.Path));
            Assert.Contains("<!--keep-->", File.ReadAllText(fixture.Path));
            Assert.Equal(ServerConfigurationUpdateStatus.UnknownField,
                fixture.Store.Update(
                    new UpdateServerConfigurationRequest("MissingField", "new", after.Version),
                    fixture.Catalog).Status);
            Assert.Equal(ServerConfigurationUpdateStatus.ReadOnly,
                fixture.Store.Update(
                    new UpdateServerConfigurationRequest("FutureToken", "leaked", after.Version),
                    fixture.Catalog).Status);
            Assert.Equal(ServerConfigurationUpdateStatus.Conflict,
                fixture.Store.Update(
                    new UpdateServerConfigurationRequest("FutureField", "Again", before.Version),
                    fixture.Catalog).Status);
        }

        [Theory]
        [InlineData("ServerMaxPlayerCount", "0")]
        [InlineData("ServerMaxPlayerCount", "9.5")]
        [InlineData("ServerPort", "70000")]
        [InlineData("GameDifficulty", "6")]
        [InlineData("WebDashboardEnabled", "enabled")]
        [InlineData("Region", "Moon")]
        public void Store_rejects_invalid_typed_values(string key, string value)
        {
            using var fixture = new ConfigurationFixture(
                $"<ServerSettings><property name=\"{key}\" value=\"1\"/></ServerSettings>");
            var snapshot = fixture.Store.Read(fixture.Catalog);

            var result = fixture.Store.Update(
                new UpdateServerConfigurationRequest(key, value, snapshot.Version),
                fixture.Catalog);

            Assert.Equal(ServerConfigurationUpdateStatus.InvalidValue, result.Status);
        }

        [Fact]
        public void Store_rejects_file_paths_from_official_and_advanced_fields()
        {
            using var fixture = new ConfigurationFixture(
                "<ServerSettings><property name=\"AdminFileName\" value=\"serveradmin.xml\"/>" +
                "<property name=\"UserDataFolder\" value=\"data\"/></ServerSettings>");
            var snapshot = fixture.Store.Read(fixture.Catalog);

            Assert.Equal(ServerConfigurationUpdateStatus.InvalidValue,
                fixture.Store.Update(
                    new UpdateServerConfigurationRequest("AdminFileName", "../outside.xml", snapshot.Version),
                    fixture.Catalog).Status);
            Assert.False(snapshot.Fields.Single(field => field.Key == "UserDataFolder").Editable);
        }

        [Theory]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData("Owner", HttpStatusCode.OK)]
        public async Task Configuration_endpoint_requires_owner(string? role, HttpStatusCode expected)
        {
            using var host = CreateHttpHost(role, new StubStore("v2"));

            using var response = await host.Client.GetAsync("api/v1/server-configuration");

            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Configuration_endpoint_maps_stale_updates_to_stable_conflict_problem()
        {
            using var host = CreateHttpHost("Owner", new StubStore("v2"));

            using var response = await host.Client.PutAsync(
                "api/v1/server-configuration/ServerName",
                new StringContent("{\"value\":\"new\",\"version\":\"v1\"}", System.Text.Encoding.UTF8, "application/json"));
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("configuration_version_conflict", (string?)problem["code"]);
        }

        private static HttpFixture CreateHttpHost(string? role, IServerConfigurationStore store)
        {
            var catalog = ServerConfigurationFieldCatalog.Create();
            var services = new ServiceCollection();
            services.AddSingleton(new GetServerConfigurationUseCase(store, catalog));
            services.AddSingleton(new UpdateServerConfigurationUseCase(store, catalog));
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
            configuration.MessageHandlers.Add(new ApiProblemDetailsHandler());
            configuration.EnsureInitialized();
            return new HttpFixture(provider, configuration);
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class StubStore : IServerConfigurationStore
        {
            private readonly string version;
            public StubStore(string version) { this.version = version; }

            public ServerConfigurationSnapshot Read(ServerConfigurationFieldCatalog catalog)
            {
                return new ServerConfigurationSnapshot(version, DateTimeOffset.UtcNow, Array.Empty<ServerConfigurationField>());
            }

            public ServerConfigurationUpdateResult Update(
                UpdateServerConfigurationRequest request,
                ServerConfigurationFieldCatalog catalog)
            {
                return request.Version == version
                    ? new ServerConfigurationUpdateResult(ServerConfigurationUpdateStatus.Updated, version, DateTimeOffset.UtcNow, false)
                    : new ServerConfigurationUpdateResult(ServerConfigurationUpdateStatus.Conflict, version, null, false);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class ConfigurationFixture : IDisposable
        {
            private readonly string directory;

            public ConfigurationFixture(string xml)
            {
                directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "7dpanel-config-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, "serverconfig.xml");
                File.WriteAllText(Path, xml);
                Catalog = ServerConfigurationFieldCatalog.Create();
                Store = new LocalServerConfigurationStore(Path);
            }

            public string Path { get; }
            public ServerConfigurationFieldCatalog Catalog { get; }
            public LocalServerConfigurationStore Store { get; }

            public void Dispose()
            {
                Directory.Delete(directory, true);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class HttpFixture : IDisposable
        {
            private readonly ServiceProvider provider;
            private readonly HttpConfiguration configuration;

            public HttpFixture(ServiceProvider provider, HttpConfiguration configuration)
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

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

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
                var context = new OwinContext();
                context.Authentication.User = principal;
                request.SetOwinContext(context);
                request.GetRequestContext().Principal = principal;
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}
