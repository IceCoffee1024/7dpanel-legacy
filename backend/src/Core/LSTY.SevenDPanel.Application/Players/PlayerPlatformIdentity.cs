using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerPlatformIdentity
    {
        public PlayerPlatformIdentity(string combinedId, string platform)
        {
            if (string.IsNullOrWhiteSpace(combinedId))
                throw new ArgumentException("A platform identity is required.", nameof(combinedId));

            if (string.IsNullOrWhiteSpace(platform))
                throw new ArgumentException("A platform name is required.", nameof(platform));

            CombinedId = combinedId;
            Platform = platform;
        }

        public string CombinedId { get; }

        public string Platform { get; }
    }
}
