using System;
using System.IO;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http;
using LSTY.SevenDPanel.Adapters.Web.Outbound.Hosting;
using LSTY.SevenDPanel.Configuration;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel
{
    public sealed class ModMain : IModApi
    {
        private ModHost host;
        private SevenDaysGameLifecycleAdapter adapter;

        public void InitMod(Mod modInstance)
        {
            if (host != null) return;

            Action<string> log = message => Log.Out("[7DPanel] " + message);
            var options = PanelHostConfigurationLoader.FromMod(modInstance, log);
            var assetRoot = modInstance == null || string.IsNullOrWhiteSpace(modInstance.Path)
                ? null
                : Path.Combine(modInstance.Path, "wwwroot");
            host = new ModHost(
                () => new OwinWebHost(options.Url, app => OwinStartup.Configure(app, assetRoot, log)),
                log);
            adapter = new SevenDaysGameLifecycleAdapter(host);
            adapter.RegisterAndStart();
            Log.Out("[7DPanel] Mod initialized. URL: " + options.Url);
        }
    }
}
