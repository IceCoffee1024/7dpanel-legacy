using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
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
                services.AddSingleton(_ => new ConsoleLogService(log));
                services.AddSingleton<IServerEventStream>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogService>().Stream);
                services.AddSingleton<SqliteConsoleCommandAuditStore>();
                services.AddSingleton<IConsoleCommandAuditStore>(serviceProvider =>
                    serviceProvider.GetRequiredService<SqliteConsoleCommandAuditStore>());
                services.AddSingleton(serviceProvider => new ConsoleCommandAuditService(
                    serviceProvider.GetRequiredService<IConsoleCommandAuditStore>(),
                    log));
                services.AddSingleton<SevenDaysConsoleCommandService>();
                services.AddSingleton<IConsoleCommandGateway>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysConsoleCommandService>());
                services.AddSingleton<ExecuteConsoleCommandUseCase>();
                services.AddSingleton<SevenDaysOnlinePlayerQuery>();
                services.AddSingleton<IOnlinePlayerQuery>(serviceProvider =>
                    serviceProvider.GetRequiredService<SevenDaysOnlinePlayerQuery>());
                services.AddSingleton<GetOnlinePlayersUseCase>();
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
                services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>
                    serviceProvider.GetRequiredService<ModHost>());
                services.AddSingleton<IModRuntime>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleCommandRuntime>());

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
    }
}
