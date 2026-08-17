using System;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerWebIdentity
    {
        public PlayerWebIdentity(string steamId, string primaryId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(steamId))
                throw new ArgumentException("A Steam identity is required.", nameof(steamId));
            if (string.IsNullOrWhiteSpace(primaryId))
                throw new ArgumentException("A primary player identity is required.", nameof(primaryId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A player display name is required.", nameof(displayName));

            SteamId = steamId.Trim();
            PrimaryId = primaryId.Trim();
            DisplayName = displayName.Trim();
        }

        public string SteamId { get; }
        public string PrimaryId { get; }
        public string DisplayName { get; }
    }

    public interface IPlayerPersistentIdentityLookup
    {
        Task<PlayerWebIdentity?> FindBySteamIdAsync(
            string steamId,
            CancellationToken cancellationToken);
    }
}
