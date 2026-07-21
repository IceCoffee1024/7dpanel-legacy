using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class StoredAccessToken
    {
        public StoredAccessToken(
            PanelUserIdentity identity,
            DateTimeOffset issuedUtc,
            DateTimeOffset expiresUtc)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            IssuedUtc = issuedUtc.ToUniversalTime();
            ExpiresUtc = expiresUtc.ToUniversalTime();
        }

        public PanelUserIdentity Identity { get; }
        public DateTimeOffset IssuedUtc { get; }
        public DateTimeOffset ExpiresUtc { get; }
    }
}
