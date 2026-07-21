using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysOnlinePlayerQuery : IOnlinePlayerQuery
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);
        private int inFlight;

        private readonly Func<string, Func<OnlinePlayersSnapshot>, TimeSpan, CancellationToken, Task<OnlinePlayersSnapshot>> dispatcher;
        private readonly Func<OnlinePlayersSnapshot> capture;
        private readonly Func<DateTimeOffset> utcClock;

        public SevenDaysOnlinePlayerQuery()
            : this(
                dispatcher: (name, action, timeout, cancellationToken) => GameThreadDispatcher.Enqueue(
                    name,
                    () => action(),
                    timeout,
                    cancellationToken),
                capture: null,
                utcClock: () => DateTimeOffset.UtcNow)
        {
        }

        internal SevenDaysOnlinePlayerQuery(
            Func<string, Func<OnlinePlayersSnapshot>, TimeSpan, CancellationToken, Task<OnlinePlayersSnapshot>> dispatcher,
            Func<OnlinePlayersSnapshot>? capture,
            Func<DateTimeOffset> utcClock)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.capture = capture ?? CaptureSnapshot;
        }

        public async Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.CompareExchange(ref inFlight, 1, 0) != 0)
                throw new OnlinePlayerQueryBusyException();

            try
            {
                var snapshot = await dispatcher(
                    "7DPanel.Players.Online",
                    () => capture(),
                    DispatchTimeout,
                    cancellationToken).ConfigureAwait(false);

                var players = new List<PlayerSnapshot>(snapshot.Players);
                players.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
                return new OnlinePlayersSnapshot(snapshot.CapturedAtUtc, players);
            }
            finally
            {
                Interlocked.Exchange(ref inFlight, 0);
            }
        }

        private OnlinePlayersSnapshot CaptureSnapshot()
        {
            var connectionManager = global::ConnectionManager.Instance;
            if (connectionManager?.Clients?.List == null)
                throw new OnlinePlayerSnapshotUnavailableException();

            var world = global::GameManager.Instance?.World;
            if (world?.Players?.dict == null)
                throw new OnlinePlayerSnapshotUnavailableException();

            var players = new List<PlayerSnapshot>();
            foreach (var client in connectionManager.Clients.List)
            {
                if (client == null || client.entityId < 0)
                    continue;

                if (!world.Players.dict.TryGetValue(client.entityId, out var entityPlayer))
                    continue;

                if (entityPlayer == null)
                    continue;

                var platformIdentity = CreatePlatformIdentity(client.PlatformId);
                var crossplatformIdentity = CreateCrossplatformIdentity(client.CrossplatformId);
                players.Add(new PlayerSnapshot(
                    entityId: client.entityId,
                    name: client.playerName ?? string.Empty,
                    platformIdentity: platformIdentity,
                    crossplatformIdentity: crossplatformIdentity,
                    ping: client.ping,
                        level: RequireLevel(entityPlayer.Progression?.Level),
                    health: (int)entityPlayer.Health));
            }

                    return new OnlinePlayersSnapshot(utcClock(), players);
        }

        private static PlayerPlatformIdentity CreatePlatformIdentity(global::PlatformUserIdentifierAbs platformUserIdentifier)
        {
            if (platformUserIdentifier == null)
                throw new InvalidOperationException("The player's platform identity is unavailable.");

            return CreatePlatformIdentityFromStrings(
                platformUserIdentifier.CombinedString,
                platformUserIdentifier.PlatformIdentifierString);
        }

        internal static PlayerPlatformIdentity CreatePlatformIdentityFromStrings(
            string combinedId,
            string platform)
        {
            if (string.IsNullOrWhiteSpace(combinedId) || string.IsNullOrWhiteSpace(platform))
                throw new InvalidOperationException("The player's platform identity is unavailable.");

            return new PlayerPlatformIdentity(combinedId, platform);
        }

        internal static int RequireLevel(int? level)
        {
            if (!level.HasValue)
                throw new InvalidOperationException("The player's level is unavailable.");

            return level.Value;
        }

        private static PlayerPlatformIdentity? CreateCrossplatformIdentity(global::PlatformUserIdentifierAbs platformUserIdentifier)
        {
            if (platformUserIdentifier == null)
                return null;

            return new PlayerPlatformIdentity(
                platformUserIdentifier.CombinedString,
                platformUserIdentifier.PlatformIdentifierString);
        }
    }
}
