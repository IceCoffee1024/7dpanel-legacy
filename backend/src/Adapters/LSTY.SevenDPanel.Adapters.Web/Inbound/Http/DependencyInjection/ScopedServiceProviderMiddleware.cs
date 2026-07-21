using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection
{
    internal sealed class ScopedServiceProviderMiddleware : OwinMiddleware
    {
        internal const string EnvironmentKey =
            "LSTY.SevenDPanel.Web:RequestServiceScope";

        private readonly IServiceScopeFactory scopeFactory;

        public ScopedServiceProviderMiddleware(
            OwinMiddleware next,
            IServiceProvider serviceProvider)
            : base(next)
        {
            if (serviceProvider == null) throw new ArgumentNullException(nameof(serviceProvider));
            scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (context.Environment.ContainsKey(EnvironmentKey))
                throw new InvalidOperationException("A request service scope already exists.");

            using (var scope = scopeFactory.CreateScope())
            {
                context.Environment.Add(EnvironmentKey, scope);
                try
                {
                    await Next.Invoke(context).ConfigureAwait(false);
                }
                finally
                {
                    context.Environment.Remove(EnvironmentKey);
                }
            }
        }
    }
}
