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

            if (!verifier.Verify(context.UserName, context.Password))
            {
                context.SetError("invalid_grant", "The user name or password is incorrect.");
                return Task.CompletedTask;
            }

            var now = DateTimeOffset.UtcNow;
            var identity = new ClaimsIdentity(OAuthDefaults.AuthenticationType);
            identity.AddClaim(new Claim(ClaimTypes.Name, options.Username));
            identity.AddClaim(new Claim(ClaimTypes.Role, "Owner"));
            identity.AddClaim(new Claim("identity_source", "configuration"));
            context.Validated(new AuthenticationTicket(
                identity,
                new AuthenticationProperties
                {
                    IssuedUtc = now,
                    ExpiresUtc = now.Add(options.AccessTokenLifetime)
                }));
            return Task.CompletedTask;
        }
    }
}
