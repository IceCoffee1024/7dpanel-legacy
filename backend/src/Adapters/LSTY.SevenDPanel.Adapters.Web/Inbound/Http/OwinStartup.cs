using System;
using System.IO;
using System.Web.Http;
using Microsoft.Owin;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Newtonsoft.Json.Serialization;
using Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public static class OwinStartup
    {
        public static void Configure(IAppBuilder app)
        {
            ConfigureApi(app);
        }

        public static void Configure(IAppBuilder app, string? assetRoot, Action<string>? log = null)
        {
            ConfigureApi(app);

            if (string.IsNullOrWhiteSpace(assetRoot) || !Directory.Exists(assetRoot))
            {
                log?.Invoke("Admin assets are unavailable; expected wwwroot at: " + (assetRoot ?? "<unknown>"));
                return;
            }

            var fileSystem = new PhysicalFileSystem(assetRoot);
            app.Use(async (context, next) =>
            {
                if (!ShouldUseSpaFallback(context.Request.Method, context.Request.Path.Value))
                {
                    await next();
                    return;
                }

                var originalPath = context.Request.Path;
                context.Request.Path = new PathString("/index.html");
                try
                {
                    await next();
                }
                finally
                {
                    context.Request.Path = originalPath;
                }
            });
            app.UseFileServer(new FileServerOptions
            {
                FileSystem = fileSystem,
                EnableDefaultFiles = true,
                EnableDirectoryBrowsing = false
            });
        }

        private static void ConfigureApi(IAppBuilder app)
        {
            var config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            app.UseWebApi(config);
        }

        private static bool ShouldUseSpaFallback(string method, string path)
        {
            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var normalizedPath = string.IsNullOrEmpty(path) ? "/" : path;
            if (string.Equals(normalizedPath, "/api", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(normalizedPath, "/assets", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrEmpty(Path.GetExtension(normalizedPath));
        }
    }
}
