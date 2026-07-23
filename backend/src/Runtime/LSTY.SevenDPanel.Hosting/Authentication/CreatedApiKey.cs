using System;

namespace LSTY.SevenDPanel.Hosting.Authentication
{
    public sealed class CreatedApiKey
    {
        public CreatedApiKey(string apiKey, StoredApiKey metadata)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("An API Key is required.", nameof(apiKey));

            ApiKey = apiKey;
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        public string ApiKey { get; }

        public StoredApiKey Metadata { get; }
    }
}