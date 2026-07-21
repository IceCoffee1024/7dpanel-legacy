using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class ServiceProviderRuntime : IModRuntime, IDisposable
    {
        private readonly IModRuntime inner;
        private IDisposable? serviceProvider;

        public ServiceProviderRuntime(IModRuntime inner, IDisposable serviceProvider)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.serviceProvider = serviceProvider ??
                throw new ArgumentNullException(nameof(serviceProvider));
        }

        public void Start()
        {
            if (Volatile.Read(ref serviceProvider) == null)
                throw new ObjectDisposedException(nameof(ServiceProviderRuntime));
            inner.Start();
        }

        public void MarkGameReady()
        {
            if (Volatile.Read(ref serviceProvider) != null) inner.MarkGameReady();
        }

        public void Stop()
        {
            var provider = Interlocked.Exchange(ref serviceProvider, null);
            if (provider == null) return;

            var failures = new List<Exception>();
            try { inner.Stop(); } catch (Exception ex) { failures.Add(ex); }
            try { provider.Dispose(); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose() => Stop();
    }
}
