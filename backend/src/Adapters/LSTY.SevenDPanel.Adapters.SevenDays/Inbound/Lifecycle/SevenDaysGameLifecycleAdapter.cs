using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Lifecycle
{
    public sealed class SevenDaysGameLifecycleAdapter : IDisposable
    {
        private readonly IModRuntime runtime;
        private readonly ISevenDaysLifecycleEvents events;
        private readonly Action? onGameStartDone;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private bool registered;
        private bool disposed;

        public SevenDaysGameLifecycleAdapter(
            IModRuntime runtime,
            Action? onGameStartDone = null)
            : this(runtime, new SevenDaysModEvents(), onGameStartDone)
        {
        }

        internal SevenDaysGameLifecycleAdapter(
            IModRuntime runtime,
            ISevenDaysLifecycleEvents events,
            Action? onGameStartDone = null)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.onGameStartDone = onGameStartDone;
        }

        public void RegisterAndStart()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SevenDaysGameLifecycleAdapter));
            if (registered) return;

            var pending = new List<IDisposable>();
            var startAttempted = false;
            try
            {
                pending.Add(events.SubscribeWorldShuttingDown(OnWorldShuttingDown));
                pending.Add(events.SubscribeGameShutdown(OnGameShutdown));
                pending.Add(events.SubscribeGameStartDone(OnGameStartDone));
                subscriptions.AddRange(pending);
                registered = true;
                startAttempted = true;
                runtime.Start();
            }
            catch
            {
                registered = false;
                subscriptions.Clear();
                DisposeReverse(pending, false);
                if (startAttempted)
                {
                    try { runtime.Stop(); } catch { }
                }
                throw;
            }
        }

        private void OnGameStartDone()
        {
            runtime.MarkGameReady();
            onGameStartDone?.Invoke();
        }
        private void OnWorldShuttingDown() { runtime.Stop(); }
        private void OnGameShutdown() { runtime.Stop(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            registered = false;
            var failures = DisposeReverse(subscriptions, true);
            subscriptions.Clear();
            if (failures != null) throw new AggregateException(failures);
        }

        private static List<Exception>? DisposeReverse(
            IList<IDisposable> current,
            bool captureFailures)
        {
            List<Exception>? failures = null;
            for (var index = current.Count - 1; index >= 0; index--)
            {
                try
                {
                    current[index].Dispose();
                }
                catch (Exception ex)
                {
                    if (!captureFailures) continue;
                    if (failures == null) failures = new List<Exception>();
                    failures.Add(ex);
                }
            }

            return failures;
        }
    }
}
