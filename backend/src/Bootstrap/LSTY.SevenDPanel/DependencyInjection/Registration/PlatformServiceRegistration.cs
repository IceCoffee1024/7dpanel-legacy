using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Platform;
using LSTY.SevenDPanel.Adapters.Local.WorldOperations;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Modules;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.WorldOperations;
using LSTY.SevenDPanel.Application.Modules;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection.Registration
{
    internal static class PlatformServiceRegistration
    {
        internal static void Register(
            IServiceCollection services,
            PanelCompositionContext context)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var options = context.Options;
            var dataDirectory = context.DataDirectory;
            var log = context.Log;

            services.AddSingleton(options);
            services.AddSingleton(options.Authentication);
            services.AddSingleton(options.Overview);
            services.AddSingleton(options.Restart);
            services.AddSingleton(options.PlayerEvidence);
            services.AddSingleton(_ => new SqliteConnectionFactory(
                Path.Combine(dataDirectory, "7dpanel.db")));
            services.AddSingleton(serviceProvider => new SqliteDatabaseBootstrapper(
                serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
                log));
            services.AddSingleton(_ => CreateApprovedStorageRoots(options, dataDirectory));
            services.AddSingleton<AtomicFileWriter>();

            services.AddSingleton<SqliteWorldChangeSetMetadataStore>();
            services.AddSingleton<IWorldChangeSetMetadataStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteWorldChangeSetMetadataStore>());
            services.AddSingleton(_ => new LocalWorldChangeSetBlobStore(
                Path.Combine(dataDirectory, "world-change-sets")));
            services.AddSingleton<IWorldChangeSetBlobStore>(serviceProvider =>
                serviceProvider.GetRequiredService<LocalWorldChangeSetBlobStore>());
            services.AddSingleton<SqliteFeatureModuleStateStore>();
            services.AddSingleton<IFeatureModuleStateStore>(serviceProvider =>
                serviceProvider.GetRequiredService<SqliteFeatureModuleStateStore>());
            services.AddSingleton<FeatureModuleGate>();
            services.AddSingleton(serviceProvider => new FeatureModuleWorldOperationJobBridge(
                serviceProvider.GetRequiredService<SqliteWorldOperationJobBridge>(),
                serviceProvider.GetRequiredService<FeatureModuleGate>()));
            services.AddSingleton<IWorldOperationJobBridge>(serviceProvider =>
                serviceProvider.GetRequiredService<FeatureModuleWorldOperationJobBridge>());
            services.AddSingleton<FeatureModuleJobActivityQuery>();
            services.AddSingleton<IFeatureModuleActivityQuery>(serviceProvider =>
                serviceProvider.GetRequiredService<FeatureModuleJobActivityQuery>());
            services.AddSingleton(serviceProvider => new FeatureModuleUseCases(
                serviceProvider.GetRequiredService<IFeatureModuleStateStore>(),
                serviceProvider.GetRequiredService<IFeatureModuleActivityQuery>(),
                () => DateTimeOffset.UtcNow));

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
        }

        private static ApprovedStorageRoots CreateApprovedStorageRoots(
            PanelHostOptions options,
            string dataDirectory)
        {
            var serverConfigurationRoot = Path.GetDirectoryName(
                options.ServerConfigurationPath);
            if (string.IsNullOrWhiteSpace(serverConfigurationRoot))
            {
                throw new InvalidOperationException(
                    "The approved server configuration root is unavailable.");
            }

            return new ApprovedStorageRoots(
                global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld),
                global::GameIO.GetSaveGameDir(),
                dataDirectory,
                serverConfigurationRoot!,
                "primary",
                Path.Combine(dataDirectory, "backups"),
                global::GamePrefs.GetString(global::EnumGamePrefs.GameVersion));
        }
    }
}
