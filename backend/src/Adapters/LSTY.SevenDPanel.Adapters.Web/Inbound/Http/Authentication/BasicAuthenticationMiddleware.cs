using Microsoft.Owin;
using Microsoft.Owin.Security.Infrastructure;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class BasicAuthenticationMiddleware
        : AuthenticationMiddleware<BasicAuthenticationOptions>
    {
        public BasicAuthenticationMiddleware(
            OwinMiddleware next,
            BasicAuthenticationOptions options)
            : base(next, options)
        {
        }

        protected override AuthenticationHandler<BasicAuthenticationOptions> CreateHandler() =>
            new BasicAuthenticationHandler();
    }
}
