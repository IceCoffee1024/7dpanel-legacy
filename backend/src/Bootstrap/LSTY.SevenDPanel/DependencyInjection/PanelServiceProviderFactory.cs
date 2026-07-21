using System;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal static class PanelServiceProviderFactory
    {
        public static ServiceProviderRuntime CreateRuntime(
            PanelHostOptions options,
            string? assetRoot,
            Action<string> log)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (log == null) throw new ArgumentNullException(nameof(log));

            ServiceProvider? provider = null;
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton(options);
                services.AddSingleton(_ => new ConsoleLogService(log));
                services.AddSingleton<IServerEventStream>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogService>().Stream);
                services.AddScoped<ServerEventSseSession>();
                services.AddSingleton(serviceProvider => new ModHost(
                    () => new OwinWebHost(
                        options.Url,
                        app => OwinStartup.Configure(
                            app,
                            serviceProvider,
                            assetRoot,
                            log)),
                    log));
                services.AddSingleton(serviceProvider => new ConsoleLogRuntime(
                    serviceProvider.GetRequiredService<ConsoleLogService>(),
                    serviceProvider.GetRequiredService<ModHost>()));
                services.AddSingleton<IPanelRuntimeStatus>(serviceProvider =>
                    serviceProvider.GetRequiredService<ModHost>());
                services.AddSingleton<IModRuntime>(serviceProvider =>
                    serviceProvider.GetRequiredService<ConsoleLogRuntime>());

                provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
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
