using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class OverviewHttpTests
    {
        [Fact]
        public void Overview_attention_uses_required_member_metadata()
        {
            const string requiredMemberAttribute =
                "System.Runtime.CompilerServices.RequiredMemberAttribute";
            var responseType = typeof(OverviewAttentionHttpResponse);
            var codeProperty = responseType.GetProperty(nameof(OverviewAttentionHttpResponse.Code));

            Assert.Contains(
                responseType.GetCustomAttributesData(),
                attribute => attribute.AttributeType.FullName == requiredMemberAttribute);
            Assert.NotNull(codeProperty);
            Assert.Contains(
                codeProperty!.GetCustomAttributesData(),
                attribute => attribute.AttributeType.FullName == requiredMemberAttribute);
        }

        [Fact]
        public async Task Overview_requires_authentication()
        {
            using var host = CreateHost(null, CreateAvailableUseCase());

            using var response = await host.Client.GetAsync("api/v1/overview");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Owner")]
        [InlineData("Admin")]
        [InlineData("Viewer")]
        public async Task Overview_allows_each_read_role(string role)
        {
            using var host = CreateHost(role, CreateAvailableUseCase());

            using var response = await host.Client.GetAsync("api/v1/overview");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Owner_overview_contains_sensitive_host_fields_and_preserves_contract_names()
        {
            using var host = CreateHost("Owner", CreateAvailableUseCase());

            using var response = await host.Client.GetAsync("api/v1/overview");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal("device-1", (string?)json["host"]?["deviceId"]);
            Assert.Equal("system-user", (string?)json["host"]?["currentSystemUser"]);
            Assert.Equal("203.0.113.4", (string?)json["host"]?["publicNetwork"]?["ipv4"]);
            Assert.Equal("2001:db8::4", (string?)json["host"]?["publicNetwork"]?["ipv6"]);
            Assert.Equal("C:\\", (string?)json["host"]?["storageVolumes"]?[0]?["rootPath"]);
            Assert.Equal("7 Days to Die", (string?)json["game"]?["gameTitle"]);
            Assert.Equal("save-1", (string?)json["game"]?["saveGameName"]);
            Assert.Equal("world-1", (string?)json["game"]?["worldName"]);
            Assert.Equal(321L, (long?)json["game"]?["worldSessionUptimeSeconds"]);
            var runtimeMetrics = Assert.IsType<JObject>(json["game"]?["runtimeMetrics"]);
            Assert.Equal(11, runtimeMetrics.Properties().Count());
            Assert.Equal("Day 3", (string?)runtimeMetrics["gameDayTime"]?["value"]);
            Assert.Equal("World.worldTime", (string?)runtimeMetrics["gameDayTime"]?["source"]);
            Assert.Equal("game-clock", (string?)runtimeMetrics["gameDayTime"]?["unit"]);
            Assert.Equal(
                new DateTimeOffset(2026, 7, 25, 1, 2, 3, TimeSpan.Zero),
                (DateTimeOffset?)runtimeMetrics["gameDayTime"]?["observedAtUtc"]);
            Assert.Equal(60d, (double?)runtimeMetrics["framesPerSecond"]?["value"]);
            Assert.Equal(2, (int?)runtimeMetrics["onlinePlayerCount"]?["value"]);
            Assert.Equal(10, (int?)runtimeMetrics["historicalPlayerCount"]?["value"]);
            Assert.Equal("unsupported", (string?)runtimeMetrics["gameMemoryBytes"]?["warning"]);
            Assert.Equal(JTokenType.Null, runtimeMetrics["gameMemoryBytes"]?["value"]?.Type);
            Assert.Equal(456L, (long?)json["host"]?["processUptimeSeconds"]);
            Assert.Equal(789L, (long?)json["host"]?["managedHeapBytes"]);
            Assert.Null(json.SelectToken("$..gameName"));
            Assert.Null(json.SelectToken("$..mapName"));
            Assert.Null(json.SelectToken("$..unityHeapBytes"));
            Assert.Null(json.SelectToken("$..serverUptimeSeconds"));
            Assert.Null(json["game"]?["gameTime"]);
            Assert.Null(json["game"]?["framesPerSecond"]);
            Assert.Null(json["game"]?["onlinePlayerCount"]);
            Assert.Null(json["game"]?["historicalPlayerCount"]);
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("Viewer")]
        public async Task Non_owner_overview_omits_sensitive_host_fields(string role)
        {
            using var host = CreateHost(role, CreateAvailableUseCase());

            using var response = await host.Client.GetAsync("api/v1/overview");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            var hostJson = Assert.IsType<JObject>(json["host"]);
            var networkJson = Assert.IsType<JObject>(hostJson["publicNetwork"]);
            var volumeJson = Assert.IsType<JObject>(hostJson["storageVolumes"]?[0]);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(hostJson.ContainsKey("deviceId"));
            Assert.False(hostJson.ContainsKey("currentSystemUser"));
            Assert.False(networkJson.ContainsKey("ipv4"));
            Assert.False(networkJson.ContainsKey("ipv6"));
            Assert.False(volumeJson.ContainsKey("rootPath"));
        }

        [Fact]
        public async Task Partial_overview_remains_successful_with_partition_availability()
        {
            var useCase = new GetOverviewUseCase(
                new GameQuery(GameOverviewSnapshot.Unavailable()),
                new HostQuery(CreateHostSnapshot()),
                new RestartQuery(RestartPolicySummary.Unavailable()),
                new RecentQuery(RecentActivitySnapshot.Unavailable()));
            using var host = CreateHost("Viewer", useCase);

            using var response = await host.Client.GetAsync("api/v1/overview");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("stale", (string?)json["availability"]);
            Assert.Equal("unavailable", (string?)json["game"]?["availability"]);
            Assert.Equal("available", (string?)json["host"]?["availability"]);
        }

        [Theory]
        [InlineData(HostAdditionalMemoryKind.WindowsVirtualAddressSpace, "virtualAddressSpace")]
        [InlineData(HostAdditionalMemoryKind.LinuxSwap, "swap")]
        public async Task Additional_memory_kind_uses_stable_http_contract(
            HostAdditionalMemoryKind kind,
            string expected)
        {
            using var host = CreateHost("Owner", CreateUseCaseWithAdditionalMemory(kind));

            using var response = await host.Client.GetAsync("api/v1/overview");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expected, (string?)json["host"]?["additionalMemory"]?["kind"]);
        }

        [Fact]
        public async Task Unknown_additional_memory_kind_is_omitted()
        {
            using var host = CreateHost(
                "Owner",
                CreateUseCaseWithAdditionalMemory((HostAdditionalMemoryKind)99));

            using var response = await host.Client.GetAsync("api/v1/overview");
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(json["host"]?["additionalMemory"]);
            Assert.DoesNotContain("99", json.ToString(), StringComparison.Ordinal);
        }

        private static GetOverviewUseCase CreateAvailableUseCase()
        {
            var now = new DateTimeOffset(2026, 7, 25, 1, 2, 3, TimeSpan.Zero);
            return new GetOverviewUseCase(
                new GameQuery(new GameOverviewSnapshot(
                    AvailabilityState.Available, now, "7 Days to Die", "save-1", "world-1",
                    321, "2.4", "Survival", "Nomad", "EU", "English",
                    "127.0.0.1", 26900, 8, CreateRuntimeMetrics(now))),
                new HostQuery(CreateHostSnapshot()),
                new RestartQuery(new RestartPolicySummary(
                    AvailabilityState.Available, true, "daily", now.AddHours(1))),
                new RecentQuery(new RecentActivitySnapshot(
                    AvailabilityState.Available, now, Array.Empty<RecentActivityItem>())));
        }

        private static GameRuntimeMetrics CreateRuntimeMetrics(DateTimeOffset observedAtUtc) =>
            new GameRuntimeMetrics(
                new ObservedMetric<string>("Day 3", "World.worldTime", "game-clock", observedAtUtc, null),
                new ObservedMetric<bool?>(false, "World.aiDirector.BloodMoonComponent.BloodMoonActive", "boolean", observedAtUtc, null),
                new ObservedMetric<double?>(60d, "GameManager.frameTime", "frames/second", observedAtUtc, null),
                new ObservedMetric<int?>(2, "World.Players.Count", "count", observedAtUtc, null),
                new ObservedMetric<int?>(10, "GameManager.persistentPlayerCount", "count", observedAtUtc, null),
                new ObservedMetric<int?>(4, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(9, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(25, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<int?>(144, "Chunk.InstanceCount", "count", observedAtUtc, null),
                new ObservedMetric<int?>(6, "World.Entities", "count", observedAtUtc, null),
                new ObservedMetric<long?>(null, "GC.GetTotalMemory(false)", "bytes", observedAtUtc, RuntimeMetricWarningCode.Unsupported));

        private static GetOverviewUseCase CreateUseCaseWithAdditionalMemory(
            HostAdditionalMemoryKind kind)
        {
            return new GetOverviewUseCase(
                new GameQuery(GameOverviewSnapshot.Unavailable()),
                new HostQuery(CreateHostSnapshot(new HostAdditionalMemory(kind, 200, 50))),
                new RestartQuery(RestartPolicySummary.Unavailable()),
                new RecentQuery(RecentActivitySnapshot.Unavailable()));
        }

        private static HostOverviewSnapshot CreateHostSnapshot(
            HostAdditionalMemory? additionalMemory = null)
        {
            var now = new DateTimeOffset(2026, 7, 25, 1, 2, 3, TimeSpan.Zero);
            return new HostOverviewSnapshot(
                AvailabilityState.Available,
                AvailabilityState.Available,
                now,
                456,
                1024,
                789,
                235,
                12.5,
                "Windows",
                "11",
                8,
                16384,
                8192,
                additionalMemory,
                new[] { new HostStorageVolume("system", "C:\\", 1000, 500, true) },
                new HostPublicNetwork(AvailabilityState.Available, "203.0.113.4", "2001:db8::4"),
                "device-1",
                "system-user");
        }

        private static HttpTestHost CreateHost(string? role, GetOverviewUseCase useCase)
        {
            var services = new ServiceCollection();
            services.AddSingleton(useCase);
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

            public PrincipalHandler(string? role)
            {
                this.role = role;
            }

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

        private sealed class GameQuery : IGameOverviewQuery
        {
            private readonly GameOverviewSnapshot snapshot;
            public GameQuery(GameOverviewSnapshot snapshot) { this.snapshot = snapshot; }
            public Task<GameOverviewSnapshot> GetGameOverviewAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }

        private sealed class HostQuery : IHostOverviewQuery
        {
            private readonly HostOverviewSnapshot snapshot;
            public HostQuery(HostOverviewSnapshot snapshot) { this.snapshot = snapshot; }
            public Task<HostOverviewSnapshot> GetHostOverviewAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }

        private sealed class RestartQuery : IRestartPolicyQuery
        {
            private readonly RestartPolicySummary summary;
            public RestartQuery(RestartPolicySummary summary) { this.summary = summary; }
            public RestartPolicySummary Query() => summary;
        }

        private sealed class RecentQuery : IRecentActivityQuery
        {
            private readonly RecentActivitySnapshot snapshot;
            public RecentQuery(RecentActivitySnapshot snapshot) { this.snapshot = snapshot; }
            public Task<RecentActivitySnapshot> GetRecentActivityAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }
    }
}
