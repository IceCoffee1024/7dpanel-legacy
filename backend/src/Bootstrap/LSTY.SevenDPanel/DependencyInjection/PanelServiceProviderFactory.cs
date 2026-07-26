using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.MapTiles;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ServerOperations;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using LSTY.SevenDPanel.Hosting.Platform;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal static class PanelServiceProviderFactory
    {
        public static ServiceProviderRuntime CreateRuntime(
            PanelHostOptions options,
            string dataDirectory,
            string? assetRoot,
            Action<string> log)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(dataDirectory))
                throw new ArgumentException("The panel data directory is required.", nameof(dataDirectory));
            if (log == null) throw new ArgumentNullException(nameof(log));

            ServiceProvider? provider = null;
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(options);
                services.AddSingleton(options.Authentication);
                services.AddSingleton(options.Overview);
                services.AddSingleton(options.Restart);
                services.AddSingleton(_ => new SqliteConnectionFactory(
                    Path.Combine(dataDirectory, "7dpanel.db")));
                services.AddSingleton(serviceProvider => new SqliteDatabaseBootstrapper(
                    serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                    log));
                services.AddSingleton<SqliteAuthenticationStore>();
                services.AddSingleton<IPanelCredentialStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<IPanelAccessTokenStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<IPanelApiKeyStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteAuthenticationStore>());
                services.AddSingleton<SqliteRecentActivityStore>(serviceProvider =>
                    new SqliteRecentActivityStore(
                        serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                        retentionLimit: 256));
                services.AddSingleton<IRecentActivityQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
                services.AddSingleton<IRecentActivityWriter>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteRecentActivityStore>());
                OwinStartup.RegisterAuthenticationServices(services, log);
                services.AddSingleton(serviceProvider =>
                    new SevenDaysRecentActivityRecorder(
                        serviceProvider.GetRequiredService<IRecentActivityWriter>(),
                        log));
                services.AddSingleton<WindowsHostPlatformAdapter>();
                services.AddSingleton<LinuxHostPlatformAdapter>();
                services.AddSingleton<IHostPlatformAdapter>(serviceProvider =>
                    Environment.OSVersion.Platform == PlatformID.Win32NT
                        ? (IHostPlatformAdapter)serviceProvider.GetRequiredService<WindowsHostPlatformAdapter>()
                        : serviceProvider.GetRequiredService<LinuxHostPlatformAdapter>());
                services.AddSingleton<HostCpuSampler>();
                services.AddSingleton<HostMemorySampler>();
                services.AddSingleton(_ => new HostStorageSampler(dataDirectory));
                services.AddSingleton(_ => new DeviceIdentityProvider("LSTY.SevenDPanel"));
                services.AddSingleton(serviceProvider => new PublicNetworkAddressResolver(
                    serviceProvider.GetRequiredService<PanelOverviewOptions>()));
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
                services.AddSingleton<SqliteServerOperationAuditTrail>();
                services.AddSingleton<IServerOperationAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteServerOperationAuditTrail>());
                services.AddSingleton<RestartScriptLauncher>();
                services.AddSingleton<IRestartScriptLauncher>(serviceProvider =>
                    serviceProvider.GetRequiredService<RestartScriptLauncher>());
                services.AddSingleton<SevenDaysShutdownServerGateway>();
                services.AddSingleton<IShutdownServerGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysShutdownServerGateway>());
                services.AddSingleton<RestartServerUseCase>();
                services.AddSingleton<ShutdownServerUseCase>();
                services.AddSingleton(_ => new ConsoleLogService(log));
                services.AddSingleton<IRecentConsoleLogQuery>(serviceProvider =>
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
                services.AddSingleton<SqlitePlayerHistoryStore>();
                services.AddSingleton<IPlayerHistoryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerHistoryStore>());
                services.AddSingleton<IPlayerMapSpatialQueryStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerHistoryStore>());
                services.AddSingleton<PlayerHistoryWriteService>();
                services.AddSingleton<GetHistoricalPlayersUseCase>();
                services.AddSingleton<GetHistoricalPlayerUseCase>();
                services.AddSingleton<GetPlayerHistorySnapshotsUseCase>();
                services.AddSingleton<GetPlayerTrackUseCase>();
                services.AddSingleton<SevenDaysMapMetadataProjection>();
                services.AddSingleton<SevenDaysMapGameTimeProjection>();
                services.AddSingleton<SevenDaysMapLayerProjection>();
                services.AddSingleton<SevenDaysTransientEntityProjection>();
                services.AddSingleton<IMapMetadataQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapMetadataProjection>());
                services.AddSingleton<IMapGameTimeQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapGameTimeProjection>());
                services.AddSingleton<IMapLayerProjection>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapLayerProjection>());
                services.AddSingleton<ITransientEntityMapProjection>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysTransientEntityProjection>());
                services.AddSingleton<GetMapMetadataUseCase>();
                services.AddSingleton<GetMapGameTimeUseCase>();
                services.AddSingleton<GetMapLayerUseCase>();
                services.AddSingleton<SearchPlayersInAreaUseCase>();
                services.AddSingleton<GetTransientEntityMapLayerUseCase>();
                services.AddSingleton<IMapTileStore>(_ => new LocalMapTileStore(() => null));
                services.AddSingleton<GetMapTileUseCase>();
                services.AddSingleton(serviceProvider => new SevenDaysOnlinePlayerProjection(
                    serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                    log));
                services.AddSingleton<IOnlinePlayerQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>());
                services.AddSingleton<GetOnlinePlayersUseCase>();
                services.AddSingleton<GetHistoricalPlayerLastLocationsUseCase>();
                services.AddSingleton<SqlitePlayerActionAuditTrail>();
                services.AddSingleton<IPlayerActionAuditTrail>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqlitePlayerActionAuditTrail>());
                services.AddSingleton<SevenDaysPlayerActions>();
                services.AddSingleton<IPlayerActions>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysPlayerActions>());
                services.AddSingleton<KickPlayerUseCase>();
                services.AddScoped<ServerEventSseSession>();
                services.AddSingleton(serviceProvider => new ModHost(
                    () =>
                    {
                        var databaseBootstrapper = serviceProvider
                            .GetRequiredService<SqliteDatabaseBootstrapper>();
                        var authenticationStore = serviceProvider
                            .GetRequiredService<SqliteAuthenticationStore>();
                        var playerActionAuditTrail = serviceProvider
                            .GetRequiredService<SqlitePlayerActionAuditTrail>();
                        databaseBootstrapper.Upgrade();
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
                services.AddSingleton(serviceProvider => new OnlinePlayerProjectionRuntime(
                    serviceProvider.GetRequiredService<SevenDaysOnlinePlayerProjection>(),
                    serviceProvider.GetRequiredService<ConsoleCommandRuntime>()));
                services.AddSingleton(serviceProvider => new PlayerHistoryRuntime(
                    serviceProvider.GetRequiredService<PlayerHistoryWriteService>(),
                    serviceProvider.GetRequiredService<OnlinePlayerProjectionRuntime>()));
                services.AddSingleton(serviceProvider => new SevenDaysRecentActivityRuntime(
                    serviceProvider.GetRequiredService<SevenDaysRecentActivityRecorder>(),
                    serviceProvider.GetRequiredService<PlayerHistoryRuntime>()));
                services.AddSingleton(serviceProvider => new SevenDaysMapProjectionRuntime(
                    serviceProvider.GetRequiredService<SevenDaysMapMetadataProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysMapGameTimeProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysMapLayerProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysTransientEntityProjection>(),
                    serviceProvider.GetRequiredService<SevenDaysRecentActivityRuntime>()));
                services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>
                    serviceProvider.GetRequiredService<ModHost>());
                services.AddSingleton<IModRuntime>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysMapProjectionRuntime>());

                provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
                provider.GetRequiredService<SqliteDatabaseBootstrapper>().Upgrade();
                var inner = provider.GetRequiredService<IModRuntime>();
                var runtime = new ServiceProviderRuntime(inner, provider);
                provider = null;
                return runtime;
            }
            finally
            {
                provider?.Dispose();
            }
        }

        private sealed class UnavailableRestartPolicyQuery : IRestartPolicyQuery
        {
            public RestartPolicySummary Query() => RestartPolicySummary.Unavailable();
        }
    }
}
