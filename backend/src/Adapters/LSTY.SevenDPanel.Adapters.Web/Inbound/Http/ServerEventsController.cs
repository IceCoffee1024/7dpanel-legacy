using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/events")]
    public sealed class ServerEventsController : ApiController
    {
        private readonly ServerEventSseSession session;

        public ServerEventsController(ServerEventSseSession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
        }

        [HttpGet]
        [Route("stream")]
        public HttpResponseMessage Get(CancellationToken cancellationToken)
        {
            if (!TryReadLastEventId(out var afterSequence))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "invalid_event_cursor",
                    "Last-Event-ID must be a non-negative integer.");
            }

            if (!TryReadAuthorization(out var subject, out var bearerToken) ||
                !session.TryAuthorize(subject, bearerToken))
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Unauthorized,
                    "authentication_invalid",
                    "The current authentication is no longer valid.");
            }

            if (!session.TryReserve())
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "stream_capacity_exhausted",
                    "The server event stream has reached its connection limit.");
            }

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            response.Headers.TryAddWithoutValidation("X-Accel-Buffering", "no");
            response.Content = new PushStreamContent(
                (stream, content, context) => session.WriteAsync(
                    stream,
                    afterSequence,
                    cancellationToken),
                "text/event-stream");
            return response;
        }

        private bool TryReadAuthorization(out string subject, out string? bearerToken)
        {
            subject = string.Empty;
            bearerToken = null;
            if (!(User?.Identity is ClaimsIdentity identity)) return false;

            subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
            if (subject.Length == 0) return false;

            var authorization = Request.Headers.Authorization;
            if (authorization != null &&
                string.Equals(
                    authorization.Scheme,
                    "Bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                bearerToken = authorization.Parameter;
                if (string.IsNullOrWhiteSpace(bearerToken)) return false;
            }

            return true;
        }

        private bool TryReadLastEventId(out long? afterSequence)
        {
            afterSequence = null;
            if (!Request.Headers.TryGetValues("Last-Event-ID", out IEnumerable<string>? values))
                return true;

            var value = values.FirstOrDefault();
            if (!long.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsed) ||
                parsed < 0)
            {
                return false;
            }

            afterSequence = parsed;
            return true;
        }
    }
}
