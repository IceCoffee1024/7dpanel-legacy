using System.Web.Http;
using System.Web.Http.Description;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [RoutePrefix("")]
    public sealed class HealthController : ApiController
    {
        [HttpGet]
        [Route("health")]
        [Route("api/v1/health")]
        [ResponseType(typeof(HealthResponse))]
        public IHttpActionResult Get()
        {
            return Ok(new HealthResponse("ok", ProductInfo.Name, ProductInfo.Version));
        }
    }

    public sealed class HealthResponse
    {
        public HealthResponse(string status, string product, string version)
        {
            Status = status;
            Product = product;
            Version = version;
        }

        public string Status { get; }
        public string Product { get; }
        public string Version { get; }
    }
}
