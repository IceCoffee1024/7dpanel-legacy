using System;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.DependencyInjection.Registration;
using LSTY.SevenDPanel.Hosting;
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
            var context = new PanelCompositionContext(
                options,
                dataDirectory,
                assetRoot,
                log);
            ServiceProvider? provider = null;
            try
            {
                var services = new ServiceCollection();
                PlatformServiceRegistration.Register(services, context);
                OperationsServiceRegistration.Register(services, context);
                PlayersServiceRegistration.Register(services, context);
                CommunityServiceRegistration.Register(services, context);
                EconomyServiceRegistration.Register(services, context);
                AutomationServiceRegistration.Register(services, context);
                AdministrationServiceRegistration.Register(services, context);

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
