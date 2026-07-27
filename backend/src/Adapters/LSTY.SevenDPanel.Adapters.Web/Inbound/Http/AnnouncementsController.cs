using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Application.Announcements;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    internal sealed class AnnouncementSenderAuthorizeAttribute : AuthorizeAttribute
    {
        public AnnouncementSenderAuthorizeAttribute() => Roles = "Owner,Admin";

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (actionContext.RequestContext.Principal?.Identity?.IsAuthenticated == true)
            {
                actionContext.Response = ApiProblemDetailsFactory.CreateResponse(
                    actionContext.Request,
                    HttpStatusCode.Forbidden,
                    "announcement_sender_required",
                    "Owner or Admin access is required to send announcements.");
                return;
            }
            base.HandleUnauthorizedRequest(actionContext);
        }
    }

    [AnnouncementSenderAuthorize]
    [RoutePrefix("api/v1/announcements")]
    public sealed class AnnouncementsController : ApiController
    {
        private readonly AnnouncementService announcements;

        public AnnouncementsController(AnnouncementService announcements)
        {
            this.announcements = announcements ??
                throw new ArgumentNullException(nameof(announcements));
        }

        [HttpPost]
        [Route("")]
        [ResponseType(typeof(void))]
        public async Task<HttpResponseMessage> Send(
            AnnouncementHttpRequest? request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid || request == null)
                return ApiProblemDetailsFactory.CreateInvalidRequestBodyResponse(Request);
            try
            {
                await announcements.SendAsync(
                        request.MessageText ?? string.Empty,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Request.CreateResponse(HttpStatusCode.Accepted);
            }
            catch (AnnouncementValidationException exception)
            {
                return ApiProblemDetailsFactory.CreateResponse(
                    Request,
                    HttpStatusCode.BadRequest,
                    exception.Code,
                    exception.Message);
            }
        }
    }
}
