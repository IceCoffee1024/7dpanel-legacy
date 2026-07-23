using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class StoredApiKey
    {
        public StoredApiKey(
            string keyId,
            PanelUserIdentity identity,
            string name,
            DateTimeOffset createdUtc,
            DateTimeOffset? lastUsedUtc,
            DateTimeOffset? expiresUtc,
            DateTimeOffset? revokedUtc,
            DateTimeOffset utcNow)
        {
            if (string.IsNullOrWhiteSpace(keyId))
                throw new ArgumentException("An API Key identifier is required.", nameof(keyId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An API Key name is required.", nameof(name));

            KeyId = keyId;
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            Name = name;
            CreatedUtc = createdUtc.ToUniversalTime();
            LastUsedUtc = lastUsedUtc?.ToUniversalTime();
            ExpiresUtc = expiresUtc?.ToUniversalTime();
            RevokedUtc = revokedUtc?.ToUniversalTime();
            Status = RevokedUtc.HasValue
                ? ApiKeyStatus.Revoked
                : ExpiresUtc.HasValue && ExpiresUtc.Value <= utcNow.ToUniversalTime()
                    ? ApiKeyStatus.Expired
                    : ApiKeyStatus.Active;
        }

        public string KeyId { get; }

        public PanelUserIdentity Identity { get; }

        public string Name { get; }

        public DateTimeOffset CreatedUtc { get; }

        public DateTimeOffset? LastUsedUtc { get; }

        public DateTimeOffset? ExpiresUtc { get; }

        public DateTimeOffset? RevokedUtc { get; }

        public ApiKeyStatus Status { get; }
    }
}