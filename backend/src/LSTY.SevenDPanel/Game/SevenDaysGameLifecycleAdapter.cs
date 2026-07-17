using System;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Game
{
    public sealed class SevenDaysGameLifecycleAdapter : IDisposable
    {
        private readonly ModHost host;
        private bool registered;

        public SevenDaysGameLifecycleAdapter(ModHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Register()
        {
            if (registered) return;
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
            registered = true;
        }

        private void OnGameStartDone(ref ModEvents.SGameStartDoneData data) { host.Start(); }
        private void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data) { host.Stop(); }
        private void OnGameShutdown(ref ModEvents.SGameShutdownData data) { host.Stop(); }

        public void Dispose()
        {
            if (!registered) return;
            ModEvents.GameStartDone.UnregisterHandler(OnGameStartDone);
            ModEvents.WorldShuttingDown.UnregisterHandler(OnWorldShuttingDown);
            ModEvents.GameShutdown.UnregisterHandler(OnGameShutdown);
            registered = false;
        }
    }
}
