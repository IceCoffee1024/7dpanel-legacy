using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin")]
    [RoutePrefix("api/v1/console/logs")]
    public sealed class ConsoleLogsController : ApiController
    {
        private const int DefaultLimit = 1000;
        private const int MaximumLimit = 5000;
        private readonly IRecentConsoleLogQuery recentConsoleLogs;

        public ConsoleLogsController(IRecentConsoleLogQuery recentConsoleLogs)
        {
            this.recentConsoleLogs = recentConsoleLogs ??
                throw new ArgumentNullException(nameof(recentConsoleLogs));
        }

        [HttpGet]
        [Route("recent")]
        [ResponseType(typeof(RecentConsoleLogsResponse))]
        public HttpResponseMessage GetRecent(int? limit = null)
        {
            if (!ModelState.IsValid || limit is < 1 or > MaximumLimit)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    "invalid_console_log_limit",
                    "The console log limit must be an integer from 1 through 5000.");
            }

            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new RecentConsoleLogsResponse(
                        recentConsoleLogs.ReadRecentConsoleLogs(limit ?? DefaultLimit)));
            }
            catch (RecentConsoleLogsUnavailableException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "console_logs_unavailable",
                    "Recent console logs are unavailable.");
            }
            catch (ObjectDisposedException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "console_logs_unavailable",
                    "Recent console logs are unavailable.");
            }
        }
    }

    public sealed class RecentConsoleLogsResponse
    {
        public RecentConsoleLogsResponse(IReadOnlyList<ConsoleLogEventData> entries)
        {
            Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        }

        public IReadOnlyList<ConsoleLogEventData> Entries { get; }
    }
}
