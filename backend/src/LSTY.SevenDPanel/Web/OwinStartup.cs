using System.Web.Http;
using Owin;

namespace LSTY.SevenDPanel.Web
{
    public static class OwinStartup
    {
        public static void Configure(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            app.UseWebApi(config);
        }
    }
}
