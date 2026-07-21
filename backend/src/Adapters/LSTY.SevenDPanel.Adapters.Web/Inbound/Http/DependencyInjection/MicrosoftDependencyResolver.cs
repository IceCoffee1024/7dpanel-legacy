using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection
{
    internal sealed class MicrosoftDependencyResolver : IDependencyResolver
    {
        private readonly IServiceProvider serviceProvider;

        public MicrosoftDependencyResolver(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider ??
                throw new ArgumentNullException(nameof(serviceProvider));
        }

        public IDependencyScope BeginScope() =>
            new MicrosoftDependencyScope(serviceProvider.CreateScope());

        public object? GetService(Type serviceType) =>
            MicrosoftDependencyScope.Resolve(serviceProvider, serviceType);

        public IEnumerable<object> GetServices(Type serviceType) =>
            serviceProvider.GetServices(serviceType).OfType<object>();

        public void Dispose()
        {
            // The Bootstrap runtime owns the root provider.
        }
    }

    internal sealed class MicrosoftDependencyScope : IDependencyScope
    {
        private readonly IServiceProvider serviceProvider;
        private IServiceScope? ownedScope;

        public MicrosoftDependencyScope(IServiceScope ownedScope)
        {
            this.ownedScope = ownedScope ?? throw new ArgumentNullException(nameof(ownedScope));
            serviceProvider = ownedScope.ServiceProvider;
        }

        public MicrosoftDependencyScope(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider ??
                throw new ArgumentNullException(nameof(serviceProvider));
        }

        public object? GetService(Type serviceType) =>
            Resolve(serviceProvider, serviceType);

        public IEnumerable<object> GetServices(Type serviceType) =>
            serviceProvider.GetServices(serviceType).OfType<object>();

        public void Dispose() =>
            Interlocked.Exchange(ref ownedScope, null)?.Dispose();

        internal static object? Resolve(IServiceProvider serviceProvider, Type serviceType)
        {
            if (typeof(IHttpController).IsAssignableFrom(serviceType))
                return ActivatorUtilities.CreateInstance(serviceProvider, serviceType);

            return serviceProvider.GetService(serviceType);
        }
    }
}
