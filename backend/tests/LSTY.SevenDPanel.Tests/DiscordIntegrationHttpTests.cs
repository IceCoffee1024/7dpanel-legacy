using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Application.Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class DiscordIntegrationHttpTests
    {
        [Fact]
        public void Controller_exposes_only_the_fixed_routes_with_owner_management_and_anonymous_interactions()
        {
            var type = typeof(DiscordIntegrationController);
            Assert.Equal("Owner", type.GetCustomAttribute<AuthorizeAttribute>()?.Roles);
            Assert.Equal(
                "api/v1/integrations/discord",
                type.GetCustomAttribute<RoutePrefixAttribute>()?.Prefix);

            AssertRoute(type, "GetConfiguration", "", typeof(HttpGetAttribute));
            AssertRoute(type, "GetHealth", "health", typeof(HttpGetAttribute));
            AssertRoute(type, "PutConfiguration", "", typeof(HttpPutAttribute));
            AssertRoute(type, "Test", "test", typeof(HttpPostAttribute));
            AssertRoute(type, "GetDeliveries", "deliveries", typeof(HttpGetAttribute));
            AssertRoute(
                type,
                "RetryDelivery",
                "deliveries/{deliveryId}/retry",
                typeof(HttpPostAttribute));
            AssertRoute(type, "GetBindings", "bindings", typeof(HttpGetAttribute));
            AssertRoute(type, "CreateBindingCode", "binding-codes", typeof(HttpPostAttribute));
            AssertRoute(
                type,
                "DeleteBinding",
                "bindings/{discordSubject}",
                typeof(HttpDeleteAttribute));
            AssertRoute(type, "GetCommands", "commands", typeof(HttpGetAttribute));
            AssertRoute(type, "PostInteraction", "interactions", typeof(HttpPostAttribute));
            Assert.NotNull(type.GetMethod("PostInteraction")!.GetCustomAttribute<AllowAnonymousAttribute>());
        }

        [Theory]
        [InlineData("Owner", HttpStatusCode.OK)]
        [InlineData("Admin", HttpStatusCode.Forbidden)]
        [InlineData("Viewer", HttpStatusCode.Forbidden)]
        [InlineData(null, HttpStatusCode.Unauthorized)]
        public async Task Management_routes_are_owner_only(
            string? role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role, ConfiguredStore());

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord",
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        [Fact]
        public async Task Configuration_get_returns_safe_metadata_without_secret_values_or_fingerprints()
        {
            var store = ConfiguredStore();
            store.SetSecret(new DiscordSecretValue(
                DiscordSecretKeys.BotToken,
                "bot-token-value",
                "bot-fingerprint",
                FixedNow));
            store.SetSecret(new DiscordSecretValue(
                DiscordSecretKeys.WebhookUrl("alerts"),
                "https://discord.example/webhook/secret",
                "webhook-fingerprint",
                FixedNow));
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord",
                TestContext.Current.CancellationToken);
            var json = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(json);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True((bool?)payload["hasBotToken"]);
            Assert.True((bool?)payload["targets"]![0]!["hasCredential"]);
            Assert.Null(payload["secrets"]);
            Assert.DoesNotContain("bot-token-value", json, StringComparison.Ordinal);
            Assert.DoesNotContain("webhook/secret", json, StringComparison.Ordinal);
            Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Health_get_returns_the_configured_runtime_snapshot()
        {
            var store = ConfiguredStore();
            store.Settings = store.Settings! with
            {
                Mode = DiscordIntegrationMode.Bot,
                BridgeDiscordToGame = true
            };
            store.SetSecret(new DiscordSecretValue(
                DiscordSecretKeys.BotToken,
                "bot-token-value",
                "bot-fingerprint",
                FixedNow));
            store.Health = new DiscordHealthSnapshot(
                new DiscordHealthSection(
                    DiscordHealthState.Connected,
                    null,
                    FixedNow.AddSeconds(1)),
                new DiscordHealthSection(
                    DiscordHealthState.Healthy,
                    null,
                    FixedNow));
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord/health",
                TestContext.Current.CancellationToken);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Connected", (string?)payload["gateway"]!["state"]);
            Assert.Equal("Healthy", (string?)payload["inbound"]!["state"]);
            Assert.Null((string?)payload["gateway"]!["errorCode"]);
            Assert.Equal(
                FixedNow.AddSeconds(1),
                (DateTimeOffset?)payload["gateway"]!["observedAtUtc"]);
        }

        [Fact]
        public async Task Health_get_fails_closed_when_runtime_health_cannot_be_read()
        {
            var store = ConfiguredStore();
            store.ThrowOnHealthRead = true;
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord/health",
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("discord_health_unavailable", (string?)problem["code"]);
        }

        [Fact]
        public async Task Health_get_does_not_mask_an_incomplete_gateway_configuration()
        {
            var store = ConfiguredStore();
            store.Settings = store.Settings! with
            {
                Mode = DiscordIntegrationMode.Bot,
                BridgeDiscordToGame = true
            };
            store.Health = new DiscordHealthSnapshot(
                new DiscordHealthSection(DiscordHealthState.Connected, null, FixedNow),
                new DiscordHealthSection(DiscordHealthState.Healthy, null, FixedNow));
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord/health",
                TestContext.Current.CancellationToken);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Unavailable", (string?)payload["gateway"]!["state"]);
            Assert.Equal(
                "discord_gateway_configuration_incomplete",
                (string?)payload["gateway"]!["errorCode"]);
        }

        [Fact]
        public async Task Configuration_put_uses_optimistic_versioning_without_accepting_secret_fields()
        {
            var store = ConfiguredStore();
            using var host = CreateHost("Owner", store);
            const string body = """
                {
                  "expectedVersion": 7,
                  "isEnabled": false,
                  "mode": "Webhook",
                  "applicationId": "application-2",
                  "guildId": "guild-2",
                  "publicChannelId": null,
                  "bridgeGameToDiscord": false,
                  "bridgeDiscordToGame": false,
                  "proxyEnabled": false,
                  "proxyEndpoint": null,
                  "targets": [
                    {
                      "targetKey": "alerts",
                      "deliveryMode": "Webhook",
                      "channelId": null,
                      "isEnabled": true
                    }
                  ]
                }
                """;

            using var response = await PutJsonAsync(
                host.Client,
                "api/v1/integrations/discord",
                body);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(8, (long?)payload["version"]);
            Assert.False(store.Settings!.IsEnabled);
            Assert.Equal("application-2", store.Settings.ApplicationId);
        }

        [Fact]
        public async Task Test_queues_a_closed_synthetic_delivery_without_returning_message_content()
        {
            var store = ConfiguredStore();
            using var host = CreateHost("Owner", store);

            using var response = await PostJsonAsync(
                host.Client,
                "api/v1/integrations/discord/test",
                "{\"targetKey\":\"alerts\"}");
            var json = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(json);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("Pending", (string?)payload["status"]);
            var delivery = Assert.Single(store.Deliveries.Values);
            Assert.Equal("7DPanel Discord integration test.", delivery.ContentText);
            Assert.DoesNotContain(delivery.ContentText!, json, StringComparison.Ordinal);
            Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Retry_reuses_the_stored_body_but_never_returns_it()
        {
            var store = ConfiguredStore();
            store.Deliveries.Add(
                "delivery-1",
                new DiscordDelivery(
                    "delivery-1",
                    "business-1",
                    "alerts",
                    DiscordDeliveryStatus.Failed,
                    "private delivery body",
                    "discord_message:21",
                    null,
                    5,
                    FixedNow.AddMinutes(-1),
                    FixedNow));
            using var host = CreateHost("Owner", store);

            using var response = await PostJsonAsync(
                host.Client,
                "api/v1/integrations/discord/deliveries/delivery-1/retry",
                "{}");
            var json = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(json);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("RetryScheduled", (string?)payload["status"]);
            Assert.Equal(
                "private delivery body",
                store.Deliveries["delivery-1"].ContentText);
            Assert.DoesNotContain("private delivery body", json, StringComparison.Ordinal);
            Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("api/v1/integrations/discord/deliveries", "discord_deliveries_query_unavailable")]
        [InlineData("api/v1/integrations/discord/bindings", "discord_bindings_query_unavailable")]
        public async Task Unsupported_list_contracts_return_stable_service_unavailable(
            string path,
            string expectedCode)
        {
            using var host = CreateHost("Owner", ConfiguredStore());

            using var response = await host.Client.GetAsync(
                path,
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(expectedCode, (string?)problem["code"]);
        }

        [Fact]
        public async Task Unsupported_unbind_contract_returns_stable_service_unavailable()
        {
            using var host = CreateHost("Owner", ConfiguredStore());

            using var response = await host.Client.DeleteAsync(
                "api/v1/integrations/discord/bindings/discord-1",
                TestContext.Current.CancellationToken);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("discord_binding_delete_unavailable", (string?)problem["code"]);
        }

        [Fact]
        public async Task Binding_code_creation_returns_the_one_time_code_without_its_digest()
        {
            var store = ConfiguredStore();
            using var host = CreateHost("Owner", store);

            using var response = await PostJsonAsync(
                host.Client,
                "api/v1/integrations/discord/binding-codes",
                "{\"crossplatformId\":\"EOS_1\"}");
            var json = await response.Content.ReadAsStringAsync();
            var payload = JObject.Parse(json);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var code = Assert.IsType<string>((string?)payload["code"]);
            Assert.Equal(18, code.Length);
            Assert.Equal(code.Substring(0, 4), (string?)payload["codePrefix"]);
            var saved = Assert.Single(store.BindingCodes);
            Assert.Equal("EOS_1", saved.CrossplatformId);
            Assert.True(DiscordBindingCodeHash.Compute(code).SequenceEqual(saved.CodeHash));
            Assert.DoesNotContain("hash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("digest", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Commands_returns_only_the_closed_application_command_settings()
        {
            var store = ConfiguredStore();
            store.Commands.Add(new DiscordCommandSetting(
                DiscordSlashCommandNames.Status,
                true,
                true));
            store.Commands.Add(new DiscordCommandSetting(
                DiscordSlashCommandNames.Players,
                false,
                false));
            using var host = CreateHost("Owner", store);

            using var response = await host.Client.GetAsync(
                "api/v1/integrations/discord/commands",
                TestContext.Current.CancellationToken);
            var payload = JArray.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(new[] { "players", "status" }, payload.Select(item => (string)item["commandKey"]!).ToArray());
            Assert.All(payload, item => Assert.Null(item["payload"]));
        }

        [Fact]
        public async Task Anonymous_interaction_fails_closed_when_no_signature_port_exists()
        {
            var store = ConfiguredStore();
            using var host = CreateHost(null, store);

            using var response = await PostJsonAsync(
                host.Client,
                "api/v1/integrations/discord/interactions",
                "{\"interactionId\":\"untrusted\",\"commandName\":\"status\"}");
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(
                "discord_interaction_verification_unavailable",
                (string?)problem["code"]);
            Assert.Equal(0, store.RegisterInteractionCallCount);
        }

        [Fact]
        public async Task Anonymous_interaction_verifies_the_exact_raw_body_and_returns_ping()
        {
            var store = ConfiguredStore();
            using var host = CreateHost(
                null,
                store,
                InteractionVerifier());
            const string body = "{\"type\":1}";

            using var response = await SendInteractionAsync(
                host.Client,
                body,
                Sign(InteractionTimestamp, Encoding.UTF8.GetBytes(body)),
                InteractionTimestamp);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, (int?)payload["type"]);
            Assert.Equal(0, store.RegisterInteractionCallCount);
        }

        [Fact]
        public async Task Anonymous_interaction_returns_401_for_missing_malformed_or_tampered_signatures_before_parsing()
        {
            var store = ConfiguredStore();
            using var host = CreateHost(
                null,
                store,
                InteractionVerifier());
            const string body = "{\"type\":1}";
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var signature = Sign(InteractionTimestamp, bodyBytes);
            var cases = new[]
            {
                new { Body = body, Signature = (string?)null, Timestamp = (string?)InteractionTimestamp },
                new { Body = body, Signature = (string?)signature, Timestamp = (string?)null },
                new { Body = body, Signature = (string?)"not-hex", Timestamp = (string?)InteractionTimestamp },
                new { Body = body, Signature = (string?)signature.Substring(2), Timestamp = (string?)InteractionTimestamp },
                new { Body = "{\"type\": 1}", Signature = (string?)signature, Timestamp = (string?)InteractionTimestamp },
                new { Body = body, Signature = (string?)signature, Timestamp = (string?)"1785127201" },
                new { Body = "not-json", Signature = (string?)"00", Timestamp = (string?)InteractionTimestamp }
            };

            foreach (var item in cases)
            {
                using var response = await SendInteractionAsync(
                    host.Client,
                    item.Body,
                    item.Signature,
                    item.Timestamp);
                var json = await response.Content.ReadAsStringAsync();
                var problem = JObject.Parse(json);
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.Equal("discord_interaction_signature_invalid", (string?)problem["code"]);
                Assert.DoesNotContain(item.Body, json, StringComparison.Ordinal);
                Assert.DoesNotContain(InteractionPrivateKeyHex, json, StringComparison.Ordinal);
            }

            Assert.Equal(0, store.RegisterInteractionCallCount);
        }

        [Fact]
        public async Task Signed_application_command_is_accepted_and_persisted_for_deferred_processing()
        {
            var store = ConfiguredInboundStore();
            using var host = CreateHost(
                null,
                store,
                InteractionVerifier());
            const string body = "{\"id\":\"interaction-1\",\"type\":2,\"token\":\"interaction-token-1\",\"guild_id\":\"guild-1\",\"channel_id\":\"channel-1\",\"member\":{\"user\":{\"id\":\"discord-user-1\",\"bot\":false}},\"data\":{\"name\":\"status\"}}";

            using var response = await SendInteractionAsync(
                host.Client,
                body,
                Sign(InteractionTimestamp, Encoding.UTF8.GetBytes(body)),
                InteractionTimestamp);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(5, (int?)payload["type"]);
            var interaction = Assert.IsType<DiscordInteraction>(store.AcceptedInteraction);
            Assert.Equal("interaction-1", interaction.InteractionId);
            Assert.Equal("guild-1", interaction.GuildId);
            Assert.Equal("channel-1", interaction.ChannelId);
            Assert.Equal("discord-user-1", interaction.DiscordSubject);
            Assert.Equal("status", interaction.CommandKey);
            Assert.Equal("interaction-token-1", store.AcceptedInteractionToken);
        }

        [Fact]
        public async Task Signed_application_command_requires_a_top_level_interaction_token()
        {
            var store = ConfiguredInboundStore();
            using var host = CreateHost(
                null,
                store,
                InteractionVerifier());
            const string body = "{\"id\":\"interaction-1\",\"type\":2,\"guild_id\":\"guild-1\",\"channel_id\":\"channel-1\",\"member\":{\"user\":{\"id\":\"discord-user-1\",\"bot\":false}},\"data\":{\"name\":\"status\"}}";

            using var response = await SendInteractionAsync(
                host.Client,
                body,
                Sign(InteractionTimestamp, Encoding.UTF8.GetBytes(body)),
                InteractionTimestamp);
            var problem = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("discord_interaction_body_invalid", (string?)problem["code"]);
            Assert.Null(store.AcceptedInteraction);
        }

        private static void AssertRoute(
            Type controllerType,
            string methodName,
            string template,
            Type verbAttribute)
        {
            var method = controllerType.GetMethod(methodName);
            Assert.NotNull(method);
            Assert.Equal(template, method!.GetCustomAttribute<RouteAttribute>()?.Template);
            Assert.NotNull(method.GetCustomAttribute(verbAttribute));
        }

        private static MemoryDiscordStore ConfiguredStore()
        {
            var store = new MemoryDiscordStore
            {
                Settings = new DiscordIntegrationSettings(
                    7,
                    true,
                    DiscordIntegrationMode.Webhook,
                    "application-1",
                    "guild-1",
                    "channel-1",
                    true,
                    false,
                    false,
                    null,
                    FixedNow)
            };
            store.Targets.Add(new DiscordTarget("alerts", "Webhook", null, true));
            return store;
        }

        private static MemoryDiscordStore ConfiguredInboundStore()
        {
            var store = ConfiguredStore();
            store.Settings = store.Settings! with { Mode = DiscordIntegrationMode.Bot };
            store.Commands.Add(new DiscordCommandSetting("status", true, true));
            return store;
        }

        private static HttpTestHost CreateHost(
            string? role,
            MemoryDiscordStore store,
            IDiscordInteractionSignatureVerifier? signatureVerifier = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IDiscordIntegrationStore>(store);
            if (signatureVerifier != null)
                services.AddSingleton(signatureVerifier);
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

        private static Task<HttpResponseMessage> PutJsonAsync(
            HttpClient client,
            string path,
            string json) =>
            client.PutAsync(path, Json(json), TestContext.Current.CancellationToken);

        private static Task<HttpResponseMessage> PostJsonAsync(
            HttpClient client,
            string path,
            string json) =>
            client.PostAsync(path, Json(json), TestContext.Current.CancellationToken);

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static Task<HttpResponseMessage> SendInteractionAsync(
            HttpClient client,
            string body,
            string? signature,
            string? timestamp)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Post,
                "api/v1/integrations/discord/interactions")
            {
                Content = Json(body)
            };
            if (signature != null)
                request.Headers.TryAddWithoutValidation("X-Signature-Ed25519", signature);
            if (timestamp != null)
                request.Headers.TryAddWithoutValidation("X-Signature-Timestamp", timestamp);
            return client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        private static DiscordInteractionSignatureVerifier InteractionVerifier() =>
            new DiscordInteractionSignatureVerifier(
                InteractionPublicKeyHex,
                () => DateTimeOffset.FromUnixTimeSeconds(long.Parse(InteractionTimestamp)),
                TimeSpan.FromMinutes(5));

        private static string Sign(string timestamp, byte[] body)
        {
            var timestampBytes = Encoding.ASCII.GetBytes(timestamp);
            var message = new byte[timestampBytes.Length + body.Length];
            Buffer.BlockCopy(timestampBytes, 0, message, 0, timestampBytes.Length);
            Buffer.BlockCopy(body, 0, message, timestampBytes.Length, body.Length);
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(Hex(InteractionPrivateKeyHex)));
            signer.BlockUpdate(message, 0, message.Length);
            return string.Concat(signer.GenerateSignature().Select(item => item.ToString("x2")));
        }

        private static byte[] Hex(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static readonly DateTimeOffset FixedNow =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);
        private const string InteractionTimestamp = "1785127200";
        private const string InteractionPrivateKeyHex =
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
        private const string InteractionPublicKeyHex =
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";

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

        private sealed class MemoryDiscordStore :
            IDiscordIntegrationStore,
            IDiscordInteractionPersistenceStore,
            IDiscordIntegrationHealthSource
        {
            private readonly Dictionary<string, DiscordSecretValue> secrets =
                new Dictionary<string, DiscordSecretValue>(StringComparer.Ordinal);

            public DiscordIntegrationSettings? Settings { get; set; }
            public List<DiscordTarget> Targets { get; } = new List<DiscordTarget>();
            public List<DiscordCommandSetting> Commands { get; } =
                new List<DiscordCommandSetting>();
            public Dictionary<string, DiscordDelivery> Deliveries { get; } =
                new Dictionary<string, DiscordDelivery>(StringComparer.Ordinal);
            public List<DiscordBindingCode> BindingCodes { get; } =
                new List<DiscordBindingCode>();
            public int RegisterInteractionCallCount { get; private set; }
            public DiscordInteraction? AcceptedInteraction { get; private set; }
            public string? AcceptedInteractionToken { get; private set; }
            public bool ThrowOnHealthRead { get; set; }
            public DiscordHealthSnapshot Health { get; set; } = new DiscordHealthSnapshot(
                new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_gateway_not_started",
                    null),
                new DiscordHealthSection(
                    DiscordHealthState.Unavailable,
                    "discord_inbound_runtime_not_running",
                    null));

            public DiscordHealthSnapshot GetHealth() => ThrowOnHealthRead
                ? throw new InvalidOperationException("health unavailable")
                : Health;

            public DiscordIntegrationSettings? GetSettings() => Settings;

            public void SaveSettings(DiscordIntegrationSettings settings, long expectedVersion)
            {
                if ((Settings?.Version ?? 0) != expectedVersion)
                    throw new DiscordIntegrationVersionConflictException();
                Settings = settings;
            }

            public void SetSecret(DiscordSecretValue secret) => secrets[secret.SecretKey] = secret;
            public void DeleteSecret(string secretKey) => secrets.Remove(secretKey);

            public DiscordSecretValue? GetSecret(string secretKey) =>
                secrets.TryGetValue(secretKey, out var value) ? value : null;

            public IReadOnlyList<DiscordSecretMetadata> ListSecretMetadata() =>
                secrets.Values
                    .Select(value => new DiscordSecretMetadata(
                        value.SecretKey,
                        value.Fingerprint,
                        value.UpdatedAtUtc))
                    .ToArray();

            public void SaveTarget(DiscordTarget target)
            {
                Targets.RemoveAll(existing => string.Equals(
                    existing.TargetKey,
                    target.TargetKey,
                    StringComparison.Ordinal));
                Targets.Add(target);
            }

            public IReadOnlyList<DiscordTarget> ListTargets() => Targets.ToArray();

            public DiscordTarget? FindTarget(string targetKey) =>
                Targets.SingleOrDefault(target => string.Equals(
                    target.TargetKey,
                    targetKey,
                    StringComparison.Ordinal));

            public void SaveCommandSetting(DiscordCommandSetting command)
            {
                Commands.RemoveAll(existing => string.Equals(
                    existing.CommandKey,
                    command.CommandKey,
                    StringComparison.Ordinal));
                Commands.Add(command);
            }

            public IReadOnlyList<DiscordCommandSetting> ListCommandSettings() =>
                Commands.OrderBy(command => command.CommandKey, StringComparer.Ordinal).ToArray();

            public DiscordDeliveryEnqueueResult EnqueueDelivery(DiscordDelivery delivery)
            {
                if (Deliveries.ContainsKey(delivery.DeliveryId))
                    return new DiscordDeliveryEnqueueResult(Deliveries[delivery.DeliveryId], false);
                Deliveries.Add(delivery.DeliveryId, delivery);
                return new DiscordDeliveryEnqueueResult(delivery, true);
            }

            public void BeginDeliveryAttempt(
                string deliveryId,
                int attemptNumber,
                DateTimeOffset startedAtUtc) => throw new NotSupportedException();

            public DiscordDeliveryWorkItem? TryClaimNextDeliveryAttempt(DateTimeOffset claimedAtUtc) =>
                throw new NotSupportedException();

            public void CompleteDeliveryAttempt(
                string deliveryId,
                int attemptNumber,
                DiscordDeliveryStatus finalStatus,
                DateTimeOffset completedAtUtc,
                string? errorCode,
                DateTimeOffset? nextAttemptAtUtc) => throw new NotSupportedException();

            public int RecoverSendingAsResultUnknown(DateTimeOffset recoveredAtUtc) =>
                throw new NotSupportedException();

            public DiscordDelivery? FindDelivery(string deliveryId) =>
                Deliveries.TryGetValue(deliveryId, out var delivery) ? delivery : null;

            public IReadOnlyList<DiscordDeliveryAttempt> ListDeliveryAttempts(string deliveryId) =>
                Array.Empty<DiscordDeliveryAttempt>();

            public DiscordDelivery ScheduleManualRetry(
                string deliveryId,
                string contentText,
                DateTimeOffset scheduledAtUtc)
            {
                if (Settings?.IsEnabled != true)
                    throw new DiscordIntegrationDisabledException();
                if (!Deliveries.TryGetValue(deliveryId, out var delivery) ||
                    (delivery.Status != DiscordDeliveryStatus.Failed &&
                     delivery.Status != DiscordDeliveryStatus.ResultUnknown &&
                     delivery.Status != DiscordDeliveryStatus.Cancelled))
                    throw new InvalidOperationException("discord_delivery_not_retryable");
                var retried = delivery with
                {
                    Status = DiscordDeliveryStatus.RetryScheduled,
                    ContentText = contentText,
                    NextAttemptAtUtc = scheduledAtUtc,
                    RetryCount = 0,
                    CompletedAtUtc = null
                };
                Deliveries[deliveryId] = retried;
                return retried;
            }

            public bool CancelDelivery(string deliveryId, DateTimeOffset cancelledAtUtc) =>
                throw new NotSupportedException();

            public void SaveBindingCode(DiscordBindingCode code) => BindingCodes.Add(code);

            public DiscordBinding? TryConsumeBindingCode(
                byte[] codeHash,
                string discordSubject,
                DateTimeOffset consumedAtUtc) => throw new NotSupportedException();

            public DiscordBinding? FindBinding(string discordSubject) => null;

            public bool TryRegisterInteraction(DiscordInteraction interaction)
            {
                RegisterInteractionCallCount++;
                return true;
            }

            public void CompleteInteraction(
                string interactionId,
                string status,
                DateTimeOffset completedAtUtc) => throw new NotSupportedException();

            public void SaveInteractionWithToken(
                DiscordInteraction interaction,
                string tokenValue) => throw new NotSupportedException();

            public bool TrySaveInteractionWithToken(
                DiscordInteraction interaction,
                string tokenValue)
            {
                if (AcceptedInteraction != null) return false;
                AcceptedInteraction = interaction;
                AcceptedInteractionToken = tokenValue;
                return true;
            }

            public DiscordInteraction? TryClaimNextInteraction(DateTimeOffset claimedAtUtc) =>
                throw new NotSupportedException();

            public int RecoverRunningInteractions(DateTimeOffset recoveredAtUtc) =>
                throw new NotSupportedException();

            public DiscordInteractionToken? GetInteractionToken(
                string interactionId,
                DateTimeOffset observedAtUtc) => throw new NotSupportedException();

            public int ClearExpiredInteractionTokens(DateTimeOffset observedAtUtc) =>
                throw new NotSupportedException();

            public bool TryRegisterBridgeMessage(
                string bridgeMessageId,
                string source,
                string sourceMessageId,
                DateTimeOffset expiresAtUtc) => throw new NotSupportedException();
        }
    }
}
