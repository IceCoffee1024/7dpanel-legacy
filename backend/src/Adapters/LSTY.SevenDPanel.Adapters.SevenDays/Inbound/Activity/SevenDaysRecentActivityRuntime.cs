using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity
{
    public sealed class SevenDaysRecentActivityRuntime : IModRuntime, IDisposable
    {
        private readonly SevenDaysRecentActivityRecorder recorder;
        private readonly IModRuntime inner;
        private readonly object lifecycleGate = new object();
        private bool started;
        private bool stopped;

        public SevenDaysRecentActivityRuntime(
            SevenDaysRecentActivityRecorder recorder,
            IModRuntime inner)
        {
            this.recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            lock (lifecycleGate)
            {
                if (stopped || started) return;

                try
                {
                    inner.Start();
                    recorder.Start();
                    started = true;
                }
                catch
                {
                    try { recorder.Dispose(); } catch { }
                    try { inner.Stop(); } catch { }
                    stopped = true;
                    throw;
                }
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            lock (lifecycleGate)
            {
                if (stopped) return;
                stopped = true;
                started = false;

                var failures = new List<Exception>();
                try { recorder.Dispose(); } catch (Exception exception) { failures.Add(exception); }
                try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose() => Stop();
    }
}
