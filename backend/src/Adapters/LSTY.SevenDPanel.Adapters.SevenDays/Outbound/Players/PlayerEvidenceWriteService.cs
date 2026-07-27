using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class PlayerEvidenceWriteService : IDisposable
    {
        public const string QueueFullReason = "QueueFull";
        public const string StoreFailureReason = "StoreFailure";
        public const string DrainTimeoutReason = "DrainTimeout";

        private static long persistedIdSequence = DateTimeOffset.UtcNow.UtcTicks;
        private readonly IPlayerEvidenceStore store;
        private readonly Channel<Envelope> channel;
        private readonly TimeSpan drainTimeout;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly object persistedSync = new object();
        private readonly Dictionary<long, Envelope> outstanding = new Dictionary<long, Envelope>();
        private readonly Dictionary<GapKey, GapWindow> pendingGaps =
            new Dictionary<GapKey, GapWindow>();
        private readonly HashSet<IdempotencyKey> recentKeys = new HashSet<IdempotencyKey>();
        private readonly Queue<IdempotencyKey> recentKeyOrder = new Queue<IdempotencyKey>();
        private readonly int idempotencyCapacity;
        private Task? consumer;
        private bool accepting;
        private bool stopped;
        private long envelopeSequence;
        private int cancellationDisposalScheduled;
        private Action<PlayerSession>? sessionPersisted;

        public PlayerEvidenceWriteService(IPlayerEvidenceStore store)
            : this(
                store,
                PanelPlayerEvidenceOptions.Default.QueueCapacity,
                PanelPlayerEvidenceOptions.Default.DrainTimeout,
                () => DateTimeOffset.UtcNow)
        {
        }

        public PlayerEvidenceWriteService(
            IPlayerEvidenceStore store,
            PanelPlayerEvidenceOptions options)
            : this(
                store,
                (options ?? throw new ArgumentNullException(nameof(options))).QueueCapacity,
                options.DrainTimeout,
                () => DateTimeOffset.UtcNow)
        {
        }

        internal PlayerEvidenceWriteService(
            IPlayerEvidenceStore store,
            int queueCapacity,
            TimeSpan drainTimeout,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            this.drainTimeout = drainTimeout;
            idempotencyCapacity = checked(queueCapacity * 4);
            channel = Channel.CreateBounded<Envelope>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        public void Start()
        {
            lock (sync)
            {
                if (accepting || stopped) return;
                consumer = Task.Factory.StartNew(
                    ConsumeAsync,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default).Unwrap();
                accepting = true;
            }
        }

        internal bool TryRecord(PlayerEvidenceDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            lock (sync)
            {
                if (!accepting) return false;

                var idempotencyKey = new IdempotencyKey(
                    draft.CrossplatformId,
                    draft.ServerId,
                    draft.ObservedAtUtc);
                if (recentKeys.Contains(idempotencyKey)) return true;

                var envelope = new Envelope(
                    Interlocked.Increment(ref envelopeSequence),
                    draft);
                if (!channel.Writer.TryWrite(envelope))
                {
                    AddDraftGaps(draft, QueueFullReason);
                    return false;
                }

                outstanding.Add(envelope.Id, envelope);
                Remember(idempotencyKey);
                return true;
            }
        }

        internal IDisposable SubscribePersisted(Action<PlayerSession> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            lock (persistedSync) sessionPersisted += observer;
            return new PersistedSubscription(() =>
            {
                lock (persistedSync) sessionPersisted -= observer;
            });
        }

        public void Stop()
        {
            Task? pending;
            lock (sync)
            {
                if (stopped) return;
                stopped = true;
                accepting = false;
                channel.Writer.TryComplete();
                pending = consumer;
            }

            if (pending == null || WaitForCompletion(pending)) return;

            var timedOutAtUtc = utcClock();
            PlayerEvidenceDraft.RequireUtc(timedOutAtUtc, "timedOutAtUtc");
            lock (sync)
            {
                foreach (var envelope in outstanding.Values)
                    AddDraftGaps(envelope.Draft, DrainTimeoutReason);
                ExtendGaps(DrainTimeoutReason, timedOutAtUtc);
            }
            cancellation.Cancel();
        }

        public void Dispose()
        {
            Stop();
            if (Interlocked.Exchange(ref cancellationDisposalScheduled, 1) != 0) return;
            var pending = consumer;
            if (pending == null || pending.IsCompleted)
            {
                cancellation.Dispose();
                return;
            }
            pending.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private bool WaitForCompletion(Task pending)
        {
            try
            {
                return pending.Wait(drainTimeout);
            }
            catch (AggregateException)
            {
                return true;
            }
        }

        private async Task ConsumeAsync()
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellation.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var envelope))
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        TryFlushGaps();
                        try
                        {
                            Persist(envelope.Draft);
                        }
                        finally
                        {
                            lock (sync) outstanding.Remove(envelope.Id);
                        }
                    }
                }
                TryFlushGaps();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                TryFlushGaps();
            }
        }

        private void Persist(PlayerEvidenceDraft draft)
        {
            if (draft.Session != null)
            {
                try
                {
                    var session = new PlayerSession(
                        draft.Session.SessionId,
                        draft.CrossplatformId,
                        draft.ServerId,
                        draft.WorldId,
                        draft.Session.StartedAtUtc,
                        draft.Session.EndedAtUtc,
                        draft.Session.EndReason,
                        draft.Session.LastPosition,
                        draft.Session.Completeness);
                    store.AppendSession(session);
                    PublishPersisted(session);
                }
                catch
                {
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            if (draft.Activity != null)
            {
                try
                {
                    store.AppendActivity(new PlayerActivityEvent(
                        NextPersistedId(),
                        draft.CrossplatformId,
                        draft.ServerId,
                        draft.WorldId,
                        draft.Activity.Kind,
                        draft.ObservedAtUtc,
                        draft.Activity.CorrelationId,
                        draft.Activity.Completeness));
                }
                catch
                {
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            if (draft.Inventory != null)
            {
                try
                {
                    store.AppendInventorySnapshot(new PlayerInventorySnapshot(
                        NextPersistedId(),
                        draft.CrossplatformId,
                        draft.ServerId,
                        draft.WorldId,
                        draft.ObservedAtUtc,
                        draft.Inventory.GameVersion,
                        draft.Inventory.CatalogVersion,
                        draft.Inventory.CatalogResolution,
                        draft.Inventory.Fingerprint,
                        draft.Inventory.AdminBoundary,
                        draft.Inventory.Items));
                }
                catch
                {
                    lock (sync)
                        AddGap(
                            new GapKey(draft.CrossplatformId, EvidenceKind.Inventory, StoreFailureReason),
                            draft.ObservedAtUtc,
                            1);
                }
            }

            cancellation.Token.ThrowIfCancellationRequested();
            if (draft.Skills != null)
            {
                try
                {
                    store.AppendSkillSnapshot(new PlayerSkillSnapshot(
                        NextPersistedId(),
                        draft.CrossplatformId,
                        draft.ServerId,
                        draft.WorldId,
                        draft.ObservedAtUtc,
                        draft.Skills.GameVersion,
                        draft.Skills.Level,
                        draft.Skills.SkillPoints,
                        draft.Skills.Values));
                }
                catch
                {
                    lock (sync)
                        AddGap(
                            new GapKey(draft.CrossplatformId, EvidenceKind.Skill, StoreFailureReason),
                            draft.ObservedAtUtc,
                            1);
                }
            }
        }

        private void PublishPersisted(PlayerSession session)
        {
            Action<PlayerSession>? observers;
            lock (persistedSync) observers = sessionPersisted;
            if (observers == null) return;
            foreach (Action<PlayerSession> observer in observers.GetInvocationList())
            {
                try { observer(session); }
                catch { }
            }
        }

        private void TryFlushGaps()
        {
            while (TryTakeGap(out var key, out var gap))
            {
                try
                {
                    var record = new PlayerEvidenceGap(
                        NextPersistedId(),
                        key.CrossplatformId,
                        gap.Start,
                        gap.End,
                        key.Reason,
                        gap.Count);
                    if (key.Kind == EvidenceKind.Inventory) store.AppendInventoryGap(record);
                    else store.AppendSkillGap(record);
                }
                catch
                {
                    lock (sync) MergeGap(key, gap);
                    return;
                }
            }
        }

        private bool TryTakeGap(out GapKey key, out GapWindow gap)
        {
            lock (sync)
            {
                key = default;
                gap = default;
                var found = false;
                foreach (var pair in pendingGaps)
                {
                    key = pair.Key;
                    gap = pair.Value;
                    found = true;
                    break;
                }
                if (!found) return false;
                pendingGaps.Remove(key);
                return true;
            }
        }

        private void AddDraftGaps(PlayerEvidenceDraft draft, string reason)
        {
            if (draft.Inventory != null)
                AddGap(
                    new GapKey(draft.CrossplatformId, EvidenceKind.Inventory, reason),
                    draft.ObservedAtUtc,
                    1);
            if (draft.Skills != null)
                AddGap(
                    new GapKey(draft.CrossplatformId, EvidenceKind.Skill, reason),
                    draft.ObservedAtUtc,
                    1);
        }

        private void AddGap(GapKey key, DateTimeOffset atUtc, long count)
        {
            pendingGaps[key] = pendingGaps.TryGetValue(key, out var current)
                ? current.Include(atUtc, count)
                : new GapWindow(atUtc, atUtc, count);
        }

        private void MergeGap(GapKey key, GapWindow gap)
        {
            pendingGaps[key] = pendingGaps.TryGetValue(key, out var current)
                ? current.Merge(gap)
                : gap;
        }

        private void ExtendGaps(string reason, DateTimeOffset endedAtUtc)
        {
            var keys = new List<GapKey>();
            foreach (var pair in pendingGaps)
                if (string.Equals(pair.Key.Reason, reason, StringComparison.Ordinal))
                    keys.Add(pair.Key);
            foreach (var key in keys)
                pendingGaps[key] = pendingGaps[key].Include(endedAtUtc, 0);
        }

        private void Remember(IdempotencyKey key)
        {
            recentKeys.Add(key);
            recentKeyOrder.Enqueue(key);
            while (recentKeyOrder.Count > idempotencyCapacity)
                recentKeys.Remove(recentKeyOrder.Dequeue());
        }

        private static long NextPersistedId() => Interlocked.Increment(ref persistedIdSequence);

        private sealed class Envelope
        {
            public Envelope(long id, PlayerEvidenceDraft draft)
            {
                Id = id;
                Draft = draft;
            }

            public long Id { get; }
            public PlayerEvidenceDraft Draft { get; }
        }

        private sealed class PersistedSubscription : IDisposable
        {
            private Action? unsubscribe;

            public PersistedSubscription(Action unsubscribe) =>
                this.unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));

            public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }

        private enum EvidenceKind
        {
            Inventory,
            Skill
        }

        private readonly struct GapKey : IEquatable<GapKey>
        {
            public GapKey(string crossplatformId, EvidenceKind kind, string reason)
            {
                CrossplatformId = crossplatformId;
                Kind = kind;
                Reason = reason;
            }

            public string CrossplatformId { get; }
            public EvidenceKind Kind { get; }
            public string Reason { get; }

            public bool Equals(GapKey other) =>
                Kind == other.Kind &&
                string.Equals(CrossplatformId, other.CrossplatformId, StringComparison.Ordinal) &&
                string.Equals(Reason, other.Reason, StringComparison.Ordinal);

            public override bool Equals(object? value) => value is GapKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(CrossplatformId);
                    hash = (hash * 397) ^ (int)Kind;
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Reason);
                    return hash;
                }
            }
        }

        private readonly struct IdempotencyKey : IEquatable<IdempotencyKey>
        {
            public IdempotencyKey(
                string crossplatformId,
                string serverId,
                DateTimeOffset observedAtUtc)
            {
                CrossplatformId = crossplatformId;
                ServerId = serverId;
                ObservedAtUtc = observedAtUtc;
            }

            public string CrossplatformId { get; }
            public string ServerId { get; }
            public DateTimeOffset ObservedAtUtc { get; }

            public bool Equals(IdempotencyKey other) =>
                ObservedAtUtc.Equals(other.ObservedAtUtc) &&
                string.Equals(CrossplatformId, other.CrossplatformId, StringComparison.Ordinal) &&
                string.Equals(ServerId, other.ServerId, StringComparison.Ordinal);

            public override bool Equals(object? value) => value is IdempotencyKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.Ordinal.GetHashCode(CrossplatformId);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ServerId);
                    hash = (hash * 397) ^ ObservedAtUtc.GetHashCode();
                    return hash;
                }
            }
        }

        private readonly struct GapWindow
        {
            public GapWindow(DateTimeOffset start, DateTimeOffset end, long count)
            {
                Start = start;
                End = end;
                Count = count;
            }

            public DateTimeOffset Start { get; }
            public DateTimeOffset End { get; }
            public long Count { get; }

            public GapWindow Include(DateTimeOffset at, long count) =>
                new GapWindow(
                    at < Start ? at : Start,
                    at > End ? at : End,
                    Count + count);

            public GapWindow Merge(GapWindow other) =>
                new GapWindow(
                    other.Start < Start ? other.Start : Start,
                    other.End > End ? other.End : End,
                    Count + other.Count);
        }
    }
}
