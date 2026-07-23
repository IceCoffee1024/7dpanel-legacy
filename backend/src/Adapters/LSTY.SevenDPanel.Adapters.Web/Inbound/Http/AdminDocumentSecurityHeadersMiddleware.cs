using System;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class AdminDocumentSecurityHeadersMiddleware : OwinMiddleware
    {
        internal const string ContentSecurityPolicy =
            "default-src 'self'; base-uri 'self'; object-src 'none'; " +
            "frame-ancestors 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; font-src 'self'; connect-src 'self'; form-action 'self'";

        public AdminDocumentSecurityHeadersMiddleware(OwinMiddleware next)
            : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (IsAdminDocumentRequest(context.Request.Method, context.Request.Path.Value))
            {
                context.Response.Headers.Set(
                    "Content-Security-Policy",
                    ContentSecurityPolicy);
            }

            await Next.Invoke(context).ConfigureAwait(false);
        }

        private static bool IsAdminDocumentRequest(string method, string? path) =>
            (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) &&
            (string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase) ||
                OwinStartup.ShouldUseSpaFallback(method, path));
    }
}