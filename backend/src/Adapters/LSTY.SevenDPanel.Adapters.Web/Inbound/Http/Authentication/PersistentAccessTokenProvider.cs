using System;
using System.Security.Claims;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.Owin.Security.OAuth;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class PersistentAccessTokenProvider : AuthenticationTokenProvider
    {
        private readonly IPanelAccessTokenStore accessTokenStore;
        private readonly IPanelCredentialStore credentialStore;

        public PersistentAccessTokenProvider(
            IPanelAccessTokenStore accessTokenStore,
            IPanelCredentialStore credentialStore)
        {
            this.accessTokenStore = accessTokenStore ??
                throw new ArgumentNullException(nameof(accessTokenStore));
            this.credentialStore = credentialStore ??
                throw new ArgumentNullException(nameof(credentialStore));
        }

        public override Task CreateAsync(AuthenticationTokenCreateContext context)
        {
            context.SetToken(Issue(context.Ticket));
            return Task.CompletedTask;
        }

        public override Task ReceiveAsync(AuthenticationTokenReceiveContext context)
        {
            if (TryReceive(context.Token, out var ticket)) context.SetTicket(ticket);
            return Task.CompletedTask;
        }

        internal string Issue(AuthenticationTicket ticket)
        {
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));

            var subject = ticket.Identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = ticket.Identity.FindFirst(ClaimTypes.Name)?.Value;
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException(
                    "Access tokens require name identifier and name claims.");
            if (!ticket.Properties.IssuedUtc.HasValue || !ticket.Properties.ExpiresUtc.HasValue)
                throw new InvalidOperationException(
                    "Access tokens require issue and expiration times.");

            return accessTokenStore.Issue(
                new PanelUserIdentity(subject!, username!),
                ticket.Properties.IssuedUtc.Value,
                ticket.Properties.ExpiresUtc.Value);
        }

        internal bool TryReceive(string? token, out AuthenticationTicket ticket)
        {
            ticket = null!;
            if (string.IsNullOrEmpty(token) ||
                !accessTokenStore.TryValidate(token!, DateTimeOffset.UtcNow, out var storedToken) ||
                !credentialStore.TryGetActive(
                    storedToken.Identity.Subject,
                    out var currentIdentity) ||
                !string.Equals(
                    currentIdentity.Subject,
                    storedToken.Identity.Subject,
                    StringComparison.Ordinal))
            {
                return false;
            }

            ticket = new AuthenticationTicket(
                PanelClaimsIdentityFactory.Create(
                    currentIdentity,
                    OAuthDefaults.AuthenticationType),
                new AuthenticationProperties
                {
                    IssuedUtc = storedToken.IssuedUtc,
                    ExpiresUtc = storedToken.ExpiresUtc
                });
            return true;
        }
    }
}
