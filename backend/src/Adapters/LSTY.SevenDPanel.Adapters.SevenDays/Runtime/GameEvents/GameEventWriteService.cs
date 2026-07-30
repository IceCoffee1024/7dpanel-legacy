using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.GameEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.GameEvents
{
    public sealed class GameEventWriteService : IDisposable
    {
        public const int DefaultQueueCapacity = 256;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
        private readonly IGameEventStore store;
        private readonly IAutomationTriggerIngress? automationIngress;
        private readonly Channel<GameEventRecord> channel;
        private readonly TimeSpan drainTimeout;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly object sync = new object();
        private readonly Dictionary<GameEventGapReason, GapWindow> pendingGaps = new Dictionary<GameEventGapReason, GapWindow>();
        private Task? consumer;
        private bool accepting;
        private bool stopped;
        private int queueDepth;
        private GameEventRecord? inFlight;
        private int cancellationDisposalScheduled;

        public GameEventWriteService(IGameEventStore store) : this(store, DefaultQueueCapacity, DefaultDrainTimeout, null) { }
        public GameEventWriteService(
            IGameEventStore store,
            IAutomationTriggerIngress? automationIngress)
            : this(store, DefaultQueueCapacity, DefaultDrainTimeout, automationIngress) { }
        internal GameEventWriteService(IGameEventStore store, int queueCapacity, TimeSpan drainTimeout)
            : this(store, queueCapacity, drainTimeout, null) { }
        internal GameEventWriteService(
            IGameEventStore store,
            int queueCapacity,
            TimeSpan drainTimeout,
            IAutomationTriggerIngress? automationIngress)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            this.automationIngress = automationIngress;
            channel = Channel.CreateBounded<GameEventRecord>(new BoundedChannelOptions(queueCapacity) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        }

        internal int QueueDepth { get { lock (sync) return queueDepth; } }
        internal long QueueFullCount { get; private set; }
        internal long StoreFailureCount { get; private set; }

        public void Start()
        {
            lock (sync)
            {
                if (accepting || stopped) return;
                consumer = Task.Factory.StartNew(
                    Consume,
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
                accepting = true;
            }
        }

        internal bool TryRecord(GameEventRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            bool accepted;
            lock (sync)
            {
                if (!accepting) return false;
                if (channel.Writer.TryWrite(record))
                {
                    queueDepth++;
                    accepted = true;
                }
                else
                {
                    QueueFullCount++;
                    AddGap(GameEventGapReason.QueueFull, record.OccurredAtUtc, 1);
                    accepted = false;
                }
            }
            TryWriteAutomationTrigger(record);
            return accepted;
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
            if (pending == null || pending.Wait(drainTimeout)) return;

            lock (sync)
            {
                var affectedCount = Math.Max(1, queueDepth + (inFlight == null ? 0 : 1));
                AddGap(
                    GameEventGapReason.DrainTimeout,
                    inFlight?.OccurredAtUtc ?? DateTimeOffset.UtcNow,
                    affectedCount);
            }
            cancellation.Cancel();
        }

        public void Dispose()
        {
            Stop();
            if (Interlocked.Exchange(ref cancellationDisposalScheduled, 1) != 0) return;
            var pending = consumer;
            if (pending == null || pending.IsCompleted) cancellation.Dispose();
            else pending.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void Consume()
        {
            try
            {
                while (channel.Reader.WaitToReadAsync(cancellation.Token).AsTask().GetAwaiter().GetResult())
                    while (channel.Reader.TryRead(out var record))
                    {
                        lock (sync)
                        {
                            queueDepth--;
                            inFlight = record;
                        }
                        TryFlushGaps();
                        try { store.Append(record); }
                        catch { lock (sync) { StoreFailureCount++; AddGap(GameEventGapReason.StoreFailure, record.OccurredAtUtc, 1); } }
                        finally { lock (sync) inFlight = null; }
                    }
                TryFlushGaps();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                TryFlushGaps();
            }
        }

        private void TryFlushGaps()
        {
            while (TryTakeGap(out var reason, out var gap))
            {
                try
                {
                    store.AppendGap(new GameEventGap(
                        Guid.NewGuid().ToString("D"),
                        reason,
                        gap.Start,
                        gap.End,
                        gap.Count));
                }
                catch
                {
                    lock (sync) MergeGap(reason, gap);
                    return;
                }
            }
        }

        private bool TryTakeGap(out GameEventGapReason reason, out GapWindow gap)
        {
            lock (sync)
            {
                reason = default;
                gap = default;
                var found = false;
                foreach (var pair in pendingGaps)
                {
                    reason = pair.Key;
                    gap = pair.Value;
                    found = true;
                    break;
                }
                if (!found) return false;
                pendingGaps.Remove(reason);
                return true;
            }
        }

        private void AddGap(GameEventGapReason reason, DateTimeOffset at, long count) =>
            pendingGaps[reason] = pendingGaps.TryGetValue(reason, out var gap) ? gap.Include(at, count) : new GapWindow(at, at, count);

        private void MergeGap(GameEventGapReason reason, GapWindow gap) =>
            pendingGaps[reason] = pendingGaps.TryGetValue(reason, out var current)
                ? current.Merge(gap)
                : gap;

        private void TryWriteAutomationTrigger(GameEventRecord record)
        {
            if (automationIngress == null ||
                (record.EventType != GameEventType.PlayerJoined &&
                 record.EventType != GameEventType.PlayerLeft))
            {
                return;
            }
            var actor = record.Actor;
            var trigger = new AutomationTriggerSnapshot(
                record.EventId,
                record.EventType.ToString(),
                record.OccurredAtUtc,
                actor?.CrossplatformId,
                actor?.EntityId,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<string>());
            try { automationIngress.TryWrite(trigger); }
            catch { }
        }

        private readonly struct GapWindow
        {
            public GapWindow(DateTimeOffset start, DateTimeOffset end, long count) { Start = start; End = end; Count = count; }
            public DateTimeOffset Start { get; } public DateTimeOffset End { get; } public long Count { get; }
            public GapWindow Include(DateTimeOffset at, long count) => new GapWindow(at < Start ? at : Start, at > End ? at : End, Count + count);
            public GapWindow Merge(GapWindow other) => new GapWindow(
                other.Start < Start ? other.Start : Start,
                other.End > End ? other.End : End,
                Count + other.Count);
        }
    }
}
