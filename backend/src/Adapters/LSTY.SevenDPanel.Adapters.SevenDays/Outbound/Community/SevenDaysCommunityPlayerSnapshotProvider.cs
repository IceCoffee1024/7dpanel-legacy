using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application.Community;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community
{
    public sealed class SevenDaysCommunityPlayerSnapshotProvider :
        ICommunityPlayerCommandSnapshotProvider,
        ICommunityVoteCommandSnapshotProvider
    {
        private static readonly TimeSpan DispatchTimeout = TimeSpan.FromSeconds(5);

        private readonly object sync = new object();
        private readonly Func<IReadOnlyList<SevenDaysCommunityNativePlayer>> capturePlayers;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly Dictionary<string, DateTimeOffset> firstSeenUtc =
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        public SevenDaysCommunityPlayerSnapshotProvider()
            : this(CaptureOnGameThread, () => DateTimeOffset.UtcNow)
        {
        }

        internal SevenDaysCommunityPlayerSnapshotProvider(
            Func<IReadOnlyList<SevenDaysCommunityNativePlayer>> capturePlayers,
            Func<DateTimeOffset> utcClock)
        {
            this.capturePlayers = capturePlayers ?? throw new ArgumentNullException(nameof(capturePlayers));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public CommunityPlayerCommandSnapshot? FindOnlineByCrossplatformId(string crossplatformId)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            var normalized = crossplatformId.Trim();
            return CaptureOnline().SingleOrDefault(player =>
                string.Equals(player.CrossplatformId, normalized, StringComparison.Ordinal));
        }

        public CommunityPlayerCommandSnapshot? ResolveOnline(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return null;
            return Resolve(CaptureOnline(), selector.Trim());
        }

        public IReadOnlyList<CommunityPlayerCommandSnapshot> CaptureOnline()
        {
            var now = utcClock();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("community_player_snapshot_clock_not_utc");
            var nativePlayers = capturePlayers()
                ?? throw new InvalidOperationException("community_player_snapshot_unavailable");
            lock (sync)
            {
                var currentIds = new HashSet<string>(
                    nativePlayers.Select(player => player.CrossplatformId),
                    StringComparer.Ordinal);
                foreach (var departed in firstSeenUtc.Keys
                             .Where(crossplatformId => !currentIds.Contains(crossplatformId))
                             .ToArray())
                {
                    firstSeenUtc.Remove(departed);
                }

                var result = new List<CommunityPlayerCommandSnapshot>(nativePlayers.Count);
                foreach (var native in nativePlayers.OrderBy(
                             player => player.CrossplatformId,
                             StringComparer.Ordinal))
                {
                    if (!firstSeenUtc.TryGetValue(native.CrossplatformId, out var firstSeen))
                    {
                        firstSeen = now;
                        firstSeenUtc.Add(native.CrossplatformId, firstSeen);
                    }

                    result.Add(new CommunityPlayerCommandSnapshot(
                        native.DisplayName,
                        native.Player,
                        now >= firstSeen ? now - firstSeen : TimeSpan.Zero));
                }

                return result;
            }
        }

        public VoteCommandSnapshot Capture(
            VoteKind kind,
            string initiatorCrossplatformId,
            string? targetSelector)
        {
            if (!Enum.IsDefined(typeof(VoteKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(initiatorCrossplatformId))
                throw new ArgumentException("An initiator identity is required.", nameof(initiatorCrossplatformId));
            var players = CaptureOnline();
            var target = targetSelector == null ? null : Resolve(players, targetSelector);
            return new VoteCommandSnapshot(
                target?.CrossplatformId,
                players.Select(player => new VoteEligiblePlayer(
                        player.CrossplatformId,
                        player.OnlineDuration))
                    .ToArray());
        }

        private static CommunityPlayerCommandSnapshot? Resolve(
            IReadOnlyList<CommunityPlayerCommandSnapshot> players,
            string selector)
        {
            var exactIdentity = players.Where(player => string.Equals(
                    player.CrossplatformId,
                    selector,
                    StringComparison.Ordinal))
                .ToArray();
            if (exactIdentity.Length == 1) return exactIdentity[0];
            var displayName = players.Where(player => string.Equals(
                    player.DisplayName,
                    selector,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return displayName.Length == 1 ? displayName[0] : null;
        }

        private static IReadOnlyList<SevenDaysCommunityNativePlayer> CaptureOnGameThread()
        {
            return GameThreadDispatcher.Enqueue(
                    "7DPanel.Community.CapturePlayers",
                    CaptureNativePlayers,
                    DispatchTimeout,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        private static IReadOnlyList<SevenDaysCommunityNativePlayer> CaptureNativePlayers()
        {
            var clients = global::ConnectionManager.Instance?.Clients?.List;
            var world = global::GameManager.Instance?.World;
            var worldId = global::GamePrefs.GetString(global::EnumGamePrefs.GameWorld);
            if (clients == null || world == null || string.IsNullOrWhiteSpace(worldId))
                return Array.Empty<SevenDaysCommunityNativePlayer>();
            if (!world.GetWorldExtent(out var minimum, out var maximum))
                return Array.Empty<SevenDaysCommunityNativePlayer>();

            if (string.Equals(worldId, "Navezgane", StringComparison.Ordinal))
            {
                minimum.x = -2900;
                minimum.z = -2900;
                maximum.x = 2900;
                maximum.z = 2900;
            }
            else if (!global::GameUtils.IsPlaytesting())
            {
                minimum.x += 90;
                minimum.z += 90;
                maximum.x -= 90;
                maximum.z -= 90;
            }

            var bounds = new WorldBounds(minimum.x, maximum.x, minimum.z, maximum.z);
            var isBloodMoon = global::GameUtils.IsBloodMoonTime(
                world.worldTime,
                global::GameUtils.CalcDuskDawnHours(
                    global::GamePrefs.GetInt(global::EnumGamePrefs.DayLightLength)),
                global::GameStats.GetInt(global::EnumGameStats.BloodMoonDay));
            var result = new List<SevenDaysCommunityNativePlayer>();
            foreach (var client in clients)
            {
                var crossplatformId = client?.CrossplatformId?.CombinedString;
                if (client == null || string.IsNullOrWhiteSpace(crossplatformId)) continue;
                var player = world.GetEntity(client.entityId) as global::EntityPlayer;
                if (player == null) continue;
                result.Add(new SevenDaysCommunityNativePlayer(
                    crossplatformId!,
                    string.IsNullOrWhiteSpace(client.playerName)
                        ? crossplatformId!
                        : client.playerName,
                    new TeleportPlayerSnapshot(
                        crossplatformId!,
                        client.entityId,
                        new WorldPosition(
                            worldId!,
                            player.position.x,
                            player.position.y,
                            player.position.z,
                            player.rotation.y),
                        true,
                        !player.IsDead(),
                        player.IsSpawned(),
                        isBloodMoon,
                        false,
                        bounds)));
            }

            return result;
        }
    }

    internal sealed class SevenDaysCommunityNativePlayer
    {
        public SevenDaysCommunityNativePlayer(
            string crossplatformId,
            string displayName,
            TeleportPlayerSnapshot player)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId))
                throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));
            Player = player ?? throw new ArgumentNullException(nameof(player));
            if (!string.Equals(player.CrossplatformId, crossplatformId.Trim(), StringComparison.Ordinal))
                throw new ArgumentException("The player snapshot identity must match.", nameof(player));
            CrossplatformId = crossplatformId.Trim();
            DisplayName = displayName.Trim();
        }

        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public TeleportPlayerSnapshot Player { get; }
    }
}
