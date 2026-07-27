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
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class ChatMuteHttpAuthorizationTests
    {
        public static TheoryData<string, string, string?, HttpStatusCode> MuteAuthorizationMatrix => new()
        {
            { "GET", "api/v1/chat/mutes", null, HttpStatusCode.Unauthorized },
            { "GET", "api/v1/chat/mutes", "Admin", HttpStatusCode.Forbidden },
            { "GET", "api/v1/chat/mutes", "Owner", HttpStatusCode.OK },
            { "POST", "api/v1/chat/mutes", null, HttpStatusCode.Unauthorized },
            { "POST", "api/v1/chat/mutes", "Admin", HttpStatusCode.Forbidden },
            { "POST", "api/v1/chat/mutes", "Owner", HttpStatusCode.Created },
            { "PUT", "api/v1/chat/mutes/EOS_1", null, HttpStatusCode.Unauthorized },
            { "PUT", "api/v1/chat/mutes/EOS_1", "Admin", HttpStatusCode.Forbidden },
            { "PUT", "api/v1/chat/mutes/EOS_1", "Owner", HttpStatusCode.OK },
            { "DELETE", "api/v1/chat/mutes/EOS_1", null, HttpStatusCode.Unauthorized },
            { "DELETE", "api/v1/chat/mutes/EOS_1", "Admin", HttpStatusCode.Forbidden },
            { "DELETE", "api/v1/chat/mutes/EOS_1", "Owner", HttpStatusCode.NoContent }
        };

        [Theory]
        [MemberData(nameof(MuteAuthorizationMatrix))]
        public async Task All_mute_routes_enforce_the_401_403_owner_matrix_over_the_real_http_pipeline(
            string method,
            string path,
            string? role,
            HttpStatusCode expectedStatus)
        {
            using var host = CreateHost(role);
            using var request = CreateRequest(method, path);

            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(expectedStatus, response.StatusCode);
        }

        private static HttpRequestMessage CreateRequest(string method, string path)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "POST")
            {
                request.Content = Json("{\"crossplatformId\":\"EOS_2\",\"reason\":\"spam\"}");
            }
            else if (method == "PUT")
            {
                request.Content = Json("{\"reason\":\"updated reason\"}");
            }

            return request;
        }

        private static StringContent Json(string value) =>
            new StringContent(value, Encoding.UTF8, "application/json");

        private static HttpTestHost CreateHost(string? role)
        {
            var ports = new TestChatPorts();
            var services = new ServiceCollection();
            services.AddSingleton<IRecentChatMessageQuery>(ports);
            services.AddSingleton<IPanelRuntimeStatus>(ports);
            services.AddSingleton<IChatHistoryStore>(ports);
            services.AddSingleton<IChatSettingsStore>(ports);
            services.AddSingleton<IColoredChatStore>(ports);
            services.AddSingleton<IChatMessageSender>(ports);
            services.AddSingleton<IChatOperationAuditTrail>(ports);
            services.AddSingleton<IChatRuntimeConfiguration>(ports);
            services.AddSingleton<IChatMuteStore>(ports);
            services.AddSingleton<IChatMuteRuntimeConfiguration>(ports);
            services.AddTransient<GetChatHistoryUseCase>();
            services.AddTransient<GetChatSettingsUseCase>();
            services.AddTransient<SaveChatSettingsUseCase>();
            services.AddTransient<ResetChatSettingsUseCase>();
            services.AddTransient<GetColoredChatSettingsUseCase>();
            services.AddTransient<SaveColoredChatSettingsUseCase>();
            services.AddTransient<ResetColoredChatSettingsUseCase>();
            services.AddTransient<GetColoredChatProfilesUseCase>();
            services.AddTransient<CreateColoredChatProfileUseCase>();
            services.AddTransient<UpdateColoredChatProfileUseCase>();
            services.AddTransient<DeleteColoredChatProfileUseCase>();
            services.AddTransient<SendGlobalChatMessageUseCase>();
            services.AddTransient<SendPrivateChatMessageUseCase>();
            services.AddTransient(_ => new ChatMuteUseCases(
                ports,
                ports,
                () => new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero)));
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

        private sealed class TestChatPorts :
            IRecentChatMessageQuery,
            IPanelRuntimeStatus,
            IChatHistoryStore,
            IChatSettingsStore,
            IColoredChatStore,
            IChatMessageSender,
            IChatOperationAuditTrail,
            IChatMuteRuntimeConfiguration,
            IChatMuteStore
        {
            private static readonly DateTimeOffset Now =
                new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);
            private readonly Dictionary<string, ChatMuteRecord> mutes =
                new Dictionary<string, ChatMuteRecord>(StringComparer.Ordinal)
                {
                    ["EOS_1"] = new ChatMuteRecord(
                        "EOS_1", "Player", "spam", null,
                        "subject-1", Now, "subject-1", Now)
                };

            public ModHostState State => default;
            public GameReadinessState GameReadiness => default;

            public IReadOnlyList<ChatMessageEventData> ReadRecentChatMessages(int limit) =>
                Array.Empty<ChatMessageEventData>();

            public void Append(ChatMessage message) { }
            public void AppendGap(ChatHistoryGap gap) { }
            public ChatHistoryPage GetHistory(ChatHistoryQuery query) =>
                new ChatHistoryPage(Array.Empty<ChatMessage>(), null, Array.Empty<ChatHistoryGap>());
            public int DeleteBefore(DateTimeOffset cutoffUtc, int maximumDeletes) => 0;

            public ChatSettings Get() => Settings();
            public ChatSettings Save(ChatSettings settings) => settings;
            public ChatSettings Reset() => Settings();

            public ColoredChatSettings GetSettings() => ColoredSettings();
            public ColoredChatSettings SaveSettings(ColoredChatSettings settings) => settings;
            public ColoredChatSettings ResetSettings() => ColoredSettings();
            public ColoredChatProfilePage GetProfiles(ColoredChatProfileQuery query) =>
                new ColoredChatProfilePage(Array.Empty<ColoredChatProfile>(), null);
            public IReadOnlyList<ColoredChatProfile> GetAllProfiles() =>
                Array.Empty<ColoredChatProfile>();
            public bool TryCreateProfile(ColoredChatProfile profile) => true;
            public bool TryUpdateProfile(ColoredChatProfile profile) => true;
            public bool TryDeleteProfile(string crossplatformId) => true;

            public Task<ChatSendResult> SendGlobalAsync(
                string message,
                CancellationToken cancellationToken) => Task.FromResult(ChatSendResult.Accepted());
            public Task<ChatSendResult> SendPrivateAsync(
                string targetCrossplatformId,
                string message,
                CancellationToken cancellationToken) => Task.FromResult(ChatSendResult.Accepted());

            public void Record(ChatOperationAuditEntry entry) { }
            public void ApplyChatSettings(ChatSettings settings) { }
            public void ApplyColoredChatSettings(ColoredChatSettings settings) { }
            public void UpsertProfile(ColoredChatProfile profile) { }
            public void RemoveProfile(string crossplatformId) { }
            public void ReplaceMuteSnapshot(IReadOnlyDictionary<string, ChatMuteRecord> snapshot) { }

            public ChatMutePage GetPage(int pageSize, ChatMuteCursor? cursor) =>
                new ChatMutePage(mutes.Values, null);

            public ChatMuteRecord? Find(string crossplatformId) =>
                mutes.TryGetValue(crossplatformId, out var record) ? record : null;

            public IReadOnlyList<ChatMuteRecord> Create(
                ChatMuteRecord record,
                ChatMuteOperation operation)
            {
                mutes[record.CrossplatformId] = record;
                return Snapshot();
            }

            public IReadOnlyList<ChatMuteRecord> Update(
                ChatMuteRecord record,
                ChatMuteOperation operation)
            {
                mutes[record.CrossplatformId] = record;
                return Snapshot();
            }

            public IReadOnlyList<ChatMuteRecord> Release(
                string crossplatformId,
                ChatMuteOperation operation)
            {
                mutes.Remove(crossplatformId);
                return Snapshot();
            }

            private IReadOnlyList<ChatMuteRecord> Snapshot() => mutes.Values.ToArray();

            private static ChatSettings Settings() => new ChatSettings
            {
                IsEnabled = true,
                CommandPrefixes = new[] { "/" },
                ExcludeCommandsFromHistory = true,
                HistoryRetentionDays = 30
            };

            private static ColoredChatSettings ColoredSettings() => new ColoredChatSettings
            {
                IsEnabled = true,
                PlayerColorTagPermission = PlayerColorTagPermission.None
            };
        }
    }
}
