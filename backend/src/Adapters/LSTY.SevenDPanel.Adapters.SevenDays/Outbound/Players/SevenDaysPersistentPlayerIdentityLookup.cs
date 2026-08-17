using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysPersistentPlayerIdentityLookup : IPlayerPersistentIdentityLookup
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private readonly Func<string, CancellationToken, Task<PlayerWebIdentity?>> lookup;

        public SevenDaysPersistentPlayerIdentityLookup()
            : this(LookupOnGameThreadAsync)
        {
        }

        internal SevenDaysPersistentPlayerIdentityLookup(
            Func<string, CancellationToken, Task<PlayerWebIdentity?>> lookup)
        {
            this.lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        }

        public Task<PlayerWebIdentity?> FindBySteamIdAsync(
            string steamId,
            CancellationToken cancellationToken)
        {
            if (!IsSteamId(steamId))
                throw new ArgumentException("A valid SteamID64 is required.", nameof(steamId));

            return lookup(steamId, cancellationToken);
        }

        internal static bool IsSteamId(string? value) =>
            value != null &&
            value.Length >= 17 &&
            value.Length <= 18 &&
            ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0;

        private static Task<PlayerWebIdentity?> LookupOnGameThreadAsync(
            string steamId,
            CancellationToken cancellationToken) =>
            GameThreadDispatcher.Enqueue(
                "7DPanel.PlayerAuthentication.LookupPersistentPlayer",
                () => CapturePersistentIdentity(steamId),
                DispatchTimeout,
                cancellationToken);

        private static PlayerWebIdentity? CapturePersistentIdentity(string steamId)
        {
            var players = global::GameManager.Instance?.persistentPlayers?.Players;
            if (players == null) return null;

            var expectedNativeId = "Steam_" + steamId;
            PlayerWebIdentity? match = null;
            foreach (var entry in players)
            {
                var player = entry.Value;
                if (player == null || !string.Equals(
                        player.NativeId?.CombinedString,
                        expectedNativeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var primaryId = player.PrimaryId?.CombinedString;
                var displayName = player.PlayerName.SafeDisplayName;
                if (string.IsNullOrWhiteSpace(primaryId) || string.IsNullOrWhiteSpace(displayName))
                    return null;
                if (match != null)
                    return null;

                match = new PlayerWebIdentity(steamId, primaryId!, displayName!);
            }

            return match;
        }
    }
}
