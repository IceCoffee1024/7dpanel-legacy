using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class OnlinePlayerProjectionRuntime : IModRuntime, IDisposable
    {
        private readonly Action startProjection;
        private readonly Action stopProjection;
        private readonly IModRuntime inner;
        private int stopped;

        public OnlinePlayerProjectionRuntime(
            SevenDaysOnlinePlayerProjection projection,
            IModRuntime inner)
            : this(projection.Start, projection.Stop, inner)
        {
        }

        internal OnlinePlayerProjectionRuntime(
            Action startProjection,
            Action stopProjection,
            IModRuntime inner)
        {
            this.startProjection = startProjection ?? throw new ArgumentNullException(nameof(startProjection));
            this.stopProjection = stopProjection ?? throw new ArgumentNullException(nameof(stopProjection));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            startProjection();
            try
            {
                inner.Start();
            }
            catch
            {
                try { stopProjection(); } catch { }
                throw;
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;

            var failures = new List<Exception>();
            try { inner.Stop(); } catch (Exception ex) { failures.Add(ex); }
            try { stopProjection(); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}