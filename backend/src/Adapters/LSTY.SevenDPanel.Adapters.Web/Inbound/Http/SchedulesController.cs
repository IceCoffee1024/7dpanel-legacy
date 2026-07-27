using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Schedules;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [OwnerAuthorize]
    [RoutePrefix("api/v1/schedules")]
    public sealed class SchedulesController : ApiController
    {
        private readonly ScheduleService schedules;

        public SchedulesController(ScheduleService schedules)
        {
            this.schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(ScheduleHttpResponse[]))]
        public HttpResponseMessage List() => Request.CreateResponse(
            HttpStatusCode.OK,
            schedules.List().Select(item => new ScheduleHttpResponse(item)).ToArray());

        [HttpGet]
        [Route("{scheduleId:guid}")]
        [ResponseType(typeof(ScheduleHttpResponse))]
        public HttpResponseMessage Get(Guid scheduleId) => Execute(
            () => Request.CreateResponse(
                HttpStatusCode.OK,
                new ScheduleHttpResponse(schedules.Get(scheduleId))));

        [HttpPost]
        [Route("")]
        [ResponseType(typeof(ScheduleHttpResponse))]
        public HttpResponseMessage Create(ScheduleWriteHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            return Execute(() => Request.CreateResponse(
                HttpStatusCode.Created,
                new ScheduleHttpResponse(schedules.Create(new CreateScheduleRequest(
                    request.Name ?? string.Empty,
                    request.CronExpression ?? string.Empty,
                    request.TimeZoneId ?? string.Empty,
                    request.Enabled,
                    request.ParseConcurrencyPolicy(),
                    request.ParseAction())))));
        }

        [HttpPut]
        [Route("{scheduleId:guid}")]
        [ResponseType(typeof(ScheduleHttpResponse))]
        public HttpResponseMessage Update(Guid scheduleId, ScheduleWriteHttpRequest? request)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            return Execute(() => Request.CreateResponse(
                HttpStatusCode.OK,
                new ScheduleHttpResponse(schedules.Update(
                    scheduleId,
                    new UpdateScheduleRequest(
                        request.Name ?? string.Empty,
                        request.CronExpression ?? string.Empty,
                        request.TimeZoneId ?? string.Empty,
                        request.Enabled,
                        request.ParseConcurrencyPolicy(),
                        request.ParseAction(),
                        request.RowVersion)))));
        }

        [HttpPost]
        [Route("{scheduleId:guid}/enable")]
        [ResponseType(typeof(ScheduleHttpResponse))]
        public HttpResponseMessage Enable(Guid scheduleId, ScheduleRowVersionHttpRequest? request) =>
            SetEnabled(scheduleId, request, true);

        [HttpPost]
        [Route("{scheduleId:guid}/disable")]
        [ResponseType(typeof(ScheduleHttpResponse))]
        public HttpResponseMessage Disable(Guid scheduleId, ScheduleRowVersionHttpRequest? request) =>
            SetEnabled(scheduleId, request, false);

        [HttpDelete]
        [Route("{scheduleId:guid}")]
        public HttpResponseMessage Delete(Guid scheduleId, long rowVersion) => Execute(() =>
        {
            schedules.Delete(scheduleId, rowVersion);
            return Request.CreateResponse(HttpStatusCode.NoContent);
        });

        private HttpResponseMessage SetEnabled(
            Guid scheduleId,
            ScheduleRowVersionHttpRequest? request,
            bool enabled)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            return Execute(() => Request.CreateResponse(
                HttpStatusCode.OK,
                new ScheduleHttpResponse(enabled
                    ? schedules.Enable(scheduleId, request.RowVersion)
                    : schedules.Disable(scheduleId, request.RowVersion))));
        }

        private HttpResponseMessage Execute(Func<HttpResponseMessage> action)
        {
            try { return action(); }
            catch (ScheduleNotFoundException exception)
            {
                return Problem(HttpStatusCode.NotFound, exception.Code, exception.Message);
            }
            catch (ScheduleConflictException exception)
            {
                return Problem(HttpStatusCode.Conflict, exception.Code, exception.Message);
            }
            catch (ScheduleValidationException exception)
            {
                return Problem(HttpStatusCode.BadRequest, exception.Code, exception.Message);
            }
        }

        private HttpResponseMessage Problem(HttpStatusCode status, string code, string detail) =>
            ApiProblemDetailsFactory.CreateResponse(Request, status, code, detail);
    }
}
