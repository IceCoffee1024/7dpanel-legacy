using System;
using LSTY.SevenDPanel.Game;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Web;

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
            var options = PanelHostOptions.FromMod(modInstance, log);
            host = new ModHost(
                () => new OwinWebHost(options.Url),
                log);
            adapter = new SevenDaysGameLifecycleAdapter(host);
            adapter.Register();
            Log.Out("[7DPanel] Mod initialized; waiting for GameStartDone. URL: " + options.Url);
        }
    }
}
