using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/jobs")]
    public sealed class JobsController : ApiController
    {
        private readonly JobService jobs;

        public JobsController(JobService jobs)
        {
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(JobPageHttpResponse))]
        public HttpResponseMessage List(
            int pageSize = 50,
            string? kind = null,
            string? status = null,
            string? fromUtc = null,
            string? toUtc = null,
            string? cursor = null)
        {
            try
            {
                if (pageSize < 1 || pageSize > 100)
                    return InvalidQuery("invalid_job_query");
                var parsedKind = ParseEnum<JobKind>(kind);
                var parsedStatus = ParseEnum<JobStatus>(status);
                var parsedFrom = ParseUtc(fromUtc);
                var parsedTo = ParseUtc(toUtc);
                if (parsedFrom.HasValue && parsedTo.HasValue && parsedFrom > parsedTo)
                    return InvalidQuery("invalid_job_query");
                var parsedCursor = string.IsNullOrWhiteSpace(cursor)
                    ? null
                    : JobCursorCodec.Decode(cursor!);
                var page = jobs.List(new JobQuery(
                    pageSize,
                    parsedKind,
                    parsedStatus,
                    parsedFrom,
                    parsedTo,
                    parsedCursor));
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new JobPageHttpResponse(
                        page.Items,
                        page.NextCursor == null ? null : JobCursorCodec.Encode(page.NextCursor)));
            }
            catch (FormatException exception)
            {
                return InvalidQuery(exception.Message == "invalid_job_cursor"
                    ? "invalid_job_cursor"
                    : "invalid_job_query");
            }
            catch
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.ServiceUnavailable,
                    "jobs_unavailable",
                    "Jobs are temporarily unavailable.");
            }
        }

        [HttpGet]
        [Route("{jobId:guid}")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage Get(Guid jobId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new JobHttpResponse(jobs.Get(jobId)));
            }
            catch (JobNotFoundException)
            {
                return NotFoundProblem();
            }
        }

        [HttpPost]
        [Route("{jobId:guid}/cancel")]
        [ResponseType(typeof(JobHttpResponse))]
        public HttpResponseMessage Cancel(Guid jobId)
        {
            try
            {
                return Request.CreateResponse(
                    HttpStatusCode.Accepted,
                    new JobHttpResponse(jobs.Cancel(jobId)));
            }
            catch (JobNotFoundException)
            {
                return NotFoundProblem();
            }
            catch (JobNotCancellableException)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.Conflict,
                    JobNotCancellableException.Code,
                    "The job can no longer be cancelled.");
            }
        }

        private HttpResponseMessage NotFoundProblem() =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.NotFound,
                JobNotFoundException.Code,
                "The job was not found.");

        private HttpResponseMessage InvalidQuery(string code) =>
            ApiProblemDetailsFactory.CreateResponse(
                Request,
                HttpStatusCode.BadRequest,
                code,
                "The job query is invalid.");

        private static T? ParseEnum<T>(string? value) where T : struct
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!Enum.TryParse(value, false, out T parsed) ||
                !Enum.IsDefined(typeof(T), parsed))
            {
                throw new FormatException("invalid_job_query");
            }
            return parsed;
        }

        private static DateTimeOffset? ParseUtc(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed) || parsed.Offset != TimeSpan.Zero)
            {
                throw new FormatException("invalid_job_query");
            }
            return parsed;
        }
    }
}
