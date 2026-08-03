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
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.GeoIp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Web")]
    public sealed class GeoIpHttpTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

        public static TheoryData<string, string, string?, HttpStatusCode> AuthorizationMatrix => new()
        {
            { "GET", "api/v1/access-policies/geoip", null, HttpStatusCode.Unauthorized },
            { "GET", "api/v1/access-policies/geoip", "Admin", HttpStatusCode.Forbidden },
            { "GET", "api/v1/access-policies/geoip", "Owner", HttpStatusCode.OK },
            { "PUT", "api/v1/access-policies/geoip", null, HttpStatusCode.Unauthorized },
            { "PUT", "api/v1/access-policies/geoip", "Admin", HttpStatusCode.Forbidden },
            { "PUT", "api/v1/access-policies/geoip", "Owner", HttpStatusCode.OK },
            { "PUT", "api/v1/access-policies/geoip/credentials", null, HttpStatusCode.Unauthorized },
            { "PUT", "api/v1/access-policies/geoip/credentials", "Admin", HttpStatusCode.Forbidden },
            { "PUT", "api/v1/access-policies/geoip/credentials", "Owner", HttpStatusCode.OK },
            { "POST", "api/v1/access-policies/geoip/test", null, HttpStatusCode.Unauthorized },
            { "POST", "api/v1/access-policies/geoip/test", "Admin", HttpStatusCode.Forbidden },
            { "POST", "api/v1/access-policies/geoip/test", "Owner", HttpStatusCode.Accepted },
            { "GET", "api/v1/access-policies/geoip/diagnostics", null, HttpStatusCode.Unauthorized },
            { "GET", "api/v1/access-policies/geoip/diagnostics", "Admin", HttpStatusCode.Forbidden },
            { "GET", "api/v1/access-policies/geoip/diagnostics", "Owner", HttpStatusCode.OK }
        };

        [Theory]
        [MemberData(nameof(AuthorizationMatrix))]
        public async Task All_geoip_routes_are_owner_only(
            string method,
            string path,
            string? role,
            HttpStatusCode expected)
        {
            using var host = CreateHost(role);
            using var request = CreateRequest(method, path);

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Read_summary_exposes_typed_rules_health_versions_and_only_masked_decisions()
        {
            var store = new MemoryGeoIpStore();
            store.NetworkRules.Add(new GeoIpNetworkRule("office", "203.0.113.0/24", "Deny", 1));
            store.CountryRules.Add(new GeoIpCountryRule("CA", "Allow"));
            store.Decisions.Add(new GeoIpDecision(
                "decision-1",
                Now,
                "203.0.113.42",
                "EOS_1",
                "Deny",
                "country_denied",
                "Found"));
            store.Secrets[GeoIpSecretKeys.MaxMindLicenseKey] = new GeoIpSecretValue(
                GeoIpSecretKeys.MaxMindLicenseKey,
                "license-secret-never-return",
                "fingerprint-secret-never-return",
                Now);
            var diagnostics = new StaticDiagnostics(new GeoIpRefreshDiagnostics(
                true,
                2,
                3,
                Now,
                GeoIpLookupStatus.Found,
                new[]
                {
                    new GeoIpProviderMetadata(
                        GeoIpProviderNames.LocalMmdb,
                        false,
                        "0123456789abcdef",
                        "1785100800")
                }));
            using var host = CreateHost("Owner", store, diagnostics: diagnostics);

            using var response = await host.Client.GetAsync(
                "api/v1/access-policies/geoip",
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("203.0.113.0/24", (string?)json["networkRules"]?[0]?["networkCidr"]);
            Assert.Equal("CA", (string?)json["countryRules"]?[0]?["countryCode"]);
            Assert.Equal(2, (int?)json["cacheHealth"]?["queueDepth"]);
            Assert.Equal("0123456789abcdef", (string?)json["providers"]?[0]?["dataVersion"]);
            Assert.Equal("203.0.113.0/24", (string?)json["recentDecisions"]?[0]?["maskedIp"]);
            Assert.DoesNotContain("203.0.113.42", body, StringComparison.Ordinal);
            Assert.DoesNotContain("license-secret-never-return", body, StringComparison.Ordinal);
            Assert.DoesNotContain("fingerprint-secret-never-return", body, StringComparison.Ordinal);
            Assert.DoesNotContain("EOS_1", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Diagnostics_drops_arbitrary_provider_paths_urls_and_errors_and_highlights_fail_open()
        {
            var diagnostics = new StaticDiagnostics(new GeoIpRefreshDiagnostics(
                true,
                0,
                1,
                Now,
                GeoIpLookupStatus.Unknown,
                new[]
                {
                    new GeoIpProviderMetadata(
                        GeoIpProviderNames.LocalMmdb,
                        false,
                        @"D:\private\GeoLite2-Country.mmdb",
                        "https://provider.invalid/error?credential=secret")
                }));
            using var host = CreateHost("Owner", diagnostics: diagnostics);

            using var response = await host.Client.GetAsync(
                "api/v1/access-policies/geoip/diagnostics",
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Warning", (string?)json["severity"]);
            Assert.Equal("fail_open_active", (string?)json["statusCode"]);
            Assert.Null(json["providers"]?[0]?["dataVersion"]?.Value<string>());
            Assert.Null(json["providers"]?[0]?["buildEpoch"]?.Value<string>());
            Assert.DoesNotContain("GeoLite2-Country.mmdb", body, StringComparison.Ordinal);
            Assert.DoesNotContain("provider.invalid", body, StringComparison.Ordinal);
            Assert.DoesNotContain("credential", body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Put_canonicalizes_and_persists_typed_configuration_and_rules()
        {
            var store = new MemoryGeoIpStore();
            using var host = CreateHost("Owner", store);
            const string requestBody = "{" +
                "\"expectedVersion\":7," +
                "\"isEnabled\":true," +
                "\"provider\":\"MaxMindWebService\"," +
                "\"failureMode\":\"FailClosed\"," +
                "\"bypassAdmins\":false," +
                "\"rejectionMessage\":\"Connection denied by server policy.\"," +
                "\"networkRules\":[" +
                    "{\"ruleId\":\"exact\",\"networkCidr\":\"203.0.113.42\",\"effect\":\"deny\",\"ordinal\":2}," +
                    "{\"ruleId\":\"v6\",\"networkCidr\":\"2001:db8:abcd:1234::1/48\",\"effect\":\"allow\",\"ordinal\":1}" +
                "]," +
                "\"countryRules\":[{\"countryCode\":\"ca\",\"effect\":\"allow\"}]" +
                "}";

            using var response = await host.Client.PutAsync(
                "api/v1/access-policies/geoip",
                Json(requestBody),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(8, store.Settings.Version);
            Assert.Equal(GeoIpProviderNames.MaxMindWebService, store.Settings.Provider);
            Assert.Equal(GeoIpFailureMode.FailClosed, store.Settings.FailureMode);
            Assert.Equal("203.0.113.42/32", store.NetworkRules[0].NetworkCidr);
            Assert.Equal("Deny", store.NetworkRules[0].Effect);
            Assert.Equal("2001:db8:abcd::/48", store.NetworkRules[1].NetworkCidr);
            Assert.Equal("CA", store.CountryRules.Single().CountryCode);
            Assert.Equal("Allow", store.CountryRules.Single().Effect);
        }

        [Theory]
        [InlineData("UnknownProvider", "FailClosed", "203.0.113.0/24", "Deny", "CA")]
        [InlineData("LocalMmdb", "Maybe", "203.0.113.0/24", "Deny", "CA")]
        [InlineData("LocalMmdb", "FailClosed", "bad-cidr", "Deny", "CA")]
        [InlineData("LocalMmdb", "FailClosed", "203.0.113.0/24", "Maybe", "CA")]
        [InlineData("LocalMmdb", "FailClosed", "203.0.113.0/24", "Deny", "CAN")]
        public async Task Put_rejects_unapproved_or_malformed_typed_values(
            string provider,
            string failureMode,
            string cidr,
            string effect,
            string country)
        {
            using var host = CreateHost("Owner");
            var body = "{" +
                "\"expectedVersion\":7," +
                "\"isEnabled\":true," +
                "\"provider\":\"" + provider + "\"," +
                "\"failureMode\":\"" + failureMode + "\"," +
                "\"bypassAdmins\":true," +
                "\"rejectionMessage\":\"Denied\"," +
                "\"networkRules\":[{\"ruleId\":\"rule-1\",\"networkCidr\":\"" + cidr + "\",\"effect\":\"" + effect + "\",\"ordinal\":0}]," +
                "\"countryRules\":[{\"countryCode\":\"" + country + "\",\"effect\":\"Allow\"}]" +
                "}";

            using var response = await host.Client.PutAsync(
                "api/v1/access-policies/geoip",
                Json(body),
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid_geoip_policy", (string?)problem["code"]);
        }

        [Fact]
        public async Task Put_maps_stale_version_to_a_stable_conflict()
        {
            var store = new MemoryGeoIpStore { RejectSaveAsConflict = true };
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.PutAsync(
                "api/v1/access-policies/geoip",
                Json(ValidPutBody()),
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("geoip_settings_version_conflict", (string?)problem["code"]);
        }

        [Fact]
        public async Task Credentials_endpoint_atomically_keeps_replaces_and_clears_without_echoing_values()
        {
            var store = new MemoryGeoIpStore();
            using var host = CreateHost("Owner", store);
            const string firstAccountId = "12345";
            const string firstLicenseKey = "license-value-one";

            using var replaced = await host.Client.PutAsync(
                "api/v1/access-policies/geoip/credentials",
                Json("{" +
                    "\"accountId\":{\"operation\":\"Replace\",\"value\":\"" + firstAccountId + "\"}," +
                    "\"licenseKey\":{\"operation\":\"Replace\",\"value\":\"" + firstLicenseKey + "\"}" +
                    "}"),
                TestContext.Current.CancellationToken);
            var replacedBody = await replaced.Content.ReadAsStringAsync();
            var replacedJson = JObject.Parse(replacedBody);

            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            Assert.True((bool?)replacedJson["accountId"]?["isSet"]);
            Assert.True((bool?)replacedJson["licenseKey"]?["isSet"]);
            Assert.NotNull((string?)replacedJson["accountId"]?["fingerprint"]);
            Assert.NotNull((string?)replacedJson["licenseKey"]?["fingerprint"]);
            Assert.DoesNotContain(firstAccountId, replacedBody, StringComparison.Ordinal);
            Assert.DoesNotContain(firstLicenseKey, replacedBody, StringComparison.Ordinal);
            Assert.Equal(firstAccountId, store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Equal(firstLicenseKey, store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey)!.SecretValue);

            using var cleared = await host.Client.PutAsync(
                "api/v1/access-policies/geoip/credentials",
                Json("{" +
                    "\"accountId\":{\"operation\":\"Keep\"}," +
                    "\"licenseKey\":{\"operation\":\"Clear\"}" +
                    "}"),
                TestContext.Current.CancellationToken);
            var clearedBody = await cleared.Content.ReadAsStringAsync();
            var clearedJson = JObject.Parse(clearedBody);

            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.True((bool?)clearedJson["accountId"]?["isSet"]);
            Assert.False((bool?)clearedJson["licenseKey"]?["isSet"]);
            Assert.DoesNotContain(firstAccountId, clearedBody, StringComparison.Ordinal);
            Assert.DoesNotContain(firstLicenseKey, clearedBody, StringComparison.Ordinal);
            Assert.Equal(firstAccountId, store.GetSecret(GeoIpSecretKeys.MaxMindAccountId)!.SecretValue);
            Assert.Null(store.GetSecret(GeoIpSecretKeys.MaxMindLicenseKey));
        }

        [Fact]
        public async Task Test_endpoint_only_enqueues_and_returns_a_masked_address()
        {
            var queue = new RecordingRefreshQueue(true);
            using var host = CreateHost("Owner", queue: queue);

            using var response = await host.Client.PostAsync(
                "api/v1/access-policies/geoip/test",
                Json("{\"ipAddress\":\"203.0.113.42\"}"),
                TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("203.0.113.0/24", (string?)json["maskedIp"]);
            Assert.Equal("queued", (string?)json["state"]);
            Assert.DoesNotContain("203.0.113.42", body, StringComparison.Ordinal);
            var queued = Assert.Single(queue.Requests);
            Assert.Equal("203.0.113.42", queued.CanonicalIp);
            Assert.Equal(GeoIpProviderNames.LocalMmdb, queued.Provider);
            Assert.Equal(7, queued.SettingsVersion);
        }

        [Fact]
        public async Task Test_endpoint_types_disabled_and_full_queue_failures_without_leaking_input()
        {
            var disabledStore = new MemoryGeoIpStore
            {
                Settings = new GeoIpAccessPolicySettings(
                    7,
                    false,
                    GeoIpProviderNames.LocalMmdb,
                    GeoIpFailureMode.FailOpen,
                    true,
                    GeoIpPolicyDecision.DefaultRejectionMessage)
            };
            using (var disabled = CreateHost("Owner", disabledStore))
            using (var response = await disabled.Client.PostAsync(
                       "api/v1/access-policies/geoip/test",
                       Json("{\"ipAddress\":\"203.0.113.42\"}"),
                       TestContext.Current.CancellationToken))
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Contains("geoip_policy_disabled", body, StringComparison.Ordinal);
                Assert.DoesNotContain("203.0.113.42", body, StringComparison.Ordinal);
            }

            using var full = CreateHost("Owner", queue: new RecordingRefreshQueue(false));
            using var fullResponse = await full.Client.PostAsync(
                "api/v1/access-policies/geoip/test",
                Json("{\"ipAddress\":\"203.0.113.42\"}"),
                TestContext.Current.CancellationToken);
            var fullBody = await fullResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, fullResponse.StatusCode);
            Assert.Contains("geoip_refresh_unavailable", fullBody, StringComparison.Ordinal);
            Assert.DoesNotContain("203.0.113.42", fullBody, StringComparison.Ordinal);
        }

        private static string ValidPutBody() => "{" +
            "\"expectedVersion\":7," +
            "\"isEnabled\":true," +
            "\"provider\":\"LocalMmdb\"," +
            "\"failureMode\":\"FailOpen\"," +
            "\"bypassAdmins\":true," +
            "\"rejectionMessage\":\"Denied\"," +
            "\"networkRules\":[]," +
            "\"countryRules\":[]" +
            "}";

        private static HttpRequestMessage CreateRequest(string method, string path)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "PUT" && path.EndsWith("/credentials", StringComparison.Ordinal))
            {
                request.Content = Json("{" +
                    "\"accountId\":{\"operation\":\"Keep\"}," +
                    "\"licenseKey\":{\"operation\":\"Keep\"}" +
                    "}");
            }
            else if (method == "PUT") request.Content = Json(ValidPutBody());
            if (method == "POST") request.Content = Json("{\"ipAddress\":\"203.0.113.42\"}");
            return request;
        }

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static Host CreateHost(
            string? role,
            MemoryGeoIpStore? store = null,
            RecordingRefreshQueue? queue = null,
            StaticDiagnostics? diagnostics = null)
        {
            store ??= new MemoryGeoIpStore();
            queue ??= new RecordingRefreshQueue(true);
            diagnostics ??= new StaticDiagnostics(new GeoIpRefreshDiagnostics(
                true,
                0,
                0,
                null,
                null,
                Array.Empty<GeoIpProviderMetadata>()));

            var services = new ServiceCollection();
            services.AddSingleton<IGeoIpAccessPolicyStore>(store);
            services.AddSingleton<IGeoIpRefreshQueue>(queue);
            services.AddSingleton<IGeoIpRefreshDiagnostics>(diagnostics);
            services.AddTransient<GetGeoIpDiagnosticsUseCase>();
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
            configuration.MessageHandlers.Add(new ApiProblemDetailsHandler());
            configuration.EnsureInitialized();
            return new Host(provider, configuration);
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

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class PrincipalHandler : DelegatingHandler
        {
            private readonly string? role;

            public PrincipalHandler(string? role) => this.role = role;

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

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class RecordingRefreshQueue : IGeoIpRefreshQueue
        {
            private readonly bool accepts;

            public RecordingRefreshQueue(bool accepts) => this.accepts = accepts;

            public List<GeoIpRefreshRequest> Requests { get; } =
                new List<GeoIpRefreshRequest>();

            public bool TryWrite(GeoIpRefreshRequest request)
            {
                Requests.Add(request);
                return accepts;
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class StaticDiagnostics : IGeoIpRefreshDiagnostics
        {
            private readonly GeoIpRefreshDiagnostics diagnostics;

            public StaticDiagnostics(GeoIpRefreshDiagnostics diagnostics) =>
                this.diagnostics = diagnostics;

            public GeoIpRefreshDiagnostics GetDiagnostics() => diagnostics;
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Web")]

        private sealed class MemoryGeoIpStore : IGeoIpAccessPolicyStore
        {
            public MemoryGeoIpStore()
            {
                Settings = new GeoIpAccessPolicySettings(
                    7,
                    true,
                    GeoIpProviderNames.LocalMmdb,
                    GeoIpFailureMode.FailOpen,
                    true,
                    GeoIpPolicyDecision.DefaultRejectionMessage);
            }

            public GeoIpAccessPolicySettings Settings { get; set; }
            public bool RejectSaveAsConflict { get; set; }
            public List<GeoIpNetworkRule> NetworkRules { get; } = new List<GeoIpNetworkRule>();
            public List<GeoIpCountryRule> CountryRules { get; } = new List<GeoIpCountryRule>();
            public List<GeoIpDecision> Decisions { get; } = new List<GeoIpDecision>();
            public Dictionary<string, GeoIpSecretValue> Secrets { get; } =
                new Dictionary<string, GeoIpSecretValue>(StringComparer.Ordinal);

            public GeoIpAccessPolicySettings? GetSettings() => Settings;

            public void SaveSettings(GeoIpAccessPolicySettings settings, long expectedVersion)
            {
                if (RejectSaveAsConflict || expectedVersion != Settings.Version)
                    throw new GeoIpAccessPolicyVersionConflictException();
                Settings = settings;
            }

            public void SetSecret(GeoIpSecretValue secret) => Secrets[secret.SecretKey] = secret;

            public void ApplySecretChanges(IReadOnlyList<GeoIpSecretMutation> changes)
            {
                var next = new Dictionary<string, GeoIpSecretValue>(Secrets, StringComparer.Ordinal);
                foreach (var change in changes)
                {
                    if (change.Replacement == null) next.Remove(change.SecretKey);
                    else next[change.SecretKey] = change.Replacement;
                }
                Secrets.Clear();
                foreach (var pair in next) Secrets.Add(pair.Key, pair.Value);
            }

            public GeoIpSecretValue? GetSecret(string secretKey) =>
                Secrets.TryGetValue(secretKey, out var value) ? value : null;

            public IReadOnlyList<GeoIpSecretMetadata> ListSecretMetadata() =>
                Secrets.Values.Select(value => new GeoIpSecretMetadata(
                    value.SecretKey,
                    value.Fingerprint,
                    value.UpdatedAtUtc)).ToArray();

            public void ReplaceNetworkRules(IReadOnlyList<GeoIpNetworkRule> rules)
            {
                NetworkRules.Clear();
                NetworkRules.AddRange(rules);
            }

            public IReadOnlyList<GeoIpNetworkRule> ListNetworkRules() => NetworkRules.ToArray();

            public void ReplaceCountryRules(IReadOnlyList<GeoIpCountryRule> rules)
            {
                CountryRules.Clear();
                CountryRules.AddRange(rules);
            }

            public IReadOnlyList<GeoIpCountryRule> ListCountryRules() => CountryRules.ToArray();

            public void UpsertCache(GeoIpCacheEntry entry) { }
            public GeoIpCacheEntry? FindCache(string ipAddress) => null;
            public void RecordDecision(GeoIpDecision decision) => Decisions.Add(decision);

            public GeoIpDecisionPage QueryDecisions(GeoIpDecisionQuery query) =>
                new GeoIpDecisionPage(
                    Decisions.Take(query.PageSize).ToArray(),
                    null);
        }
    }
}
