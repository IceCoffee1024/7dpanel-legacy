using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [Authorize(Roles = "Owner,Admin,Viewer")]
    [RoutePrefix("api/v1/overview")]
    public sealed class OverviewController : ApiController
    {
        private readonly GetOverviewUseCase useCase;

        public OverviewController(GetOverviewUseCase useCase)
        {
            this.useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        }

        [HttpGet]
        [Route("")]
        [ResponseType(typeof(OverviewHttpResponse))]
        public async Task<HttpResponseMessage> Get(CancellationToken cancellationToken)
        {
            var isOwner = HasOwnerRole(User as ClaimsPrincipal);
            var snapshot = await useCase.ExecuteAsync(
                    isOwner ? OverviewAudience.Owner : OverviewAudience.NonOwner,
                    cancellationToken)
                .ConfigureAwait(false);
            return Request.CreateResponse(
                HttpStatusCode.OK,
                OverviewHttpResponse.FromSnapshot(snapshot, isOwner));
        }

        private static bool HasOwnerRole(ClaimsPrincipal? principal)
        {
            return principal?.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role &&
                string.Equals(claim.Value, "Owner", StringComparison.Ordinal)) == true;
        }
    }
}
