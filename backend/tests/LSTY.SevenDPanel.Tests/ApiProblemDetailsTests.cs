using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.ExceptionHandling;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using Microsoft.Owin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Platform")]
    [Trait("Boundary", "Application")]
    public sealed class ApiProblemDetailsTests
    {
        [Fact]
        public async Task Unhandled_api_exception_returns_problem_details_with_correlation_id()
        {
            var logs = new List<string>();
            var context = CreateContext("/api/v1/failure");
            var middleware = CreateMiddleware(
                _ => Task.FromException(new InvalidOperationException("boom")),
                logs.Add);

            await middleware.Invoke(context);

            Assert.Equal((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
            Assert.Equal(ApiProblemDetailsFactory.ContentType, context.Response.ContentType);
            var payload = await ReadJsonAsync(context.Response.Body);
            var requestId = context.Response.Headers.Get(RequestCorrelationMiddleware.HeaderName);
            Assert.Equal("internal_server_error", (string?)payload["code"]);
            Assert.Equal("/api/v1/failure", (string?)payload["instance"]);
            Assert.Equal(requestId, (string?)payload["traceId"]);
            Assert.Single(logs);
            Assert.Contains(requestId, logs[0]);
        }

        [Fact]
        public async Task Request_cancellation_is_not_converted_to_an_internal_server_error()
        {
            var logs = new List<string>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var context = CreateContext("/api/v1/players/7/kick");
            var middleware = CreateMiddleware(
                _ => Task.FromCanceled(cancellation.Token),
                logs.Add);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middleware.Invoke(context));

            Assert.Empty(logs);
            Assert.NotEqual((int)HttpStatusCode.InternalServerError, context.Response.StatusCode);
        }

        [Fact]
        public async Task Exception_after_sse_body_starts_does_not_append_problem_details()
        {
            var logs = new List<string>();
            var context = CreateContext("/api/v1/events/stream");
            var middleware = CreateMiddleware(
                async owinContext =>
                {
                    owinContext.Response.StatusCode = (int)HttpStatusCode.OK;
                    owinContext.Response.ContentType = "text/event-stream";
                    await owinContext.Response.WriteAsync("event: welcome\n\n");
                    throw new InvalidOperationException("stream failed");
                },
                logs.Add);

            await middleware.Invoke(context);

            Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
            Assert.Equal("text/event-stream", context.Response.ContentType);
            Assert.Equal("event: welcome\n\n", await ReadTextAsync(context.Response.Body));
            Assert.Single(logs);
        }

        [Fact]
        public async Task Existing_error_body_is_not_appended_after_writing_starts()
        {
            var context = CreateContext("/api/v1/failure");
            var middleware = CreateMiddleware(
                async owinContext =>
                {
                    owinContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    await owinContext.Response.WriteAsync("existing-error");
                },
                _ => { });

            await middleware.Invoke(context);

            Assert.Equal((int)HttpStatusCode.BadRequest, context.Response.StatusCode);
            Assert.Equal("existing-error", await ReadTextAsync(context.Response.Body));
        }

        [Fact]
        public async Task Web_api_logger_records_exception_when_no_response_can_be_selected()
        {
            const string requestId = "stream-write-failure";
            const string failureMessage = "stream serialization failed";
            var logs = new List<string>();
            IExceptionLogger logger = new OwinUnhandledExceptionLogger(logs.Add);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "http://localhost/api/v1/events/stream");
            request.Headers.Add(RequestCorrelationMiddleware.HeaderName, requestId);
            var exceptionContext = new ExceptionContext(
                new InvalidOperationException(failureMessage),
                new ExceptionContextCatchBlock(
                    "StreamContent",
                    isTopLevel: true,
                    callsHandler: false),
                request);

            await logger.LogAsync(
                new ExceptionLoggerContext(exceptionContext),
                CancellationToken.None);

            var log = Assert.Single(logs);
            Assert.Contains(requestId, log);
            Assert.Contains(failureMessage, log);
        }

        [Fact]
        public async Task Web_api_logger_defers_handleable_exception_to_owin_boundary()
        {
            var logs = new List<string>();
            IExceptionLogger logger = new OwinUnhandledExceptionLogger(logs.Add);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "http://localhost/api/v1/failure");
            var exceptionContext = new ExceptionContext(
                new InvalidOperationException("handled outside Web API"),
                new ExceptionContextCatchBlock(
                    "BufferContent",
                    isTopLevel: true,
                    callsHandler: true),
                request);

            await logger.LogAsync(
                new ExceptionLoggerContext(exceptionContext),
                CancellationToken.None);

            Assert.Empty(logs);
        }

        [Fact]
        public async Task Owin_fallback_does_not_echo_an_api_key_in_the_problem_instance()
        {
            const string apiKey = "7dp_k_test-key_sssssssssssssssssssssssssssssssssss";
            var context = CreateContext("/api/v1/api-keys/" + apiKey);
            var middleware = CreateMiddleware(
                owinContext =>
                {
                    owinContext.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    return Task.CompletedTask;
                },
                _ => { });

            await middleware.Invoke(context);

            var payload = await ReadJsonAsync(context.Response.Body);
            Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
            Assert.Equal("/api/v1/api-keys", (string?)payload["instance"]);
            Assert.DoesNotContain(apiKey, payload.ToString());
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, "forbidden")]
        [InlineData(HttpStatusCode.MethodNotAllowed, "method_not_allowed")]
        [InlineData(HttpStatusCode.UnsupportedMediaType, "unsupported_media_type")]
        public async Task Web_api_errors_are_normalized_to_stable_problem_details(
            HttpStatusCode statusCode,
            string expectedCode)
        {
            using var handler = new ApiProblemDetailsHandler
            {
                InnerHandler = new StaticResponseHandler(statusCode)
            };
            using var invoker = new HttpMessageInvoker(handler);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "http://localhost/api/v1/resource?secret=value");
            request.Headers.TryAddWithoutValidation(
                RequestCorrelationMiddleware.HeaderName,
                "stable-request-id");

            using var response = await invoker.SendAsync(request, CancellationToken.None);
            var payload = JObject.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(statusCode, response.StatusCode);
            Assert.Equal(ApiProblemDetailsFactory.ContentType, response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(expectedCode, (string?)payload["code"]);
            Assert.Equal("/api/v1/resource", (string?)payload["instance"]);
            Assert.Equal("stable-request-id", (string?)payload["traceId"]);
        }

        private static RequestCorrelationMiddleware CreateMiddleware(
            Func<IOwinContext, Task> invoke,
            Action<string> log)
        {
            var terminal = new DelegateMiddleware(invoke);
            var problems = new ApiProblemDetailsMiddleware(terminal, log);
            return new RequestCorrelationMiddleware(problems);
        }

        private static OwinContext CreateContext(string path)
        {
            var context = new OwinContext();
            context.Request.Path = new PathString(path);
            context.Response.Body = new MemoryStream();
            return context;
        }

        private static async Task<JObject> ReadJsonAsync(Stream stream) =>
            JObject.Parse(await ReadTextAsync(stream));

        private static async Task<string> ReadTextAsync(Stream stream)
        {
            stream.Position = 0;
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                true,
                1024,
                leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Application")]

        private sealed class DelegateMiddleware : OwinMiddleware
        {
            private readonly Func<IOwinContext, Task> invoke;

            public DelegateMiddleware(Func<IOwinContext, Task> invoke)
                : base(null)
            {
                this.invoke = invoke;
            }

            public override Task Invoke(IOwinContext context) => invoke(context);
        }

        [Trait("Capability", "Platform")]

        [Trait("Boundary", "Application")]

        private sealed class StaticResponseHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode statusCode;

            public StaticResponseHandler(HttpStatusCode statusCode)
            {
                this.statusCode = statusCode;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
