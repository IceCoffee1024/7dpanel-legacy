using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class PlayerHistoryRuntime : IModRuntime, IDisposable
    {
        private readonly Action startHistory;
        private readonly Action stopHistory;
        private readonly IModRuntime inner;
        private int stopped;

        public PlayerHistoryRuntime(PlayerHistoryWriteService history, IModRuntime inner)
            : this(
                history == null
                    ? throw new ArgumentNullException(nameof(history))
                    : (Action)history.Start,
                history == null
                    ? throw new ArgumentNullException(nameof(history))
                    : (Action)history.Stop,
                inner)
        {
        }

        internal PlayerHistoryRuntime(
            Action startHistory,
            Action stopHistory,
            IModRuntime inner)
        {
            this.startHistory = startHistory ?? throw new ArgumentNullException(nameof(startHistory));
            this.stopHistory = stopHistory ?? throw new ArgumentNullException(nameof(stopHistory));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            startHistory();
            try { inner.Start(); }
            catch
            {
                try { stopHistory(); } catch { }
                throw;
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            var failures = new List<Exception>();
            try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
            try { stopHistory(); } catch (Exception exception) { failures.Add(exception); }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose() => Stop();
    }
}
