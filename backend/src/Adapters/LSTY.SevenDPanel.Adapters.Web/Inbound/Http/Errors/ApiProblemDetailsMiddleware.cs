using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Owin;
using Newtonsoft.Json;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors
{
    internal sealed class ApiProblemDetailsMiddleware : OwinMiddleware
    {
        private readonly Action<string> log;

        public ApiProblemDetailsMiddleware(OwinMiddleware next, Action<string>? log)
            : base(next)
        {
            this.log = log ?? (_ => { });
        }

        public override async Task Invoke(IOwinContext context)
        {
            var originalBody = context.Response.Body;
            var trackedBody = new WriteTrackingStream(originalBody);
            context.Response.Body = trackedBody;
            try
            {
                try
                {
                    await Next.Invoke(context).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!IsApiRequest(context.Request.Path.Value)) throw;

                    log("Unhandled 7DPanel API exception. RequestId=" + GetRequestId(context) + ": " + ex);
                    if (trackedBody.HasWritten) return;

                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.ContentType = null;
                }
            }
            finally
            {
                context.Response.Body = originalBody;
            }

            if (!IsApiRequest(context.Request.Path.Value) ||
                context.Response.StatusCode < 400 ||
                trackedBody.HasWritten ||
                !string.IsNullOrEmpty(context.Response.ContentType))
            {
                return;
            }

            var statusCode = (HttpStatusCode)context.Response.StatusCode;
            var description = Describe(statusCode);
            var problem = ApiProblemDetailsFactory.Create(
                context.Request.Path.Value ?? string.Empty,
                GetRequestId(context),
                statusCode,
                description.Code,
                description.Detail);
            context.Response.ContentType = ApiProblemDetailsFactory.ContentType;
            await context.Response.WriteAsync(JsonConvert.SerializeObject(problem)).ConfigureAwait(false);
        }

        private static bool IsApiRequest(string? path) =>
            string.Equals(path, "/api", StringComparison.OrdinalIgnoreCase) ||
            (path?.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ?? false);

        private static string GetRequestId(IOwinContext context)
        {
            if (context.Environment.TryGetValue(
                    RequestCorrelationMiddleware.EnvironmentKey,
                    out var candidate) &&
                candidate is string requestId)
            {
                return requestId;
            }

            return Guid.NewGuid().ToString("N");
        }

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

        private sealed class WriteTrackingStream : Stream
        {
            private readonly Stream inner;
            private int hasWritten;

            public WriteTrackingStream(Stream inner)
            {
                this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public bool HasWritten => Volatile.Read(ref hasWritten) != 0;
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanTimeout => inner.CanTimeout;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }
            public override int ReadTimeout
            {
                get => inner.ReadTimeout;
                set => inner.ReadTimeout = value;
            }
            public override int WriteTimeout
            {
                get => inner.WriteTimeout;
                set => inner.WriteTimeout = value;
            }

            public override void Flush() => inner.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) =>
                inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) =>
                inner.Read(buffer, offset, count);

            public override long Seek(long offset, SeekOrigin origin) =>
                inner.Seek(offset, origin);

            public override void SetLength(long value) => inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                MarkWritten(count);
                inner.Write(buffer, offset, count);
            }

            public override Task WriteAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                MarkWritten(count);
                return inner.WriteAsync(buffer, offset, count, cancellationToken);
            }

            public override void WriteByte(byte value)
            {
                MarkWritten(1);
                inner.WriteByte(value);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }

            private void MarkWritten(int count)
            {
                if (count > 0) Interlocked.Exchange(ref hasWritten, 1);
            }
        }
    }
}
