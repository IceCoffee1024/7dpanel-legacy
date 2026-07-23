using System;
using System.Collections.Generic;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public interface IPanelApiKeyStore
    {
        ApiKeyCreateResult Create(
            string subject,
            string name,
            DateTimeOffset createdUtc,
            DateTimeOffset? expiresUtc);

        IReadOnlyList<StoredApiKey> List(string subject, DateTimeOffset utcNow);

        bool Revoke(string subject, string keyId, DateTimeOffset revokedUtc);

        bool TryValidate(string apiKey, DateTimeOffset utcNow, out StoredApiKey storedApiKey);
    }
}