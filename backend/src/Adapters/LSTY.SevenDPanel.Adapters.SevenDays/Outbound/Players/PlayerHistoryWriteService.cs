using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players
{
    public sealed class PlayerHistoryWriteService : IModRuntime, IDisposable
    {
        public const int DefaultQueueCapacity = 1024;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
        private readonly IPlayerHistoryStore store;
        private readonly Channel<PlayerSnapshot> channel;
        private readonly TimeSpan drainTimeout;
        private readonly Func<DateTimeOffset> utcClock;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly object persistedSync = new object();
        private readonly Dictionary<GapKey, GapWindow> pendingGaps = new Dictionary<GapKey, GapWindow>();
        private Action<PlayerSnapshot>? persisted;
        private Task? consumer;
        private int queueDepth;
        private bool accepting;
        private bool started;
        private bool stopped;

        public PlayerHistoryWriteService(IPlayerHistoryStore store)
            : this(store, DefaultQueueCapacity, DefaultDrainTimeout, () => DateTimeOffset.UtcNow)
        {
        }

        internal PlayerHistoryWriteService(
            IPlayerHistoryStore store,
            int queueCapacity,
            TimeSpan drainTimeout,
            Func<DateTimeOffset> utcClock)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            this.utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            channel = Channel.CreateBounded<PlayerSnapshot>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        internal long AcceptedCount { get; private set; }
        internal long PersistedCount { get; private set; }
        internal long DroppedFullCount { get; private set; }
        internal long RejectedStoppingCount { get; private set; }
        internal long SkippedMissingCrossplatformIdCount { get; private set; }
        internal long StoreFailureCount { get; private set; }

        public void Start()
        {
            lock (sync)
            {
                if (started || stopped) return;
                consumer = Task.Factory.StartNew(
                    ConsumeAsync,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default).Unwrap();
                accepting = true;
                started = true;
            }
        }

        public void MarkGameReady()
        {
        }

        internal bool TryRecord(PlayerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var crossplatformId = snapshot.CrossplatformIdentity?.CombinedId;
            lock (sync)
            {
                if (string.IsNullOrWhiteSpace(crossplatformId))
                {
                    SkippedMissingCrossplatformIdCount++;
                    return false;
                }
                if (!accepting)
                {
                    RejectedStoppingCount++;
                    return false;
                }
                if (channel.Writer.TryWrite(snapshot))
                {
                    AcceptedCount++;
                    queueDepth++;
                    return true;
                }

                DroppedFullCount++;
                AddGap(crossplatformId!, snapshot.ObservedAtUtc, PlayerHistoryGapReason.QueueFull);
                return false;
            }
        }

        internal IDisposable SubscribePersisted(Action<PlayerSnapshot> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            lock (persistedSync) persisted += observer;
            return new PersistedSubscription(() =>
            {
                lock (persistedSync) persisted -= observer;
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
            if (pending == null) return;
            if (!pending.Wait(drainTimeout))
            {
                cancellation.Cancel();
                while (channel.Reader.TryRead(out var snapshot))
                {
                    var id = snapshot.CrossplatformIdentity?.CombinedId;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        lock (sync) AddGap(id!, snapshot.ObservedAtUtc, PlayerHistoryGapReason.ShutdownTimeout);
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
            cancellation.Dispose();
        }

        private async Task ConsumeAsync()
        {
            try
            {
                while (true)
                {
                    while (channel.Reader.TryRead(out var snapshot))
                    {
                        lock (sync)
                        {
                            queueDepth--;
                        }
                        var id = snapshot.CrossplatformIdentity?.CombinedId;
                        if (string.IsNullOrWhiteSpace(id)) continue;
                        try
                        {
                            FlushPendingGaps(id!);
                            store.Append(snapshot);
                            lock (sync) PersistedCount++;
                            PublishPersisted(snapshot);
                        }
                        catch
                        {
                            lock (sync)
                            {
                                StoreFailureCount++;
                                AddGap(id!, snapshot.ObservedAtUtc, PlayerHistoryGapReason.StoreFailure);
                            }
                        }
                    }

                    CompactWhenIdle();
                    if (!await channel.Reader.WaitToReadAsync(cancellation.Token).ConfigureAwait(false))
                        return;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        private void CompactWhenIdle()
        {
            if (Volatile.Read(ref queueDepth) > 256) return;
            try
            {
                store.Compact(utcClock(), 1000);
            }
            catch
            {
            }
        }

        private void FlushPendingGaps(string crossplatformId)
        {
            while (true)
            {
                GapWindow? gap;
                lock (sync)
                {
                    var matching = pendingGaps
                        .Where(pair => pair.Key.CrossplatformId == crossplatformId)
                        .OrderBy(pair => pair.Key.Reason)
                        .FirstOrDefault();
                    if (matching.Key.CrossplatformId == null) return;
                    gap = matching.Value;
                    pendingGaps.Remove(matching.Key);
                }
                try
                {
                    store.AppendGap(new PlayerHistoryGap(Guid.NewGuid().ToString("N"), crossplatformId,
                        gap.StartedAtUtc, gap.CompletedAtUtc, gap.DroppedCount, gap.Reason, utcClock()));
                }
                catch
                {
                    lock (sync) AddGap(crossplatformId, gap.StartedAtUtc, gap.Reason, gap.DroppedCount, gap.CompletedAtUtc);
                    throw;
                }
            }
        }

        private void AddGap(string crossplatformId, DateTimeOffset observedAtUtc, PlayerHistoryGapReason reason) =>
            AddGap(crossplatformId, observedAtUtc, reason, 1, observedAtUtc);

        private void AddGap(string crossplatformId, DateTimeOffset startedAtUtc, PlayerHistoryGapReason reason, long count, DateTimeOffset completedAtUtc)
        {
            var key = new GapKey(crossplatformId, reason);
            if (pendingGaps.TryGetValue(key, out var current))
                pendingGaps[key] = current.Include(startedAtUtc, completedAtUtc, count);
            else
                pendingGaps[key] = new GapWindow(startedAtUtc, completedAtUtc, count, reason);
        }

        private void PublishPersisted(PlayerSnapshot snapshot)
        {
            Action<PlayerSnapshot>? observers;
            lock (persistedSync) observers = persisted;
            if (observers == null) return;
            foreach (Action<PlayerSnapshot> observer in observers.GetInvocationList())
            {
                try { observer(snapshot); }
                catch { }
            }
        }

        private readonly struct GapKey : IEquatable<GapKey>
        {
            public GapKey(string crossplatformId, PlayerHistoryGapReason reason) { CrossplatformId = crossplatformId; Reason = reason; }
            public string CrossplatformId { get; }
            public PlayerHistoryGapReason Reason { get; }
            public bool Equals(GapKey other) => Reason == other.Reason && string.Equals(CrossplatformId, other.CrossplatformId, StringComparison.Ordinal);
            public override bool Equals(object? obj) => obj is GapKey other && Equals(other);
            public override int GetHashCode() => (CrossplatformId ?? string.Empty).GetHashCode() ^ (int)Reason;
        }

        private sealed class GapWindow
        {
            public GapWindow(DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc, long droppedCount, PlayerHistoryGapReason reason) { StartedAtUtc = startedAtUtc; CompletedAtUtc = completedAtUtc; DroppedCount = droppedCount; Reason = reason; }
            public DateTimeOffset StartedAtUtc { get; }
            public DateTimeOffset CompletedAtUtc { get; }
            public long DroppedCount { get; }
            public PlayerHistoryGapReason Reason { get; }
            public GapWindow Include(DateTimeOffset started, DateTimeOffset completed, long count) => new GapWindow(started < StartedAtUtc ? started : StartedAtUtc, completed > CompletedAtUtc ? completed : CompletedAtUtc, DroppedCount + count, Reason);
        }

        private sealed class PersistedSubscription : IDisposable
        {
            private Action? unsubscribe;

            public PersistedSubscription(Action unsubscribe) =>
                this.unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));

            public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
        }
    }
}
