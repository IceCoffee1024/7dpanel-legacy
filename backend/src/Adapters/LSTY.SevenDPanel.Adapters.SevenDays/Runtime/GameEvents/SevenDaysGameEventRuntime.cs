using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Inbound.GameEvents;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents
{
    public sealed class SevenDaysGameEventRuntime : IModRuntime, IDisposable
    {
        private readonly GameEventWriteService writer;
        private readonly Func<IDisposable> subscribe;
        private readonly IModRuntime inner;
        private readonly object gate = new object();
        private IDisposable? subscription;
        private bool started;
        private bool stopped;
        public SevenDaysGameEventRuntime(GameEventWriteService writer, SevenDaysGameEventAdapter adapter, IModRuntime inner) : this(writer, () => (adapter ?? throw new ArgumentNullException(nameof(adapter))).Subscribe(), inner) { }
        internal SevenDaysGameEventRuntime(GameEventWriteService writer, Func<IDisposable> subscribe, IModRuntime inner)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer)); this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe)); this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }
        public void Start()
        {
            lock (gate)
            {
                if (started || stopped) return;
                try { writer.Start(); subscription = subscribe(); inner.Start(); started = true; }
                catch { try { subscription?.Dispose(); } catch { } try { writer.Stop(); } catch { } try { inner.Stop(); } catch { } stopped = true; throw; }
            }
        }
        public void MarkGameReady() => inner.MarkGameReady();
        public void Stop()
        {
            lock (gate)
            {
                if (stopped) return;
                stopped = true; started = false;
                var failures = new List<Exception>();
                try { Interlocked.Exchange(ref subscription, null)?.Dispose(); } catch (Exception exception) { failures.Add(exception); }
                try { writer.Stop(); } catch (Exception exception) { failures.Add(exception); }
                try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }
        public void Dispose() => Stop();
    }
}
