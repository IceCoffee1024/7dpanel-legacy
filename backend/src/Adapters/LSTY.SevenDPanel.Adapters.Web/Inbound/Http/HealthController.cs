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
            return Ok(new HealthResponse
            {
                Status = "ok",
                Product = ProductInfo.Name,
                Version = ProductInfo.Version
            });
        }
    }

    public sealed class HealthResponse
    {
        public string Status { get; set; }
        public string Product { get; set; }
        public string Version { get; set; }
    }
}
