using System.Web.Http;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    [RoutePrefix("")]
    public sealed class HealthController : ApiController
    {
        [HttpGet]
        [Route("health")]
        [Route("api/v1/health")]
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
