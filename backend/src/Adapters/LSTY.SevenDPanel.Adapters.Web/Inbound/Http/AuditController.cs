using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/audit")]
    public sealed class AuditController : ApiController
    {
        private static readonly System.Collections.Generic.HashSet<string> Statuses =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            "Pending", "Started", "Succeeded", "Failed", "Unknown", "Completed", "Threw"
        };

        private readonly IUnifiedAuditQuery query;

        public AuditController(IUnifiedAuditQuery query)
        {
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(AuditPageHttpResponse))]
        public HttpResponseMessage Get(
            string? fromUtc = null,
            string? toUtc = null,
            string? actor = null,
            string? target = null,
            string? action = null,
            string? sourceKind = null,
            string? status = null,
            string? limit = null,
            string? cursor = null)
        {
            if (!TryParseCursor(cursor, out var parsedCursor))
            {
                return Problem(HttpStatusCode.BadRequest, "invalidAuditCursor", "The audit cursor is invalid.");
            }

            if (!TryCreateFilter(
                    fromUtc,
                    toUtc,
                    actor,
                    target,
                    action,
                    sourceKind,
                    status,
                    limit,
                    parsedCursor,
                    out var filter))
            {
                return Problem(HttpStatusCode.BadRequest, "invalidAuditQuery", "The audit query is invalid.");
            }

            try
            {
                return Request.CreateResponse(HttpStatusCode.OK, new AuditPageHttpResponse(query.Query(filter!)));
            }
            catch (Exception)
            {
                return Problem(HttpStatusCode.ServiceUnavailable, "auditUnavailable", "Audit entries are unavailable.");
            }
        }

        private static bool TryCreateFilter(
            string? fromText,
            string? toText,
            string? actor,
            string? target,
            string? action,
            string? sourceKind,
            string? status,
            string? limitText,
            UnifiedAuditCursor? cursor,
            out UnifiedAuditFilter? filter)
        {
            filter = null;
            if (!TryParseUtc(fromText, out var fromUtc) ||
                !TryParseUtc(toText, out var toUtc) ||
                !TryParseLimit(limitText, out var limit) ||
                (sourceKind != null && !AuditCursorCodec.IsSupportedSourceKind(sourceKind)) ||
                (status != null && !Statuses.Contains(status)))
            {
                return false;
            }

            try
            {
                filter = new UnifiedAuditFilter(limit, fromUtc, toUtc, actor, target, action, sourceKind, status, cursor);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool TryParseUtc(string? value, out DateTimeOffset? result)
        {
            result = null;
            if (value == null) return true;
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed) ||
                parsed.Offset != TimeSpan.Zero)
            {
                return false;
            }
            result = parsed;
            return true;
        }

        private static bool TryParseLimit(string? value, out int limit)
        {
            limit = 50;
            return value == null ||
                (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out limit) && limit >= 1 && limit <= 200);
        }

        private static bool TryParseCursor(string? value, out UnifiedAuditCursor? cursor)
        {
            cursor = null;
            return value == null || AuditCursorCodec.TryDecode(value, out cursor);
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail, "/api/v1/audit");
    }
}
