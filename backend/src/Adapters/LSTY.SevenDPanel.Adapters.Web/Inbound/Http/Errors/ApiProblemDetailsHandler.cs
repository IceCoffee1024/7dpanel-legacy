using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class ApiProblemDetailsHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode < 400 || IsProblemDetails(response)) return response;

            var description = Describe(response.StatusCode);
            var oldContent = response.Content;
            response.Content = ApiProblemDetailsFactory.CreateContent(
                request,
                response.StatusCode,
                description.Code,
                description.Detail);
            response.RequestMessage = request;
            response.Headers.Remove(RequestCorrelationMiddleware.HeaderName);
            response.Headers.TryAddWithoutValidation(
                RequestCorrelationMiddleware.HeaderName,
                ApiProblemDetailsFactory.GetTraceId(request));
            oldContent?.Dispose();
            return response;
        }

        private static bool IsProblemDetails(HttpResponseMessage response) =>
            string.Equals(
                response.Content?.Headers.ContentType?.MediaType,
                ApiProblemDetailsFactory.ContentType,
                System.StringComparison.OrdinalIgnoreCase);

        private static ProblemDescription Describe(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.BadRequest:
                    return new ProblemDescription("bad_request", "The request is invalid.");
                case HttpStatusCode.Unauthorized:
                    return new ProblemDescription("authentication_required", "Authentication is required to access this resource.");
                case HttpStatusCode.Forbidden:
                    return new ProblemDescription("forbidden", "The authenticated identity cannot access this resource.");
                case HttpStatusCode.NotFound:
                    return new ProblemDescription("resource_not_found", "The requested API resource was not found.");
                case HttpStatusCode.MethodNotAllowed:
                    return new ProblemDescription("method_not_allowed", "The HTTP method is not supported for this resource.");
                case HttpStatusCode.UnsupportedMediaType:
                    return new ProblemDescription("unsupported_media_type", "The request media type is not supported.");
                case (HttpStatusCode)429:
                    return new ProblemDescription("too_many_requests", "The request rate limit was exceeded.");
                case HttpStatusCode.ServiceUnavailable:
                    return new ProblemDescription("service_unavailable", "The service is temporarily unavailable.");
                default:
                    return new ProblemDescription("internal_server_error", "An unexpected error occurred while processing the request.");
            }
        }

        private readonly struct ProblemDescription
        {
            public ProblemDescription(string code, string detail)
            {
                Code = code;
                Detail = detail;
            }

            public string Code { get; }
            public string Detail { get; }
        }
    }
}
