using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class PlayerEvidenceRuntime : IModRuntime, IDisposable
    {
        private readonly Action startWriter;
        private readonly Action stopWriter;
        private readonly Action startProjection;
        private readonly Action stopProjection;
        private readonly IModRuntime inner;
        private int started;
        private int stopped;

        public PlayerEvidenceRuntime(
            PlayerEvidenceWriteService writer,
            SevenDaysPlayerEvidenceProjection projection,
            IModRuntime inner)
            : this(
                (writer ?? throw new ArgumentNullException(nameof(writer))).Start,
                writer.Stop,
                (projection ?? throw new ArgumentNullException(nameof(projection))).Start,
                projection.Stop,
                inner)
        {
        }

        internal PlayerEvidenceRuntime(
            Action startWriter,
            Action stopWriter,
            Action startProjection,
            Action stopProjection,
            IModRuntime inner)
        {
            this.startWriter = startWriter ?? throw new ArgumentNullException(nameof(startWriter));
            this.stopWriter = stopWriter ?? throw new ArgumentNullException(nameof(stopWriter));
            this.startProjection = startProjection ?? throw new ArgumentNullException(nameof(startProjection));
            this.stopProjection = stopProjection ?? throw new ArgumentNullException(nameof(stopProjection));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            if (Volatile.Read(ref stopped) != 0 ||
                Interlocked.CompareExchange(ref started, 1, 0) != 0)
            {
                return;
            }

            var writerStarted = false;
            var projectionStarted = false;
            try
            {
                startWriter();
                writerStarted = true;
                startProjection();
                projectionStarted = true;
                inner.Start();
            }
            catch
            {
                if (projectionStarted)
                {
                    try { stopProjection(); } catch { }
                }
                else
                {
                    try { stopProjection(); } catch { }
                }
                if (writerStarted)
                {
                    try { stopWriter(); } catch { }
                }
                Interlocked.Exchange(ref stopped, 1);
                throw;
            }
        }

        public void MarkGameReady() => inner.MarkGameReady();

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;

            var failures = new List<Exception>();
            if (Volatile.Read(ref started) != 0)
            {
                try { inner.Stop(); } catch (Exception exception) { failures.Add(exception); }
                try { stopProjection(); } catch (Exception exception) { failures.Add(exception); }
                try { stopWriter(); } catch (Exception exception) { failures.Add(exception); }
            }
            if (failures.Count != 0) throw new AggregateException(failures);
        }

        public void Dispose() => Stop();
    }
}
