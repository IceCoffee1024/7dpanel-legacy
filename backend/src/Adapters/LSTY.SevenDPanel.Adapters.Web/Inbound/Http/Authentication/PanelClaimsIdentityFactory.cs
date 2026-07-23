using System;
using System.Security.Claims;
using LSTY.SevenDPanel.Hosting.Authentication;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http.Authentication
{
    internal static class PanelClaimsIdentityFactory
    {
        public static ClaimsIdentity Create(
            PanelUserIdentity panelIdentity,
            string authenticationType,
            PanelCredentialType credentialType = PanelCredentialType.AccessToken)
        {
            if (panelIdentity == null) throw new ArgumentNullException(nameof(panelIdentity));
            if (string.IsNullOrEmpty(authenticationType))
                throw new ArgumentException("An authentication type is required.", nameof(authenticationType));

            var identity = new ClaimsIdentity(authenticationType);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, panelIdentity.Subject));
            identity.AddClaim(new Claim(ClaimTypes.Name, panelIdentity.Username));
            identity.AddClaim(new Claim(ClaimTypes.Role, panelIdentity.Role));
            identity.AddClaim(new Claim(
                PanelClaimTypes.CredentialType,
                credentialType == PanelCredentialType.ApiKey ? "api_key" : "access_token"));
            identity.AddClaim(new Claim("identity_source", "sqlite"));
            return identity;
        }
    }
}
