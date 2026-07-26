using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.GameResources
{
    public sealed class GameResourceCatalogRuntime : IModRuntime, IDisposable
    {
        private static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(5);

        private readonly object sync = new object();
        private readonly Func<CancellationToken, Task> buildAsync;
        private readonly IModRuntime inner;
        private readonly TimeSpan stopTimeout;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task? buildTask;
        private int buildStarted;
        private int stopped;

        public GameResourceCatalogRuntime(
            SevenDaysGameResourceCatalog catalog,
            IModRuntime inner)
            : this(
                (catalog ?? throw new ArgumentNullException(nameof(catalog))).BuildAsync,
                inner,
                DefaultStopTimeout)
        {
        }

        internal GameResourceCatalogRuntime(
            Func<CancellationToken, Task> buildAsync,
            IModRuntime inner,
            TimeSpan stopTimeout)
        {
            if (stopTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(stopTimeout));
            this.buildAsync = buildAsync ?? throw new ArgumentNullException(nameof(buildAsync));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.stopTimeout = stopTimeout;
        }

        public void Start() => inner.Start();

        public void MarkGameReady()
        {
            inner.MarkGameReady();
            lock (sync)
            {
                if (Volatile.Read(ref stopped) != 0 ||
                    Interlocked.CompareExchange(ref buildStarted, 1, 0) != 0)
                {
                    return;
                }

                try
                {
                    buildTask = buildAsync(lifetime.Token) ??
                        Task.FromException(new InvalidOperationException(
                            "The game resource catalog returned no build task."));
                }
                catch (Exception exception)
                {
                    buildTask = Task.FromException(exception);
                }
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;

            var failures = new List<Exception>();
            Task? currentBuild;
            lock (sync)
            {
                currentBuild = buildTask;
                try { lifetime.Cancel(); }
                catch (Exception exception) { failures.Add(exception); }
            }

            if (currentBuild != null)
                WaitForBuild(currentBuild, failures);

            try { inner.Stop(); }
            catch (Exception exception) { failures.Add(exception); }
            lifetime.Dispose();

            if (failures.Count != 0)
                throw new AggregateException(failures);
        }

        public void Dispose() => Stop();

        private void WaitForBuild(Task task, ICollection<Exception> failures)
        {
            try
            {
                if (!task.Wait(stopTimeout))
                {
                    failures.Add(new TimeoutException(
                        "Timed out waiting for the game resource catalog build to stop."));
                }
            }
            catch (AggregateException exception)
            {
                if (task.IsCanceled) return;
                foreach (var failure in exception.Flatten().InnerExceptions)
                    failures.Add(failure);
            }
        }
    }
}
