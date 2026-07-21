using System;
using System.Threading.Tasks;
using Microsoft.Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class RequestCorrelationMiddleware : OwinMiddleware
    {
        internal const string HeaderName = "X-Request-ID";
        internal const string EnvironmentKey = "LSTY.SevenDPanel.Web:RequestId";
        private const int MaximumRequestIdLength = 64;

        public RequestCorrelationMiddleware(OwinMiddleware next)
            : base(next)
        {
        }

        public override async Task Invoke(IOwinContext context)
        {
            var supplied = context.Request.Headers.Get(HeaderName);
            var requestId = IsValid(supplied)
                ? supplied!
                : Guid.NewGuid().ToString("N");

            context.Environment[EnvironmentKey] = requestId;
            context.Request.Headers.Set(HeaderName, requestId);
            context.Response.Headers.Set(HeaderName, requestId);
            await Next.Invoke(context).ConfigureAwait(false);
        }

        internal static bool IsValid(string? value)
        {
            if (value == null || value.Length == 0 || value.Length > MaximumRequestIdLength)
                return false;

            foreach (var character in value)
            {
                var valid = character >= 'a' && character <= 'z' ||
                    character >= 'A' && character <= 'Z' ||
                    character >= '0' && character <= '9' ||
                    character == '.' || character == '_' || character == '-';
                if (!valid) return false;
            }

            return true;
        }
    }
}
