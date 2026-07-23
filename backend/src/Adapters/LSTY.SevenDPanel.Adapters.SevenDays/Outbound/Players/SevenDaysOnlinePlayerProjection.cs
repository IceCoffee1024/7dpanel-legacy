using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
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
        private readonly Func<Action<OnlinePlayerObservation>, IDisposable>? subscribeSave;
        private readonly Func<Action<OnlinePlayerIdentitySource>, IDisposable>? subscribeDisconnected;
        private readonly Func<OnlinePlayerObservation, OnlinePlayerObservation>? copyObservation;
        private readonly Action<string>? log;
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
                observation => observation,
                log ?? (_ => { }))
        {
        }

        internal SevenDaysOnlinePlayerProjection(
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeJoined,
            Func<Action<OnlinePlayerObservation>, IDisposable> subscribeSave,
            Func<Action<OnlinePlayerIdentitySource>, IDisposable> subscribeDisconnected,
            Func<OnlinePlayerObservation, OnlinePlayerObservation> copyObservation,
            Action<string> log)
        {
            this.subscribeJoined = subscribeJoined ?? throw new ArgumentNullException(nameof(subscribeJoined));
            this.subscribeSave = subscribeSave ?? throw new ArgumentNullException(nameof(subscribeSave));
            this.subscribeDisconnected = subscribeDisconnected ?? throw new ArgumentNullException(nameof(subscribeDisconnected));
            this.copyObservation = copyObservation ?? throw new ArgumentNullException(nameof(copyObservation));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return;

            try
            {
                joinedSubscription = subscribeJoined!(HandleJoined);
                saveSubscription = subscribeSave!(HandleSave);
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

        private void HandleSave(OnlinePlayerObservation source)
        {
            try
            {
                var observation = copyObservation!(source);
                UpsertForTest(observation);
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

        private static IDisposable SubscribeSave(Action<OnlinePlayerObservation> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SSavePlayerDataData> callback =
                delegate(ref ModEvents.SSavePlayerDataData data)
                {
                    handler(CopyObservation(data.ClientInfo, data.PlayerDataFile, DateTimeOffset.UtcNow));
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
            DateTimeOffset observedAtUtc)
        {
            if (client == null) throw new InvalidOperationException("Client information is unavailable.");
            if (playerData == null) throw new InvalidOperationException("Player data is unavailable.");
            if (client.entityId < 0 || client.entityId != playerData.id)
                throw new InvalidOperationException("The player entity identity is invalid.");
            if (playerData.ecd?.stats?.Health == null)
                throw new InvalidOperationException("The player health is unavailable.");

            var player = new PlayerSnapshot(
                client.entityId,
                playerData.metadata.Name,
                CreateIdentity(client.PlatformId),
                CreateOptionalIdentity(client.CrossplatformId),
                client.ping,
                playerData.metadata.Level,
                checked((int)playerData.ecd.stats.Health.Value),
                observedAtUtc);
            return new OnlinePlayerObservation(player, observedAtUtc);
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

        private static string RequireIdentityValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("The player platform identity is unavailable.");

            return value!;
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