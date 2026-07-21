using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection
{
    internal sealed class OwinScopeBridgingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var owinContext = request.GetOwinContext();
            if (!owinContext.Environment.TryGetValue(
                    ScopedServiceProviderMiddleware.EnvironmentKey,
                    out var candidate) ||
                candidate is not IServiceScope scope)
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            request.Properties.TryGetValue(HttpPropertyKeys.DependencyScope, out var previous);
            var bridge = new MicrosoftDependencyScope(scope.ServiceProvider);
            request.Properties[HttpPropertyKeys.DependencyScope] = bridge;
            try
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (previous == null)
                    request.Properties.Remove(HttpPropertyKeys.DependencyScope);
                else
                    request.Properties[HttpPropertyKeys.DependencyScope] = previous;
            }
        }
    }
}
