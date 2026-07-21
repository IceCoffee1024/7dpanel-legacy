using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public interface IPanelAccessTokenStore
    {
        string Issue(
            PanelUserIdentity identity,
            DateTimeOffset issuedUtc,
            DateTimeOffset expiresUtc);

        bool TryValidate(
            string token,
            DateTimeOffset utcNow,
            out StoredAccessToken storedToken);
    }
}
