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
            var provider = Volatile.Read(ref serviceProvider);
            if (provider == null) return;

            try
            {
                inner.Stop();
            }
            catch (Exception ex)
            {
                throw new AggregateException(ex);
            }

            provider = Interlocked.Exchange(ref serviceProvider, null);
            if (provider == null) return;
            try
            {
                provider.Dispose();
            }
            catch (Exception ex)
            {
                throw new AggregateException(ex);
            }
        }

        public void Dispose() => Stop();
    }
}
