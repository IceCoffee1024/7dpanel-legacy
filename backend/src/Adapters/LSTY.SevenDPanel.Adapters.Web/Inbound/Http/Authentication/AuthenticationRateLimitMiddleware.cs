using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using Microsoft.Owin;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class AuthenticationRateLimitMiddleware : OwinMiddleware
    {
        private readonly AuthenticationAttemptLimiter limiter;

        public AuthenticationRateLimitMiddleware(
            OwinMiddleware next,
            AuthenticationAttemptLimiter limiter)
            : base(next)
        {
            this.limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        }

        public override async Task Invoke(IOwinContext context)
        {
            if (!ShouldLimit(context.Request) ||
                limiter.TryAcquire(context.Request.RemoteIpAddress ?? "<unknown>", out var retryAfter))
            {
                await Next.Invoke(context).ConfigureAwait(false);
                return;
            }

            var retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.Response.StatusCode = 429;
            context.Response.ContentType = ApiProblemDetailsFactory.ContentType;
            context.Response.Headers.Set(
                "Retry-After",
                retrySeconds.ToString(CultureInfo.InvariantCulture));
            var traceId = context.Environment.TryGetValue(
                    RequestCorrelationMiddleware.EnvironmentKey,
                    out var candidate) && candidate is string requestId
                ? requestId
                : Guid.NewGuid().ToString("N");
            var problem = ApiProblemDetailsFactory.Create(
                context.Request.Path.Value ?? string.Empty,
                traceId,
                (HttpStatusCode)429,
                "too_many_requests",
                "The authentication request rate limit was exceeded.");
            await context.Response.WriteAsync(JsonConvert.SerializeObject(problem)).ConfigureAwait(false);
        }

        private static bool ShouldLimit(IOwinRequest request)
        {
            if (string.Equals(
                    request.Path.Value,
                    HttpRoutes.TokenEndpoint,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.Equals(
                    request.Path.Value,
                    "/api/v1/events/stream",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var authorization = request.Headers.Get("Authorization");
            return authorization?.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ?? false;
        }
    }
}
