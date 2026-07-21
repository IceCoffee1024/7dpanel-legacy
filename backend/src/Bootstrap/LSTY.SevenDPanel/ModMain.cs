using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle;
using LSTY.SevenDPanel.Configuration;
using LSTY.SevenDPanel.DependencyInjection;

namespace LSTY.SevenDPanel
{
    public sealed class ModMain : IModApi
    {
        private ServiceProviderRuntime? runtime;
        private SevenDaysGameLifecycleAdapter? adapter;

        public void InitMod(Mod? modInstance)
        {
            if (runtime != null) return;

            Action<string> log = message => Log.Out("[7DPanel] " + message);
            var options = PanelHostConfigurationLoader.FromMod(modInstance, log);
            var assetRoot = modInstance == null || string.IsNullOrWhiteSpace(modInstance.Path)
                ? null
                : Path.Combine(modInstance.Path, "wwwroot");
            if (options.Authentication.Enabled && options.Authentication.AllowInsecureHttp)
            {
                log("WARNING: authentication over insecure HTTP is enabled. " +
                    "Use this only for local development or behind a controlled local TLS proxy.");
            }
            var candidateRuntime = PanelServiceProviderFactory.CreateRuntime(
                options,
                assetRoot,
                log);
            var candidateAdapter = new SevenDaysGameLifecycleAdapter(candidateRuntime);
            try
            {
                candidateAdapter.RegisterAndStart();
                runtime = candidateRuntime;
                adapter = candidateAdapter;
            }
            catch
            {
                try { candidateAdapter.Dispose(); } catch { }
                try { candidateRuntime.Dispose(); } catch { }
                throw;
            }
            Log.Out("[7DPanel] Mod initialized. URL: " + options.Url);
        }
    }
}
