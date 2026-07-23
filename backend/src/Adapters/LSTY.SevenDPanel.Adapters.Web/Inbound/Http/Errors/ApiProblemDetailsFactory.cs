using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Web.Http;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal static class ApiProblemDetailsFactory
    {
        internal const string ContentType = "application/problem+json";

        public static ApiProblemDetails Create(
            string instance,
            string traceId,
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            return new ApiProblemDetails
            {
                Title = GetTitle(statusCode),
                Status = (int)statusCode,
                Detail = detail,
                Instance = GetSafeInstance(instance),
                Code = code,
                TraceId = traceId
            };
        }

        public static ObjectContent<ApiProblemDetails> CreateContent(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            return CreateContent(
                request,
                statusCode,
                code,
                detail,
                GetSafeInstance(request));
        }

        public static ObjectContent<ApiProblemDetails> CreateContent(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string code,
            string detail,
            string instance)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A problem code is required.", nameof(code));
            if (string.IsNullOrWhiteSpace(detail)) throw new ArgumentException("Problem detail is required.", nameof(detail));

            var traceId = GetTraceId(request);
            var problem = Create(
                instance ?? string.Empty,
                traceId,
                statusCode,
                code,
                detail);
            var formatter = request.GetConfiguration()?.Formatters.JsonFormatter ??
                new JsonMediaTypeFormatter();
            return new ObjectContent<ApiProblemDetails>(
                problem,
                formatter,
                new MediaTypeHeaderValue(ContentType));
        }

        public static HttpResponseMessage CreateResponse(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string code,
            string detail)
        {
            return CreateResponse(
                request,
                statusCode,
                code,
                detail,
                GetSafeInstance(request));
        }

        public static HttpResponseMessage CreateResponse(
            HttpRequestMessage request,
            HttpStatusCode statusCode,
            string code,
            string detail,
            string instance)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                RequestMessage = request,
                Content = CreateContent(request, statusCode, code, detail, instance)
            };
            response.Headers.TryAddWithoutValidation(
                RequestCorrelationMiddleware.HeaderName,
                GetTraceId(request));
            return response;
        }

        public static string GetTraceId(HttpRequestMessage request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            try
            {
                var context = request.GetOwinContext();
                if (context.Environment.TryGetValue(
                        RequestCorrelationMiddleware.EnvironmentKey,
                        out var candidate) &&
                    candidate is string requestId &&
                    RequestCorrelationMiddleware.IsValid(requestId))
                {
                    return requestId;
                }
            }
            catch (InvalidOperationException)
            {
            }

            if (request.Headers.TryGetValues(RequestCorrelationMiddleware.HeaderName, out var values))
            {
                foreach (var value in values)
                {
                    if (RequestCorrelationMiddleware.IsValid(value)) return value;
                }
            }

            return Guid.NewGuid().ToString("N");
        }

        private static string GetSafeInstance(HttpRequestMessage? request)
        {
            return GetSafeInstance(request?.RequestUri?.AbsolutePath);
        }

        private static string GetSafeInstance(string? path)
        {
            path ??= string.Empty;
            const string apiKeysPath = "/api/v1/api-keys";
            if (path.StartsWith(apiKeysPath + "/", StringComparison.OrdinalIgnoreCase))
                return apiKeysPath;

            return path;
        }

        private static string GetTitle(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.BadRequest: return "Bad Request";
                case HttpStatusCode.Unauthorized: return "Unauthorized";
                case HttpStatusCode.Forbidden: return "Forbidden";
                case HttpStatusCode.NotFound: return "Not Found";
                case HttpStatusCode.MethodNotAllowed: return "Method Not Allowed";
                case HttpStatusCode.UnsupportedMediaType: return "Unsupported Media Type";
                case (HttpStatusCode)429: return "Too Many Requests";
                case HttpStatusCode.InternalServerError: return "Internal Server Error";
                case HttpStatusCode.ServiceUnavailable: return "Service Unavailable";
                default: return "Error";
            }
        }
    }
}
