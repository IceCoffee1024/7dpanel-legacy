using System;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle
{
    public sealed class SevenDaysGameLifecycleAdapter : IDisposable
    {
        private readonly IModRuntime runtime;
        private bool registered;

        public SevenDaysGameLifecycleAdapter(IModRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Register()
        {
            if (registered) return;
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
            registered = true;
        }

        private void OnGameStartDone(ref ModEvents.SGameStartDoneData data) { runtime.Start(); }
        private void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data) { runtime.Stop(); }
        private void OnGameShutdown(ref ModEvents.SGameShutdownData data) { runtime.Stop(); }

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
