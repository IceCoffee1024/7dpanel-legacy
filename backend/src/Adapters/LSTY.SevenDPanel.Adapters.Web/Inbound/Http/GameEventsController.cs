using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.GameEvents;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/game-events")]
    public sealed class GameEventsController : ApiController
    {
        private readonly IGameEventStore store;
        public GameEventsController(IGameEventStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(GameEventPageHttpResponse))]
        public HttpResponseMessage Get(string? fromUtc = null, string? toUtc = null, string? eventType = null, string? crossplatformId = null, string? limit = null, string? cursor = null)
        {
            if (!TryUtc(fromUtc, out var from) ||
                !TryUtc(toUtc, out var to) ||
                !TryEventType(eventType, out var type) ||
                !TryLimit(limit, out var pageSize))
                return Problem(HttpStatusCode.BadRequest, "invalidGameEventQuery", "The game event query is invalid.");
            var filters = new GameEventCursorFilters(from, to, type, crossplatformId);
            GameEventCursor? keyset = null;
            if (cursor != null && !GameEventCursorCodec.TryDecode(cursor, filters, out keyset)) return Problem(HttpStatusCode.BadRequest, "invalidGameEventCursor", "The game event cursor is invalid.");
            try { return Request.CreateResponse(HttpStatusCode.OK, new GameEventPageHttpResponse(store.Query(new GameEventQuery(pageSize, from, to, type, crossplatformId, keyset)), filters)); }
            catch (ArgumentException) { return Problem(HttpStatusCode.BadRequest, "invalidGameEventQuery", "The game event query is invalid."); }
            catch { return Problem(HttpStatusCode.ServiceUnavailable, "gameEventsUnavailable", "Game events are currently unavailable."); }
        }
        private bool TryUtc(string? text, out DateTimeOffset? value)
        {
            value = null; if (string.IsNullOrWhiteSpace(text)) return true;
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) || parsed.Offset != TimeSpan.Zero) return false;
            value = parsed; return true;
        }
        private static bool TryEventType(string? text, out GameEventType? value)
        {
            value = null; if (string.IsNullOrWhiteSpace(text)) return true;
            if (!Enum.TryParse(text, false, out GameEventType parsed) || !Enum.IsDefined(typeof(GameEventType), parsed)) return false;
            value = parsed; return true;
        }
        private bool TryLimit(string? text, out int value)
        {
            value = 50;
            var queryValues = Request?.GetQueryNameValuePairs()
                .Where(pair => string.Equals(
                    pair.Key,
                    "limit",
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Value)
                .ToArray() ?? Array.Empty<string>();
            if (queryValues.Length > 1) return false;
            var candidate = queryValues.Length == 1 ? queryValues[0] : text;
            if (candidate == null) return true;
            return candidate.Length > 0 &&
                int.TryParse(
                    candidate,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out value) &&
                value >= 1 &&
                value <= 200;
        }
        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) => ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
