using System;
using System.Security.Claims;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting.Authentication;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Infrastructure;
using Microsoft.Owin.Security.OAuth;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal sealed class PersistentBearerCredentialProvider : AuthenticationTokenProvider
    {
        private const string AccessTokenPrefix = "7dp_";
        private const string ApiKeyPrefix = "7dp_k_";

        private readonly IPanelAccessTokenStore accessTokenStore;
        private readonly IPanelApiKeyStore apiKeyStore;
        private readonly IPanelCredentialStore credentialStore;

        public PersistentBearerCredentialProvider(
            IPanelAccessTokenStore accessTokenStore,
            IPanelApiKeyStore apiKeyStore,
            IPanelCredentialStore credentialStore)
        {
            this.accessTokenStore = accessTokenStore ??
                throw new ArgumentNullException(nameof(accessTokenStore));
            this.apiKeyStore = apiKeyStore ??
                throw new ArgumentNullException(nameof(apiKeyStore));
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
            var role = ticket.Identity.FindFirst(ClaimTypes.Role)?.Value;
            if (string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(role))
            {
                throw new InvalidOperationException(
                    "Access tokens require name identifier, name, and role claims.");
            }
            if (!ticket.Properties.IssuedUtc.HasValue || !ticket.Properties.ExpiresUtc.HasValue)
                throw new InvalidOperationException(
                    "Access tokens require issue and expiration times.");

            return accessTokenStore.Issue(
                new PanelUserIdentity(subject!, username!, role!),
                ticket.Properties.IssuedUtc.Value,
                ticket.Properties.ExpiresUtc.Value);
        }

        internal bool TryReceive(string? credential, out AuthenticationTicket ticket)
        {
            ticket = null!;
            if (credential == null || credential.Length == 0) return false;

            PanelUserIdentity? storedIdentity = null;
            PanelCredentialType credentialType;
            DateTimeOffset? issuedUtc = null;
            DateTimeOffset? expiresUtc = null;
            if (credential.StartsWith(ApiKeyPrefix, StringComparison.Ordinal))
            {
                if (!apiKeyStore.TryValidate(credential, DateTimeOffset.UtcNow, out var storedApiKey) ||
                    storedApiKey == null)
                    return false;

                storedIdentity = storedApiKey!.Identity;
                credentialType = PanelCredentialType.ApiKey;
            }
            else if (credential.StartsWith(AccessTokenPrefix, StringComparison.Ordinal))
            {
                if (!accessTokenStore.TryValidate(credential, DateTimeOffset.UtcNow, out var storedToken) ||
                    storedToken == null)
                    return false;

                storedIdentity = storedToken.Identity;
                credentialType = PanelCredentialType.AccessToken;
                issuedUtc = storedToken.IssuedUtc;
                expiresUtc = storedToken.ExpiresUtc;
            }
            else
            {
                return false;
            }

            if (storedIdentity == null ||
                !credentialStore.TryGetActive(storedIdentity.Subject, out var currentIdentity) ||
                !string.Equals(currentIdentity.Subject, storedIdentity.Subject, StringComparison.Ordinal))
            {
                return false;
            }

            var identity = PanelClaimsIdentityFactory.Create(
                currentIdentity,
                OAuthDefaults.AuthenticationType,
                credentialType);
            ticket = new AuthenticationTicket(
                identity,
                new AuthenticationProperties
                {
                    IssuedUtc = issuedUtc,
                    ExpiresUtc = expiresUtc
                });
            return true;
        }
    }
}