using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class BasicAuthenticationHandler
        : AuthenticationHandler<BasicAuthenticationOptions>
    {
        protected override Task<AuthenticationTicket?> AuthenticateCoreAsync()
        {
            if (!Options.AllowInsecureHttp &&
                !string.Equals(Request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<AuthenticationTicket?>(null);
            }

            var header = Request.Headers.Get("Authorization");
            if (string.IsNullOrEmpty(header) ||
                !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) ||
                !TryDecodeCredentials(header.Substring("Basic ".Length).Trim(), out var username, out var password) ||
                !Options.Verifier(username, password))
            {
                return Task.FromResult<AuthenticationTicket?>(null);
            }

            var identity = new ClaimsIdentity(Options.AuthenticationType);
            identity.AddClaim(new Claim(ClaimTypes.Name, username));
            identity.AddClaim(new Claim(ClaimTypes.Role, "Owner"));
            identity.AddClaim(new Claim("identity_source", "configuration"));
            return Task.FromResult<AuthenticationTicket?>(
                new AuthenticationTicket(identity, new AuthenticationProperties()));
        }

        protected override Task ApplyResponseChallengeAsync()
        {
            if (Response.StatusCode == (int)HttpStatusCode.Unauthorized &&
                Helper.LookupChallenge(Options.AuthenticationType, Options.AuthenticationMode) != null)
            {
                Response.Headers.AppendValues(
                    "WWW-Authenticate",
                    "Basic realm=\"" + Options.Realm + "\", charset=\"UTF-8\"");
            }

            return Task.CompletedTask;
        }

        internal static bool TryDecodeCredentials(
            string encoded,
            out string username,
            out string password)
        {
            username = string.Empty;
            password = string.Empty;
            if (string.IsNullOrEmpty(encoded)) return false;

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var separator = decoded.IndexOf(':');
                if (separator <= 0) return false;
                username = decoded.Substring(0, separator);
                password = decoded.Substring(separator + 1);
                return username.Trim().Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
