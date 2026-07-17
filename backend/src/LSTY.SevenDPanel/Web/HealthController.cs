using System.Reflection;
using System.Web.Http;

namespace LSTY.SevenDPanel.Web
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
                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString()
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
