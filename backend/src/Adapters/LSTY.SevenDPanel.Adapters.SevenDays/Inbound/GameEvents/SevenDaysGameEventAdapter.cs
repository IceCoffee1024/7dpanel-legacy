using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents;
using LSTY.SevenDPanel.Application.GameEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.GameEvents
{
    internal readonly struct GameEventEntitySnapshot
    {
        public GameEventEntitySnapshot(GameEventSubject? subject, bool isPlayer)
        {
            Subject = subject;
            IsPlayer = isPlayer;
        }

        public GameEventSubject? Subject { get; }
        public bool IsPlayer { get; }
    }

    internal interface ISevenDaysGameEventSources
    {
        IDisposable SubscribePlayerJoined(Action<GameEventSubject?> handler);
        IDisposable SubscribePlayerDisconnected(Action<GameEventSubject?, bool> handler);
        IDisposable SubscribeEntityKilled(
            Action<GameEventEntitySnapshot, GameEventEntitySnapshot> handler);
    }

    internal sealed class SevenDaysModGameEventSources : ISevenDaysGameEventSources
    {
        private readonly Func<int, GameEventSubject?> currentPlayerLookup;

        public SevenDaysModGameEventSources()
            : this(LookupCurrentPlayer)
        {
        }

        internal SevenDaysModGameEventSources(Func<int, GameEventSubject?> currentPlayerLookup)
        {
            this.currentPlayerLookup = currentPlayerLookup ??
                throw new ArgumentNullException(nameof(currentPlayerLookup));
        }

        public IDisposable SubscribePlayerJoined(Action<GameEventSubject?> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerJoinedGameData> callback =
                delegate(ref ModEvents.SPlayerJoinedGameData data)
                {
                    handler(Copy(data.ClientInfo));
                };
            ModEvents.PlayerJoinedGame.RegisterHandler(callback);
            return new CallbackSubscription(
                () => ModEvents.PlayerJoinedGame.UnregisterHandler(callback));
        }

        public IDisposable SubscribePlayerDisconnected(
            Action<GameEventSubject?, bool> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerDisconnectedData> callback =
                delegate(ref ModEvents.SPlayerDisconnectedData data)
                {
                    handler(Copy(data.ClientInfo), data.GameShuttingDown);
                };
            ModEvents.PlayerDisconnected.RegisterHandler(callback);
            return new CallbackSubscription(
                () => ModEvents.PlayerDisconnected.UnregisterHandler(callback));
        }

        public IDisposable SubscribeEntityKilled(
            Action<GameEventEntitySnapshot, GameEventEntitySnapshot> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ModEvents.ModEventHandlerDelegate<ModEvents.SEntityKilledData> callback =
                delegate(ref ModEvents.SEntityKilledData data)
                {
                    handler(Copy(data.KillingEntity), Copy(data.KilledEntitiy));
                };
            ModEvents.EntityKilled.RegisterHandler(callback);
            return new CallbackSubscription(
                () => ModEvents.EntityKilled.UnregisterHandler(callback));
        }

        internal static GameEventEntitySnapshot CreateEntitySnapshot(
            int? entityId,
            string? entityName,
            bool isPlayer,
            Func<int, GameEventSubject?> currentPlayerLookup)
        {
            if (currentPlayerLookup == null)
                throw new ArgumentNullException(nameof(currentPlayerLookup));
            var current = entityId.HasValue
                ? currentPlayerLookup(entityId.Value)
                : null;
            var subject = current == null
                ? new GameEventSubject(null, null, entityId, entityName)
                : new GameEventSubject(
                    current.CrossplatformId,
                    current.PlatformId,
                    entityId,
                    current.DisplayName ?? entityName);
            return new GameEventEntitySnapshot(subject, isPlayer);
        }

        private GameEventEntitySnapshot Copy(global::Entity? entity)
        {
            if (entity == null) return new GameEventEntitySnapshot(null, false);
            var entityId = entity.entityId >= 0 ? entity.entityId : (int?)null;
            var entityName = entity is global::EntityAlive alive
                ? alive.EntityName
                : entity.GetType().Name;
            return CreateEntitySnapshot(
                entityId,
                entityName,
                entity is global::EntityPlayer,
                currentPlayerLookup);
        }

        private static GameEventSubject? LookupCurrentPlayer(int entityId)
        {
            try
            {
                return Copy(ConnectionManager.Instance.Clients.ForEntityId(entityId));
            }
            catch
            {
                return null;
            }
        }

        private static GameEventSubject? Copy(global::ClientInfo? client) =>
            client == null
                ? null
                : new GameEventSubject(
                    client.CrossplatformId?.CombinedString,
                    client.PlatformId?.CombinedString,
                    client.entityId >= 0 ? client.entityId : (int?)null,
                    client.playerName);

        private sealed class CallbackSubscription : IDisposable
        {
            private Action? unsubscribe;
            public CallbackSubscription(Action unsubscribe) =>
                this.unsubscribe = unsubscribe;
            public void Dispose() =>
                Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }

    public sealed class SevenDaysGameEventAdapter
    {
        private readonly GameEventWriteService writer;
        private readonly ISevenDaysGameEventSources sources;
        private readonly Func<DateTimeOffset> utcClock;

        public SevenDaysGameEventAdapter(
            GameEventWriteService writer,
            Func<DateTimeOffset>? utcClock = null)
            : this(writer, new SevenDaysModGameEventSources(), utcClock ?? (() => DateTimeOffset.UtcNow))
        {
        }

        internal SevenDaysGameEventAdapter(
            GameEventWriteService writer,
            ISevenDaysGameEventSources sources,
            Func<DateTimeOffset> utcClock)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.sources = sources ?? throw new ArgumentNullException(nameof(sources));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public IDisposable Subscribe()
        {
            IDisposable? joined = null;
            IDisposable? disconnected = null;
            IDisposable? killed = null;
            try
            {
                joined = sources.SubscribePlayerJoined(RecordPlayerJoined);
                disconnected = sources.SubscribePlayerDisconnected(RecordPlayerDisconnected);
                killed = sources.SubscribeEntityKilled(RecordEntityKilled);
                return new CompositeSubscription(killed, disconnected, joined);
            }
            catch
            {
                TryDispose(killed);
                TryDispose(disconnected);
                TryDispose(joined);
                throw;
            }
        }

        private void RecordPlayerJoined(GameEventSubject? subject)
        {
            var observedAtUtc = utcClock();
            writer.TryRecord(GameEventRecord.Create(
                GameEventType.PlayerJoined,
                observedAtUtc,
                observedAtUtc,
                subject,
                null,
                null));
        }

        private void RecordPlayerDisconnected(
            GameEventSubject? subject,
            bool gameShuttingDown)
        {
            var observedAtUtc = utcClock();
            writer.TryRecord(GameEventRecord.Create(
                GameEventType.PlayerLeft,
                observedAtUtc,
                observedAtUtc,
                subject,
                null,
                gameShuttingDown));
        }

        private void RecordEntityKilled(
            GameEventEntitySnapshot killer,
            GameEventEntitySnapshot victim)
        {
            var occurredAtUtc = utcClock();
            if (killer.IsPlayer)
            {
                writer.TryRecord(GameEventRecord.Create(
                    GameEventType.PlayerKilledEntity,
                    occurredAtUtc,
                    occurredAtUtc,
                    killer.Subject,
                    victim.Subject,
                    null));
            }
            if (victim.IsPlayer)
            {
                writer.TryRecord(GameEventRecord.Create(
                    GameEventType.PlayerDied,
                    occurredAtUtc,
                    occurredAtUtc,
                    killer.Subject,
                    victim.Subject,
                    null));
            }
        }

        private static void TryDispose(IDisposable? subscription)
        {
            try { subscription?.Dispose(); }
            catch { }
        }

        private sealed class CompositeSubscription : IDisposable
        {
            private IDisposable[]? subscriptions;

            public CompositeSubscription(params IDisposable[] subscriptions)
            {
                this.subscriptions = subscriptions;
            }

            public void Dispose()
            {
                var current = Interlocked.Exchange(ref subscriptions, null);
                if (current == null) return;
                var failures = new List<Exception>();
                foreach (var subscription in current)
                {
                    try { subscription.Dispose(); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }
    }
}
