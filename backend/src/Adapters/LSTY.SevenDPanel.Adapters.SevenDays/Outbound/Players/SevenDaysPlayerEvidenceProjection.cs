using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class SevenDaysPlayerEvidenceProjection : IDisposable
    {
        private readonly Func<Action<PlayerEvidenceIdentitySource?>, IDisposable> subscribeJoined;
        private readonly Func<Action<PlayerEvidenceDraft?>, IDisposable> subscribeSave;
        private readonly Func<Action<PlayerEvidenceIdentitySource?>, IDisposable> subscribeDisconnected;
        private readonly Func<PlayerEvidenceDraft, bool> tryRecord;
        private readonly PanelPlayerEvidenceOptions options;
        private readonly Func<string> worldId;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly object sync = new object();
        private readonly Dictionary<string, OpenSession> sessions =
            new Dictionary<string, OpenSession>(StringComparer.Ordinal);
        private IDisposable? joinedSubscription;
        private IDisposable? saveSubscription;
        private IDisposable? disconnectedSubscription;
        private long sessionSequence;
        private int started;
        private int stopped;

        public SevenDaysPlayerEvidenceProjection(
            PlayerEvidenceWriteService writer,
            PanelPlayerEvidenceOptions options,
            SevenDaysPlayerEvidenceSnapshotReader snapshotReader)
            : this(
                SubscribeJoined,
                handler => SubscribeSave(snapshotReader, handler),
                SubscribeDisconnected,
                writer == null
                    ? throw new ArgumentNullException(nameof(writer))
                    : (Func<PlayerEvidenceDraft, bool>)writer.TryRecord,
                options,
                () => GamePrefs.GetString(EnumGamePrefs.GameWorld),
                () => DateTimeOffset.UtcNow)
        {
            if (snapshotReader == null) throw new ArgumentNullException(nameof(snapshotReader));
        }

        internal SevenDaysPlayerEvidenceProjection(
            Func<Action<PlayerEvidenceIdentitySource?>, IDisposable> subscribeJoined,
            Func<Action<PlayerEvidenceDraft?>, IDisposable> subscribeSave,
            Func<Action<PlayerEvidenceIdentitySource?>, IDisposable> subscribeDisconnected,
            Func<PlayerEvidenceDraft, bool> tryRecord,
            PanelPlayerEvidenceOptions options,
            Func<string> worldId,
            Func<DateTimeOffset> utcClock)
        {
            this.subscribeJoined = subscribeJoined ?? throw new ArgumentNullException(nameof(subscribeJoined));
            this.subscribeSave = subscribeSave ?? throw new ArgumentNullException(nameof(subscribeSave));
            this.subscribeDisconnected = subscribeDisconnected ??
                throw new ArgumentNullException(nameof(subscribeDisconnected));
            this.tryRecord = tryRecord ?? throw new ArgumentNullException(nameof(tryRecord));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.worldId = worldId ?? throw new ArgumentNullException(nameof(worldId));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
        }

        public void Start()
        {
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0 ||
                Volatile.Read(ref stopped) != 0)
                return;
            try
            {
                joinedSubscription = subscribeJoined(HandleJoined);
                saveSubscription = subscribeSave(HandleSave);
                disconnectedSubscription = subscribeDisconnected(HandleDisconnected);
            }
            catch
            {
                TryDispose(disconnectedSubscription);
                TryDispose(saveSubscription);
                TryDispose(joinedSubscription);
                disconnectedSubscription = null;
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
            Dispose(disconnectedSubscription, failures);
            Dispose(saveSubscription, failures);
            Dispose(joinedSubscription, failures);
            lock (sync) sessions.Clear();
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose() => Stop();

        private void HandleJoined(PlayerEvidenceIdentitySource? source)
        {
            var crossplatformId = source?.CombinedId;
            if (crossplatformId == null || Volatile.Read(ref stopped) != 0) return;
            var observedAtUtc = ReadUtc();
            var currentWorldId = PlayerEvidenceDraft.RequireText(worldId(), "worldId");
            OpenSession session;
            lock (sync)
            {
                session = new OpenSession(
                    Interlocked.Increment(ref sessionSequence),
                    observedAtUtc,
                    currentWorldId,
                    PlayerProfileSectionState.Available,
                    null);
                sessions[crossplatformId] = session;
            }
            tryRecord(new PlayerEvidenceDraft(
                crossplatformId,
                options.ServerId,
                currentWorldId,
                observedAtUtc,
                session.ToDraft(null, null),
                new PlayerEvidenceActivityDraft(
                    "Joined",
                    null,
                    PlayerProfileSectionState.Available),
                null,
                null,
                null));
        }

        private void HandleSave(PlayerEvidenceDraft? observation)
        {
            if (observation == null || Volatile.Read(ref stopped) != 0) return;
            PlayerEvidenceSessionDraft? sessionDraft = null;
            PlayerProfileSectionState completeness;
            lock (sync)
            {
                if (!sessions.TryGetValue(observation.CrossplatformId, out var session))
                {
                    session = new OpenSession(
                        Interlocked.Increment(ref sessionSequence),
                        observation.ObservedAtUtc,
                        observation.WorldId,
                        PlayerProfileSectionState.Partial,
                        observation.Position);
                    sessions[observation.CrossplatformId] = session;
                    sessionDraft = session.ToDraft(null, null);
                }
                else
                {
                    session = session.WithPosition(observation.Position);
                    sessions[observation.CrossplatformId] = session;
                }
                completeness = session.Completeness;
            }
            tryRecord(observation.WithBoundary(
                sessionDraft,
                new PlayerEvidenceActivityDraft("Saved", null, completeness)));
        }

        private void HandleDisconnected(PlayerEvidenceIdentitySource? source)
        {
            var crossplatformId = source?.CombinedId;
            if (crossplatformId == null || Volatile.Read(ref stopped) != 0) return;
            var observedAtUtc = ReadUtc();
            OpenSession session;
            lock (sync)
            {
                if (!sessions.TryGetValue(crossplatformId, out session))
                {
                    session = new OpenSession(
                        Interlocked.Increment(ref sessionSequence),
                        observedAtUtc,
                        PlayerEvidenceDraft.RequireText(worldId(), "worldId"),
                        PlayerProfileSectionState.Partial,
                        null);
                }
                sessions.Remove(crossplatformId);
            }
            tryRecord(new PlayerEvidenceDraft(
                crossplatformId,
                options.ServerId,
                session.WorldId,
                observedAtUtc,
                session.ToDraft(observedAtUtc, "Disconnected"),
                new PlayerEvidenceActivityDraft(
                    "Disconnected",
                    null,
                    session.Completeness),
                null,
                null,
                session.LastPosition));
        }

        private DateTimeOffset ReadUtc()
        {
            var value = utcClock();
            PlayerEvidenceDraft.RequireUtc(value, "observedAtUtc");
            return value;
        }

        private static IDisposable SubscribeJoined(Action<PlayerEvidenceIdentitySource?> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerJoinedGameData> callback =
                delegate(ref ModEvents.SPlayerJoinedGameData data)
                {
                    handler(CopyIdentity(data.ClientInfo));
                };
            ModEvents.PlayerJoinedGame.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerJoinedGame.UnregisterHandler(callback));
        }

        private static IDisposable SubscribeSave(
            SevenDaysPlayerEvidenceSnapshotReader reader,
            Action<PlayerEvidenceDraft?> handler)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            ModEvents.ModEventHandlerDelegate<ModEvents.SSavePlayerDataData> callback =
                delegate(ref ModEvents.SSavePlayerDataData data)
                {
                    handler(reader.Read(data.ClientInfo, data.PlayerDataFile));
                };
            ModEvents.SavePlayerData.RegisterHandler(callback);
            return new Subscription(() => ModEvents.SavePlayerData.UnregisterHandler(callback));
        }

        private static IDisposable SubscribeDisconnected(Action<PlayerEvidenceIdentitySource?> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerDisconnectedData> callback =
                delegate(ref ModEvents.SPlayerDisconnectedData data)
                {
                    handler(CopyIdentity(data.ClientInfo));
                };
            ModEvents.PlayerDisconnected.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerDisconnected.UnregisterHandler(callback));
        }

        private static PlayerEvidenceIdentitySource? CopyIdentity(global::ClientInfo? client)
        {
            if (client == null) return null;
            return new PlayerEvidenceIdentitySource(client.CrossplatformId?.CombinedString);
        }

        private static void TryDispose(IDisposable? subscription)
        {
            try { subscription?.Dispose(); } catch { }
        }

        private static void Dispose(IDisposable? subscription, ICollection<Exception> failures)
        {
            try { subscription?.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }

        private readonly struct OpenSession
        {
            public OpenSession(
                long sessionId,
                DateTimeOffset startedAtUtc,
                string worldId,
                PlayerProfileSectionState completeness,
                PlayerPosition? lastPosition)
            {
                SessionId = sessionId;
                StartedAtUtc = startedAtUtc;
                WorldId = worldId;
                Completeness = completeness;
                LastPosition = lastPosition;
            }

            public long SessionId { get; }
            public DateTimeOffset StartedAtUtc { get; }
            public string WorldId { get; }
            public PlayerProfileSectionState Completeness { get; }
            public PlayerPosition? LastPosition { get; }

            public OpenSession WithPosition(PlayerPosition? position) =>
                new OpenSession(
                    SessionId,
                    StartedAtUtc,
                    WorldId,
                    Completeness,
                    position ?? LastPosition);

            public PlayerEvidenceSessionDraft ToDraft(
                DateTimeOffset? endedAtUtc,
                string? endReason) =>
                new PlayerEvidenceSessionDraft(
                    SessionId,
                    StartedAtUtc,
                    endedAtUtc,
                    endReason,
                    LastPosition,
                    Completeness);
        }

        private sealed class Subscription : IDisposable
        {
            private Action? unsubscribe;
            public Subscription(Action unsubscribe) => this.unsubscribe = unsubscribe;
            public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }
}
