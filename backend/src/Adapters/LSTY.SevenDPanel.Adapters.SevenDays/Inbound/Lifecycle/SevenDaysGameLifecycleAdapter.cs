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

        public void RegisterAndStart()
        {
            if (registered) return;
            ModEvents.WorldShuttingDown.RegisterHandler(OnWorldShuttingDown);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
            registered = true;
            runtime.Start();
        }

        private void OnWorldShuttingDown(ref ModEvents.SWorldShuttingDownData data) { runtime.Stop(); }
        private void OnGameShutdown(ref ModEvents.SGameShutdownData data) { runtime.Stop(); }

        public void Dispose()
        {
            if (!registered) return;
            ModEvents.WorldShuttingDown.UnregisterHandler(OnWorldShuttingDown);
            ModEvents.GameShutdown.UnregisterHandler(OnGameShutdown);
            registered = false;
        }
    }
}
