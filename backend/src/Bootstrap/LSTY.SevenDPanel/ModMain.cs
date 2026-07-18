using System;
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
            host = new ModHost(
                () => new OwinWebHost(options.Url, OwinStartup.Configure),
                log);
            adapter = new SevenDaysGameLifecycleAdapter(host);
            adapter.Register();
            Log.Out("[7DPanel] Mod initialized; waiting for GameStartDone. URL: " + options.Url);
        }
    }
}
