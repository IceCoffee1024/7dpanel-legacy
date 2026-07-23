using System;
using System.Security.Claims;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.OAuth;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class PanelOAuthAuthorizationServerProvider
        : OAuthAuthorizationServerProvider
    {
        private readonly PanelAuthenticationOptions options;
        private readonly PanelCredentialVerifier verifier;

        public PanelOAuthAuthorizationServerProvider(
            PanelAuthenticationOptions options,
            PanelCredentialVerifier verifier)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        }

        public override Task ValidateClientAuthentication(
            OAuthValidateClientAuthenticationContext context)
        {
            context.Validated();
            return Task.CompletedTask;
        }

        public override Task GrantResourceOwnerCredentials(
            OAuthGrantResourceOwnerCredentialsContext context)
        {
            if (!options.AllowInsecureHttp &&
                !string.Equals(
                    context.Request.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
            {
                context.SetError("invalid_request", "HTTPS is required for password authentication.");
                return Task.CompletedTask;
            }

            if (!verifier.TryVerify(
                context.UserName,
                context.Password,
                out var panelIdentity))
            {
                context.SetError("invalid_grant", "The user name or password is incorrect.");
                return Task.CompletedTask;
            }

            var now = DateTimeOffset.UtcNow;
            var identity = PanelClaimsIdentityFactory.Create(
                panelIdentity,
                OAuthDefaults.AuthenticationType);
            context.Validated(new AuthenticationTicket(
                identity,
                new AuthenticationProperties
                {
                    IssuedUtc = now,
                    ExpiresUtc = now.Add(options.AccessTokenLifetime)
                }));
            return Task.CompletedTask;
        }

        public override Task TokenEndpoint(OAuthTokenEndpointContext context)
        {
            if (context.Identity == null) return Task.CompletedTask;

            var username = context.Identity.FindFirst(ClaimTypes.Name)?.Value;
            var role = context.Identity.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
            {
                throw new InvalidOperationException(
                    "The validated panel identity is missing its name or role claim.");
            }

            context.AdditionalResponseParameters["username"] = username;
            context.AdditionalResponseParameters["role"] = role;
            return Task.CompletedTask;
        }
    }
}
