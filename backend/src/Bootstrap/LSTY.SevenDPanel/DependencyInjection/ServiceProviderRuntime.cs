using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class ServiceProviderRuntime : IModRuntime, IDisposable
    {
        private readonly IModRuntime inner;
        private readonly object lifecycleSync = new object();
        private IDisposable? serviceProvider;

        public ServiceProviderRuntime(IModRuntime inner, IDisposable serviceProvider)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.serviceProvider = serviceProvider ??
                throw new ArgumentNullException(nameof(serviceProvider));
        }

        public void Start()
        {
            lock (lifecycleSync)
            {
                if (serviceProvider == null)
                    throw new ObjectDisposedException(nameof(ServiceProviderRuntime));
                inner.Start();
            }
        }

        public void MarkGameReady()
        {
            lock (lifecycleSync)
            {
                if (serviceProvider != null) inner.MarkGameReady();
            }
        }

        public void Stop()
        {
            lock (lifecycleSync)
            {
                if (serviceProvider == null) return;
                try
                {
                    inner.Stop();
                }
                catch (Exception ex)
                {
                    throw new AggregateException(ex);
                }
            }
        }

        public void Dispose()
        {
            lock (lifecycleSync)
            {
                var provider = serviceProvider;
                if (provider == null) return;

                try
                {
                    inner.Stop();
                }
                catch (Exception ex)
                {
                    throw new AggregateException(ex);
                }

                serviceProvider = null;
                try
                {
                    provider.Dispose();
                }
                catch (Exception ex)
                {
                    throw new AggregateException(ex);
                }
            }
        }
    }
}
