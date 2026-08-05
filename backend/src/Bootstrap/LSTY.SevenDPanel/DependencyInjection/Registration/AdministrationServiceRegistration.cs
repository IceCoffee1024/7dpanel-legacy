using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Adapters.Local.GeoIp;
using LSTY.SevenDPanel.Adapters.Local.Platform;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.AccessPolicies;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.GameEvents;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.AccessLists;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GamePermissions;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Mods;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Chat;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Discord;
using LSTY.SevenDPanel.Application.GameEvents;
using LSTY.SevenDPanel.Application.GeoIp;
using LSTY.SevenDPanel.Application.Mods;
using LSTY.SevenDPanel.Application.ServerConfiguration;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using LSTY.SevenDPanel.Mods;
using LSTY.SevenDPanel.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class AdministrationServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var assetRoot = context.AssetRoot;
            var log = context.Log;

            services.AddSingleton<SqliteDiscordIntegrationStore>();
            services.AddSingleton<IDiscordIntegrationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteDiscordIntegrationStore>());
            services.AddSingleton<IDiscordInteractionPersistenceStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteDiscordIntegrationStore>());
            services.AddSingleton<IDiscordInteractionSignatureVerifier>(serviceProvider =>
                new DiscordInteractionSignatureVerifier(
                    () => serviceProvider
                        .GetRequiredService<IDiscordIntegrationStore>()
                        .GetSecret("interactionPublicKey")?.SecretValue,
                    () => DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(5)));
            services.AddSingleton<GetDiscordConfigurationUseCase>();
            services.AddSingleton(serviceProvider => new SaveDiscordConfigurationUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new SetDiscordSecretUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new EnqueueDiscordDeliveryUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow,
                () => "discord-delivery-" + Guid.NewGuid().ToString("N")));
            services.AddSingleton(serviceProvider => new RetryDiscordDeliveryUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new CancelDiscordDeliveryUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton<DiscordApiClient>();
            services.AddSingleton<IDiscordApiClient>(serviceProvider =>
                serviceProvider.GetRequiredService<DiscordApiClient>());
            services.AddSingleton(serviceProvider => new DiscordDeliveryWorker(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                serviceProvider.GetRequiredService<IDiscordApiClient>(),
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(1),
                log));
            services.AddSingleton<DiscordInboundCommandDispatcher>();
            services.AddSingleton<IDiscordInboundCommandDispatcher>(serviceProvider =>
                serviceProvider.GetRequiredService<DiscordInboundCommandDispatcher>());
            services.AddSingleton<DiscordInteractionFollowupClient>();
            services.AddSingleton<IDiscordInteractionResponseSender>(serviceProvider =>
                serviceProvider.GetRequiredService<DiscordInteractionFollowupClient>());
            services.AddSingleton(serviceProvider => new BridgeDiscordMessageToGameUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                serviceProvider.GetRequiredService<IChatMessageSender>(),
                () => DateTimeOffset.UtcNow,
                () => "discord-bridge-" + Guid.NewGuid().ToString("N")));
            services.AddSingleton(serviceProvider => new BridgeGameChatToDiscordUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                () => DateTimeOffset.UtcNow,
                () => "discord-bridge-" + Guid.NewGuid().ToString("N"),
                () => "discord-delivery-" + Guid.NewGuid().ToString("N"),
                "public"));
            services.AddSingleton(serviceProvider => new HandleDiscordInteractionUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                serviceProvider.GetRequiredService<IDiscordInboundCommandDispatcher>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new AcceptDiscordInteractionUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                serviceProvider.GetRequiredService<IDiscordInteractionPersistenceStore>(),
                () => DateTimeOffset.UtcNow));
            services.AddSingleton(serviceProvider => new ProcessDiscordInteractionUseCase(
                serviceProvider.GetRequiredService<IDiscordIntegrationStore>(),
                serviceProvider.GetRequiredService<IDiscordInteractionPersistenceStore>(),
                serviceProvider.GetRequiredService<IDiscordInboundCommandDispatcher>(),
                () => DateTimeOffset.UtcNow,
                serviceProvider.GetRequiredService<IDiscordInteractionResponseSender>()));
            services.AddSingleton<DiscordInboundRuntime>();
            services.AddSingleton<IDiscordInboundTransportSink>(serviceProvider =>
                serviceProvider.GetRequiredService<DiscordInboundRuntime>());
            services.AddSingleton<IDiscordDeferredInteractionSink>(serviceProvider =>
                serviceProvider.GetRequiredService<DiscordInboundRuntime>());

            services.AddSingleton<SqliteGeoIpAccessPolicyStore>();
            services.AddSingleton<IGeoIpAccessPolicyStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteGeoIpAccessPolicyStore>());
            services.AddSingleton(serviceProvider =>
                new LocalMmdbGeoIpProvider(options.GeoIpDatabasePath));
            services.AddSingleton<MaxMindWebServiceGeoIpProvider>();
            services.AddSingleton<IGeoIpProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<LocalMmdbGeoIpProvider>());
            services.AddSingleton<IGeoIpProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<MaxMindWebServiceGeoIpProvider>());
            services.AddSingleton(serviceProvider => new GeoIpRefreshWorker(
                serviceProvider.GetRequiredService<IGeoIpAccessPolicyStore>(),
                serviceProvider.GetServices<IGeoIpProvider>()));
            services.AddSingleton<IGeoIpRefreshQueue>(serviceProvider =>
                serviceProvider.GetRequiredService<GeoIpRefreshWorker>());
            services.AddSingleton<IGeoIpRefreshDiagnostics>(serviceProvider =>
                serviceProvider.GetRequiredService<GeoIpRefreshWorker>());
            services.AddSingleton<GeoIpPolicyEvaluator>();
            services.AddSingleton<EvaluateGeoIpJoinUseCase>();
            services.AddSingleton<GetGeoIpDiagnosticsUseCase>();
            services.AddSingleton<SevenDaysGeoIpJoinPolicyRuntime>();

            services.AddSingleton<SqliteAuthenticationStore>();
            services.AddSingleton<IPanelCredentialStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
            services.AddSingleton<IPanelAccessTokenStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
            services.AddSingleton<IPanelApiKeyStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
            services.AddSingleton<IPanelUserAdministrationStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
            services.AddSingleton<SqliteRecentActivityStore>(serviceProvider =>
                new SqliteRecentActivityStore(
                    serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                    retentionLimit: 256));
            services.AddSingleton<IRecentActivityQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
            services.AddSingleton<IRecentActivityWriter>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
            services.AddSingleton(ServerConfigurationFieldCatalog.Create());
            services.AddSingleton<IServerConfigurationStore>(_ =>
                new LocalServerConfigurationStore(options.ServerConfigurationPath));
            services.AddSingleton<GetServerConfigurationUseCase>();
            services.AddSingleton<UpdateServerConfigurationUseCase>();
            services.AddSingleton<SevenDaysPlayerAccessControl>();
            services.AddSingleton<IPlayerAccessControl>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysPlayerAccessControl>());
            services.AddSingleton<AccessListUseCases>();
            services.AddSingleton<SevenDaysGamePermissionControl>();
            services.AddSingleton<IGamePermissionControl>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysGamePermissionControl>());
            services.AddSingleton<GamePermissionUseCases>();
            services.AddSingleton<LocalModCatalog>(_ => new LocalModCatalog(
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..")),
                new[] { Path.GetFileName(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) }));
            services.AddSingleton<IModCatalog>(serviceProvider =>
                serviceProvider.GetRequiredService<LocalModCatalog>());
            services.AddSingleton<SevenDaysLoadedModQuery>();
            services.AddSingleton<ILoadedModQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysLoadedModQuery>());
            services.AddSingleton<ListModsUseCase>();
            services.AddSingleton<SetModStateUseCase>();
            OwinStartup.RegisterAuthenticationServices(services, log);
            services.AddSingleton(serviceProvider =>
                new SevenDaysRecentActivityRecorder(
                    serviceProvider.GetRequiredService<IRecentActivityWriter>(),
                    log));

            services.AddSingleton(serviceProvider => new HostOverviewQuery(
                serviceProvider.GetRequiredService<IHostPlatformAdapter>(),
                serviceProvider.GetRequiredService<HostCpuSampler>(),
                serviceProvider.GetRequiredService<HostMemorySampler>(),
                serviceProvider.GetRequiredService<HostStorageSampler>(),
                serviceProvider.GetRequiredService<DeviceIdentityProvider>(),
                serviceProvider.GetRequiredService<PublicNetworkAddressResolver>()));
            services.AddSingleton<IHostOverviewQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<HostOverviewQuery>());
            services.AddSingleton<SevenDaysGameOverviewQuery>();
            services.AddSingleton<IGameOverviewQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysGameOverviewQuery>());
            services.AddSingleton<IRestartPolicyQuery, UnavailableRestartPolicyQuery>();
            services.AddSingleton<GetOverviewUseCase>();

            services.AddSingleton(_ => new ConsoleLogService(log));
            services.AddSingleton<IRecentConsoleLogQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<ConsoleLogService>().LiveWindow);
            services.AddSingleton<IRecentChatMessageQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<ConsoleLogService>().LiveWindow);
            services.AddSingleton<IServerEventStream>(serviceProvider =>
                serviceProvider.GetRequiredService<ConsoleLogService>().Stream);
            services.AddSingleton<SqliteConsoleCommandAuditStore>();
            services.AddSingleton<IConsoleCommandAuditStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteConsoleCommandAuditStore>());
            services.AddSingleton(serviceProvider => new ConsoleCommandAuditService(
                serviceProvider.GetRequiredService<IConsoleCommandAuditStore>(),
                log));
            services.AddSingleton<SevenDaysConsoleCommandService>();
            services.AddSingleton(_ => new SevenDaysConsoleCommandCatalogQuery(log));
            services.AddSingleton<IConsoleCommandCatalogQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysConsoleCommandCatalogQuery>());
            services.AddSingleton<IConsoleCommandGateway>(serviceProvider =>
                serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>());
            services.AddSingleton<ExecuteConsoleCommandUseCase>();
            services.AddSingleton<SqliteGameEventStore>();
            services.AddSingleton<IGameEventStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteGameEventStore>());
            services.AddSingleton<GameEventWriteService>();
            services.AddSingleton<SevenDaysGameEventAdapter>();
            services.AddSingleton<SqliteUnifiedAuditQuery>();
            services.AddSingleton<IUnifiedAuditQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteUnifiedAuditQuery>());

            services.AddSingleton(serviceProvider => new ModHost(
                () =>
                {
                    var authenticationStore = serviceProvider
                        .GetRequiredService<SqliteAuthenticationStore>();
                    var playerActionAuditTrail = serviceProvider
                        .GetRequiredService<SqlitePlayerActionAuditTrail>();
                    playerActionAuditTrail.MarkPendingUnknown(DateTimeOffset.UtcNow);
                    if (options.Authentication.Enabled)
                    {
                        authenticationStore.EnsureBootstrapOwner(
                            options.Authentication.Username,
                            options.Authentication.Password);
                    }

                    return new OwinWebHost(
                        options.Url,
                        app => OwinStartup.Configure(
                            app,
                            serviceProvider,
                            assetRoot,
                            log));
                },
                log));
            services.AddSingleton(serviceProvider => new ConsoleLogRuntime(
                serviceProvider.GetRequiredService<ConsoleLogService>(),
                serviceProvider.GetRequiredService<ModHost>()));
            services.AddSingleton(serviceProvider => new ConsoleCommandRuntime(
                serviceProvider.GetRequiredService<ConsoleCommandAuditService>(),
                serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>(),
                serviceProvider.GetRequiredService<ConsoleLogRuntime>()));
            services.AddSingleton(serviceProvider => new SevenDaysRecentActivityRuntime(
                serviceProvider.GetRequiredService<SevenDaysRecentActivityRecorder>(),
                serviceProvider.GetRequiredService<PlayerHistoryRuntime>()));
            services.AddSingleton(serviceProvider => new SevenDaysGameEventRuntime(
                serviceProvider.GetRequiredService<GameEventWriteService>(),
                serviceProvider.GetRequiredService<SevenDaysGameEventAdapter>(),
                serviceProvider.GetRequiredService<SevenDaysChatRuntime>()));
            services.AddSingleton(serviceProvider => new DiscordRuntime(
                serviceProvider.GetRequiredService<DiscordDeliveryWorker>(),
                serviceProvider.GetRequiredService<DiscordInboundRuntime>(),
                () => CreateDiscordGateway(serviceProvider, log),
                serviceProvider.GetRequiredService<AutomationRuntime>(),
                TimeSpan.FromSeconds(30)));
            services.AddSingleton(serviceProvider => new GeoIpRuntime(
                serviceProvider.GetRequiredService<GeoIpRefreshWorker>(),
                serviceProvider.GetRequiredService<SevenDaysGeoIpJoinPolicyRuntime>(),
                serviceProvider.GetRequiredService<DiscordRuntime>()));
            services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>
                serviceProvider.GetRequiredService<ModHost>());
            services.AddSingleton<IModRuntime>(serviceProvider =>
                serviceProvider.GetRequiredService<ServerOperationRecoveryRuntime>());
        }

        private static DiscordGatewayClient? CreateDiscordGateway(
            IServiceProvider serviceProvider,
            Action<string> log)
        {
            var store = serviceProvider.GetRequiredService<IDiscordIntegrationStore>();
            var settings = store.GetSettings();
            if (settings == null || !settings.IsEnabled ||
                settings.Mode != DiscordIntegrationMode.Bot ||
                !settings.BridgeDiscordToGame)
            {
                return null;
            }

            var token = store.GetSecret(DiscordSecretKeys.BotToken)?.SecretValue;
            if (string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(settings.GuildId) ||
                string.IsNullOrWhiteSpace(settings.PublicChannelId))
            {
                log("Discord gateway is disabled because its bot configuration is incomplete.");
                return null;
            }

            return new DiscordGatewayClient(
                new DiscordGatewayOptions(
                    token!,
                    settings.GuildId!,
                    new[] { settings.PublicChannelId! }),
                serviceProvider.GetRequiredService<IDiscordInboundTransportSink>());
        }

        private sealed class UnavailableRestartPolicyQuery : IRestartPolicyQuery
        {
            public RestartPolicySummary Query() => RestartPolicySummary.Unavailable();
        }
    }
}
