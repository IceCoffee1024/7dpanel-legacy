using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysOnlinePlayerProjection : IOnlinePlayerQuery, IDisposable
    {
        private readonly ConcurrentDictionary<int, OnlinePlayerObservation> players =
            new ConcurrentDictionary<int, OnlinePlayerObservation>();
        private readonly ConcurrentDictionary<int, OnlinePlayerMembership> memberships =
            new ConcurrentDictionary<int, OnlinePlayerMembership>();
        private readonly object updateGate = new object();
        private readonly Func<Action<OnlinePlayerIdentitySource>, IDisposable>? subscribeJoined;
        private readonly Func<Action<global::ClientInfo?, global::PlayerDataFile?>, IDisposable>? subscribeSave;
        private readonly Func<Action, IDisposable>? subscribeSaveForTest;
        private readonly Func<Action<OnlinePlayerIdentitySource>, IDisposable>? subscribeDisconnected;
        private readonly Func<global::ClientInfo?, global::PlayerDataFile?, OnlinePlayerObservation>? copyObservation;
        private readonly Func<OnlinePlayerObservation>? copyObservationForTest;
        private readonly Action<string>? log;
        private readonly PlayerHistoryWriteService? historyWriteService;
        private IDisposable? joinedSubscription;
        private IDisposable? saveSubscription;
        private IDisposable? disconnectedSubscription;
        private int started;
        private int stopped;
        private bool accepting = true;

        internal SevenDaysOnlinePlayerProjection()
        {
        }

        public SevenDaysOnlinePlayerProjection(Action<string>? log = null)
            : this(
                SubscribeJoined,
                SubscribeSave,
                SubscribeDisconnected,
                (client, playerData) => CopyObservation(client, playerData, () => DateTimeOffset.UtcNow),
                log ?? (_ => { }))
        {
        }

        public SevenDaysOnlinePlayerProjection(
            PlayerHistoryWriteService historyWriteService,
            Action<string>? log = null)
            : this(
                SubscribeJoined,
                SubscribeSave,
                SubscribeDisconnected,
                (client, playerData) => CopyObservation(client, playerData, () => DateTimeOffset.UtcNow),
                log ?? (_ => { }))
        {
            this.historyWriteService = historyWriteService ??
                throw new ArgumentNullException(nameof(historyWriteService));
        }

        private SevenDaysOnlinePlayerProjection(
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeJoined,
            Func<Action<global::ClientInfo?, global::PlayerDataFile?>, IDisposable> subscribeSave,
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeDisconnected,
            Func<global::ClientInfo?, global::PlayerDataFile?, OnlinePlayerObservation> copyObservation,
            Action<string> log)
        {
            this.subscribeJoined = subscribeJoined ?? throw new ArgumentNullException(nameof(subscribeJoined));
            this.subscribeSave = subscribeSave ?? throw new ArgumentNullException(nameof(subscribeSave));
            this.subscribeDisconnected = subscribeDisconnected ?? throw new ArgumentNullException(nameof(subscribeDisconnected));
            this.copyObservation = copyObservation ?? throw new ArgumentNullException(nameof(copyObservation));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal SevenDaysOnlinePlayerProjection(
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeJoined,
            Func<Action, IDisposable> subscribeSave,
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeDisconnected,
            Func<OnlinePlayerObservation> copyObservation,
            Action<string> log)
        {
            this.subscribeJoined = subscribeJoined ?? throw new ArgumentNullException(nameof(subscribeJoined));
            subscribeSaveForTest = subscribeSave ?? throw new ArgumentNullException(nameof(subscribeSave));
            this.subscribeDisconnected = subscribeDisconnected ?? throw new ArgumentNullException(nameof(subscribeDisconnected));
            copyObservationForTest = copyObservation ?? throw new ArgumentNullException(nameof(copyObservation));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;

            try
            {
                joinedSubscription = subscribeJoined!(HandleJoined);
                saveSubscription = subscribeSaveForTest != null
                    ? subscribeSaveForTest(HandleSaveForTest)
                    : subscribeSave!(HandleSave);
                disconnectedSubscription = subscribeDisconnected!(HandleDisconnected);
            }
            catch
            {
                try { saveSubscription?.Dispose(); } catch { }
                try { joinedSubscription?.Dispose(); } catch { }
                saveSubscription = null;
                joinedSubscription = null;
                Interlocked.Exchange(ref started, 0);
                throw;
            }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;

            var failures = new List<Exception>();
            try { disconnectedSubscription?.Dispose(); } catch (Exception ex) { failures.Add(ex); }
            try { saveSubscription?.Dispose(); } catch (Exception ex) { failures.Add(ex); }
            try { joinedSubscription?.Dispose(); } catch (Exception ex) { failures.Add(ex); }

            lock (updateGate)
            {
                accepting = false;
                players.Clear();
                memberships.Clear();
            }

            if (failures.Count > 0) throw new AggregateException(failures);
        }

        internal void UpsertForTest(OnlinePlayerObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));

            lock (updateGate)
            {
                if (!accepting) return;

                var player = observation.Player;
                memberships[player.EntityId] = new OnlinePlayerMembership(
                    player.EntityId,
                    player.PlatformIdentity.CombinedId);
                players[player.EntityId] = observation;
            }
        }

        internal void JoinForTest(int entityId, string combinedId)
        {
            lock (updateGate)
            {
                if (!accepting) return;

                memberships[entityId] =
                    new OnlinePlayerMembership(entityId, combinedId);
                if (players.TryGetValue(entityId, out var observation) &&
                    !string.Equals(
                        observation.Player.PlatformIdentity.CombinedId,
                        combinedId,
                        StringComparison.Ordinal))
                {
                    players.TryRemove(entityId, out _);
                }
            }
        }

        public Task<OnlinePlayersSnapshot> GetOnlineAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OnlinePlayerMembership[] membershipSnapshot;
            OnlinePlayerObservation[] observationSnapshot;
            lock (updateGate)
            {
                membershipSnapshot = memberships.Values.ToArray();
                observationSnapshot = players.Values.ToArray();
            }

            var observationsByIdentity = observationSnapshot.ToDictionary(
                observation => new PlayerKey(
                    observation.Player.EntityId,
                    observation.Player.PlatformIdentity.CombinedId));

            var current = membershipSnapshot
                .Select(membership => observationsByIdentity.TryGetValue(
                    new PlayerKey(membership.EntityId, membership.CombinedId),
                    out var observation)
                        ? observation
                        : null)
                .Where(observation => observation != null)
                .Cast<OnlinePlayerObservation>()
                .OrderBy(observation => observation.Player.EntityId)
                .ToArray();

            return Task.FromResult(new OnlinePlayersSnapshot(
                current.Select(observation => observation.Player)));
        }

        public void Dispose()
        {
            Stop();
        }

        private void HandleJoined(OnlinePlayerIdentitySource source)
        {
            if (source == null) return;

            JoinForTest(source.EntityId, source.CombinedId);
        }

        private void HandleSave(global::ClientInfo? client, global::PlayerDataFile? playerData)
        {
            HandleSave(() => copyObservation!(client, playerData));
        }

        private void HandleSaveForTest()
        {
            HandleSave(copyObservationForTest!);
        }

        private void HandleSave(Func<OnlinePlayerObservation> readObservation)
        {
            try
            {
                var observation = readObservation();
                UpsertForTest(observation);
                historyWriteService?.TryRecord(observation.Player);
            }
            catch
            {
                log!("online player save rejected");
            }
        }

        private void HandleDisconnected(OnlinePlayerIdentitySource source)
        {
            if (source == null) return;

            lock (updateGate)
            {
                if (!accepting) return;

                if (memberships.TryGetValue(source.EntityId, out var membership) &&
                    string.Equals(membership.CombinedId, source.CombinedId, StringComparison.Ordinal))
                {
                    ((ICollection<KeyValuePair<int, OnlinePlayerMembership>>)memberships).Remove(
                        new KeyValuePair<int, OnlinePlayerMembership>(source.EntityId, membership));
                }

                if (players.TryGetValue(source.EntityId, out var observation) &&
                    string.Equals(
                        observation.Player.PlatformIdentity.CombinedId,
                        source.CombinedId,
                        StringComparison.Ordinal))
                {
                    ((ICollection<KeyValuePair<int, OnlinePlayerObservation>>)players).Remove(
                        new KeyValuePair<int, OnlinePlayerObservation>(source.EntityId, observation));
                }
            }
        }

        private static IDisposable SubscribeJoined(Action<OnlinePlayerIdentitySource> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerJoinedGameData> callback =
                delegate(ref ModEvents.SPlayerJoinedGameData data)
                {
                    var source = CopyIdentity(data.ClientInfo);
                    if (source != null) handler(source);
                };
            ModEvents.PlayerJoinedGame.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerJoinedGame.UnregisterHandler(callback));
        }

        private static IDisposable SubscribeSave(
            Action<global::ClientInfo?, global::PlayerDataFile?> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SSavePlayerDataData> callback =
                delegate(ref ModEvents.SSavePlayerDataData data)
                {
                    handler(data.ClientInfo, data.PlayerDataFile);
                };
            ModEvents.SavePlayerData.RegisterHandler(callback);
            return new Subscription(() => ModEvents.SavePlayerData.UnregisterHandler(callback));
        }

        private static IDisposable SubscribeDisconnected(Action<OnlinePlayerIdentitySource> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerDisconnectedData> callback =
                delegate(ref ModEvents.SPlayerDisconnectedData data)
                {
                    var source = CopyIdentity(data.ClientInfo);
                    if (source != null) handler(source);
                };
            ModEvents.PlayerDisconnected.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerDisconnected.UnregisterHandler(callback));
        }

        private static OnlinePlayerIdentitySource? CopyIdentity(global::ClientInfo? client)
        {
            if (client == null || client.entityId < 0 || client.PlatformId == null)
                return null;

            return new OnlinePlayerIdentitySource(
                client.entityId,
                RequireIdentityValue(client.PlatformId.CombinedString));
        }

        private static OnlinePlayerObservation CopyObservation(
            global::ClientInfo? client,
            global::PlayerDataFile? playerData,
            Func<DateTimeOffset> utcClock)
        {
            if (client == null) throw new InvalidOperationException("Client information is unavailable.");
            if (playerData == null) throw new InvalidOperationException("Player data is unavailable.");
            if (client.entityId < 0 || client.entityId != playerData.id)
                throw new InvalidOperationException("The player entity identity is invalid.");
            if (playerData.ecd?.stats?.Health == null)
                throw new InvalidOperationException("The player health is unavailable.");

            var entityId = client.entityId;
            var name = RequirePlayerName(playerData.metadata.Name);
            var platformIdentity = CreateIdentity(client.PlatformId);
            var crossplatformIdentity = CreateOptionalIdentity(client.CrossplatformId);
            var deviceType = MapDeviceType(client.device);
            var ip = CopyNullableIp(() => client.ip);
            var ping = client.ping;
            var compatibilityVersion = NormalizeOptionalString(client.compatibilityVersion);
            var discordUserId = FormatDiscordUserId(client.DiscordUserId);
            var permissionLevel = GameManager.Instance.adminTools.Users.GetUserPermissionLevel(client);
            var position = new PlayerPosition(
                playerData.ecd.pos.x,
                playerData.ecd.pos.y,
                playerData.ecd.pos.z);
            var isDead = playerData.bDead;
            var health = TruncateFiniteToInt(playerData.ecd.stats.Health.Value, "health");
            var maxHealth = TruncateFiniteToInt(playerData.ecd.stats.Health.ModifiedMax, "max health");
            var level = playerData.metadata.Level;
            var historyFields = CopyHistoryFields(client, playerData, entityId);
            var score = playerData.score;
            var zombieKills = playerData.zombieKills;
            var playerKills = playerData.playerKills;
            var deaths = playerData.deaths;
            var totalTimePlayedMinutes = RequireNonNegativeFinite(
                playerData.totalTimePlayed,
                "total time played");
            var distanceWalkedMeters = RequireNonNegativeFinite(
                playerData.distanceWalked,
                "distance walked");
            var totalItemsCrafted = playerData.totalItemsCrafted;
            var longestLifeMinutes = RequireNonNegativeFinite(playerData.longestLife, "longest life");
            var currentLifeMinutes = RequireNonNegativeFinite(playerData.currentLife, "current life");
            var observedAtUtc = utcClock();

            var player = new PlayerSnapshot(
                entityId,
                name,
                platformIdentity,
                crossplatformIdentity,
                deviceType,
                ip,
                ping,
                compatibilityVersion,
                discordUserId,
                permissionLevel,
                position,
                isDead,
                health,
                maxHealth,
                level,
                historyFields.PlayGroup,
                historyFields.LastLoginUtc,
                historyFields.GameStage,
                historyFields.ExpToNextLevel,
                historyFields.SkillPoints,
                historyFields.Bedroll,
                score,
                zombieKills,
                playerKills,
                deaths,
                totalTimePlayedMinutes,
                distanceWalkedMeters,
                totalItemsCrafted,
                longestLifeMinutes,
                currentLifeMinutes,
                observedAtUtc);
            return new OnlinePlayerObservation(player, observedAtUtc);
        }

        private static HistoricalPlayerFields CopyHistoryFields(
            global::ClientInfo client,
            global::PlayerDataFile playerData,
            int entityId)
        {
            string? playGroup = null;
            DateTimeOffset? lastLoginUtc = null;
            PlayerPosition? bedroll = null;

            try
            {
                var persistentPlayer = GameManager.Instance?.persistentPlayers?.GetPlayerData(
                    client.CrossplatformId);
                if (persistentPlayer != null)
                {
                    playGroup = NormalizeOptionalString(persistentPlayer.PlayGroup.ToString());
                    lastLoginUtc = ToUtcOrNull(persistentPlayer.LastLogin);
                    if (persistentPlayer.BedrollPos.y != int.MaxValue)
                    {
                        bedroll = new PlayerPosition(
                            persistentPlayer.BedrollPos.x,
                            persistentPlayer.BedrollPos.y,
                            persistentPlayer.BedrollPos.z);
                    }
                }
            }
            catch
            {
            }

            int? gameStage = null;
            int? expToNextLevel = null;
            int? skillPoints = null;
            try
            {
                var player = GameManager.Instance?.World?.GetEntity(entityId) as global::EntityPlayer;
                if (player != null)
                {
                    gameStage = player.gameStage;
                    if (player.Progression != null)
                    {
                        expToNextLevel = player.Progression.ExpToNextLevel;
                        skillPoints = player.Progression.SkillPoints;
                    }
                }
            }
            catch
            {
            }

            if (!expToNextLevel.HasValue || !skillPoints.HasValue)
            {
                try
                {
                    if (TryReadProgressionData(
                        playerData.progressionData,
                        out var fallbackExpToNextLevel,
                        out var fallbackSkillPoints))
                    {
                        expToNextLevel = fallbackExpToNextLevel;
                        skillPoints = fallbackSkillPoints;
                    }
                }
                catch
                {
                }
            }

            return new HistoricalPlayerFields(
                playGroup,
                lastLoginUtc,
                gameStage,
                expToNextLevel,
                skillPoints,
                bedroll);
        }

        internal static bool TryReadProgressionData(
            Stream? progressionData,
            out int expToNextLevel,
            out int skillPoints)
        {
            expToNextLevel = default;
            skillPoints = default;
            if (progressionData == null || !progressionData.CanRead || !progressionData.CanSeek)
                return false;

            long originalPosition;
            try
            {
                originalPosition = progressionData.Position;
            }
            catch
            {
                return false;
            }

            try
            {
                const int targetVersion = 3;
                const int minimumLength = sizeof(int) + sizeof(ushort) + sizeof(int) + sizeof(ushort);
                if (progressionData.Length - originalPosition < minimumLength)
                    return false;

                using (var reader = new BinaryReader(progressionData, Encoding.UTF8, true))
                {
                    if (reader.ReadInt32() != targetVersion)
                        return false;

                    reader.ReadUInt16();
                    expToNextLevel = reader.ReadInt32();
                    skillPoints = reader.ReadUInt16();
                    return true;
                }
            }
            catch
            {
                expToNextLevel = default;
                skillPoints = default;
                return false;
            }
            finally
            {
                try { progressionData.Position = originalPosition; } catch { }
            }
        }

        private static DateTimeOffset? ToUtcOrNull(DateTime value)
        {
            if (value == default)
                return null;

            try
            {
                return new DateTimeOffset(value).ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }

        internal static string? CopyNullableIp(Func<string?> readIp)
        {
            if (readIp == null) throw new ArgumentNullException(nameof(readIp));

            try
            {
                return NormalizeOptionalString(readIp());
            }
            catch
            {
                return null;
            }
        }

        internal static int TruncateFiniteToInt(float value, string fieldName)
        {
            if (!IsFinite(value))
                throw new InvalidOperationException("The player " + fieldName + " is invalid.");

            return checked((int)value);
        }

        internal static string? FormatDiscordUserId(ulong value) =>
            value == 0 ? null : value.ToString(CultureInfo.InvariantCulture);

        private static PlayerDeviceType MapDeviceType(global::ClientInfo.EDeviceType deviceType) =>
            MapDeviceType((int)deviceType);

        internal static PlayerDeviceType MapDeviceType(int deviceType)
        {
            switch (deviceType)
            {
                case (int)global::ClientInfo.EDeviceType.Linux: return PlayerDeviceType.Linux;
                case (int)global::ClientInfo.EDeviceType.Mac: return PlayerDeviceType.Mac;
                case (int)global::ClientInfo.EDeviceType.Windows: return PlayerDeviceType.Windows;
                case (int)global::ClientInfo.EDeviceType.PlayStation: return PlayerDeviceType.PlayStation;
                case (int)global::ClientInfo.EDeviceType.Xbox: return PlayerDeviceType.Xbox;
                default: return PlayerDeviceType.Unknown;
            }
        }

        private static PlayerPlatformIdentity CreateIdentity(global::PlatformUserIdentifierAbs? identity)
        {
            if (identity == null)
                throw new InvalidOperationException("The player platform identity is unavailable.");

            return new PlayerPlatformIdentity(
                RequireIdentityValue(identity.CombinedString),
                RequireIdentityValue(identity.PlatformIdentifierString));
        }

        private static PlayerPlatformIdentity? CreateOptionalIdentity(
            global::PlatformUserIdentifierAbs? identity) =>
            identity == null ? null : CreateIdentity(identity);

        private static string RequirePlayerName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("The player name is unavailable.");

            return value!;
        }

        private static string? NormalizeOptionalString(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private static float RequireNonNegativeFinite(float value, string fieldName)
        {
            if (!IsFinite(value) || value < 0)
                throw new InvalidOperationException("The player " + fieldName + " is invalid.");

            return value;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static string RequireIdentityValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("The player platform identity is unavailable.");

            return value!;
        }

        private sealed class HistoricalPlayerFields
        {
            public HistoricalPlayerFields(
                string? playGroup,
                DateTimeOffset? lastLoginUtc,
                int? gameStage,
                int? expToNextLevel,
                int? skillPoints,
                PlayerPosition? bedroll)
            {
                PlayGroup = playGroup;
                LastLoginUtc = lastLoginUtc;
                GameStage = gameStage;
                ExpToNextLevel = expToNextLevel;
                SkillPoints = skillPoints;
                Bedroll = bedroll;
            }

            public string? PlayGroup { get; }

            public DateTimeOffset? LastLoginUtc { get; }

            public int? GameStage { get; }

            public int? ExpToNextLevel { get; }

            public int? SkillPoints { get; }

            public PlayerPosition? Bedroll { get; }
        }

        private readonly struct PlayerKey : IEquatable<PlayerKey>
        {
            public PlayerKey(int entityId, string combinedId)
            {
                EntityId = entityId;
                CombinedId = combinedId;
            }

            public int EntityId { get; }

            public string CombinedId { get; }

            public bool Equals(PlayerKey other) =>
                EntityId == other.EntityId &&
                string.Equals(CombinedId, other.CombinedId, StringComparison.Ordinal);

            public override bool Equals(object? value) =>
                value is PlayerKey other && Equals(other);

            public override int GetHashCode() =>
                (EntityId * 397) ^ StringComparer.Ordinal.GetHashCode(CombinedId);
        }

        private sealed class Subscription : IDisposable
        {
            private Action? unsubscribe;

            public Subscription(Action unsubscribe)
            {
                this.unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
            }
        }
    }
}
