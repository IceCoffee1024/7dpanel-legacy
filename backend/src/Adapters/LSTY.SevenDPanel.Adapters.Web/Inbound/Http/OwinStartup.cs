using System;
using System.IO;
using System.Web.Http;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.DependencyInjection;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Errors;
using LSTY.SevenDPanel.Adapters.Web.Inbound.Http.OpenApi;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Owin;
using Microsoft.Owin.FileSystems;
using Microsoft.Owin.StaticFiles;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;
using Newtonsoft.Json.Serialization;
using Owin;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public static class OwinStartup
    {
        public static void Configure(
            IAppBuilder app,
            IServiceProvider serviceProvider,
            string? assetRoot = null,
            Action<string>? log = null)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            app.Use<RequestCorrelationMiddleware>();
            app.Use<ApiProblemDetailsMiddleware>(log);
            var authentication = serviceProvider
                .GetRequiredService<PanelHostOptions>()
                .Authentication;
            if (authentication.Enabled)
                app.Use<AuthenticationRateLimitMiddleware>(new AuthenticationAttemptLimiter());
            app.Use<ScopedServiceProviderMiddleware>(serviceProvider);
            ConfigureAuthentication(app, serviceProvider, authentication);
            ConfigureApi(app, serviceProvider);

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

        private static void ConfigureAuthentication(
            IAppBuilder app,
            IServiceProvider serviceProvider,
            PanelAuthenticationOptions authentication)
        {
            if (!authentication.Enabled) return;

            var credentialStore = serviceProvider.GetRequiredService<IPanelCredentialStore>();
            var accessTokenStore = serviceProvider.GetRequiredService<IPanelAccessTokenStore>();
            var apiKeyStore = serviceProvider.GetRequiredService<IPanelApiKeyStore>();
            var verifier = new PanelCredentialVerifier(credentialStore);
            var bearerCredentials = new PersistentBearerCredentialProvider(
                accessTokenStore,
                apiKeyStore,
                credentialStore);

            app.UseOAuthAuthorizationServer(new OAuthAuthorizationServerOptions
            {
                AllowInsecureHttp = authentication.AllowInsecureHttp,
                TokenEndpointPath = new PathString(HttpRoutes.TokenEndpoint),
                AccessTokenExpireTimeSpan = authentication.AccessTokenLifetime,
                AccessTokenFormat = RejectingAuthenticationTicketFormat.Instance,
                AccessTokenProvider = bearerCredentials,
                AuthorizationCodeFormat = RejectingAuthenticationTicketFormat.Instance,
                Provider = new PanelOAuthAuthorizationServerProvider(authentication, verifier),
                RefreshTokenFormat = RejectingAuthenticationTicketFormat.Instance
            });
            app.UseOAuthBearerAuthentication(new OAuthBearerAuthenticationOptions
            {
                AuthenticationMode = AuthenticationMode.Active,
                AccessTokenFormat = RejectingAuthenticationTicketFormat.Instance,
                AccessTokenProvider = bearerCredentials,
                Realm = "7DPanel"
            });
        }

        private static void ConfigureApi(
            IAppBuilder app,
            IServiceProvider serviceProvider)
        {
            var config = new HttpConfiguration();
            config.DependencyResolver = new MicrosoftDependencyResolver(serviceProvider);
            config.MessageHandlers.Insert(0, new OwinScopeBridgingHandler());
            config.MessageHandlers.Add(new ApiProblemDetailsHandler());
            config.MapHttpAttributeRoutes();
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.ContractResolver =
                new CamelCasePropertyNamesContractResolver();
            OpenApiConfiguration.Configure(app);
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

            if (OpenApiRoutes.OwnsPath(normalizedPath))
                return false;

            return string.IsNullOrEmpty(Path.GetExtension(normalizedPath));
        }
    }
}
