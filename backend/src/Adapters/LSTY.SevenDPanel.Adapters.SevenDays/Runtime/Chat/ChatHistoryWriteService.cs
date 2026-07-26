using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Chat;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.Chat
{
    public sealed class ChatHistoryWriteService : IDisposable
    {
        public const int DefaultQueueCapacity = 1024;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
        private readonly IChatHistoryStore store;
        private readonly Channel<ChatMessage> channel;
        private readonly TimeSpan drainTimeout;
        private readonly object sync = new object();
        private readonly Dictionary<string, GapWindow> pendingGaps = new Dictionary<string, GapWindow>(StringComparer.Ordinal);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private Task? consumer;
        private bool accepting;
        private bool stopped;
        private int queueDepth;

        public ChatHistoryWriteService(IChatHistoryStore store)
            : this(store, DefaultQueueCapacity, DefaultDrainTimeout) { }

        internal ChatHistoryWriteService(IChatHistoryStore store, int queueCapacity, TimeSpan drainTimeout)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            channel = Channel.CreateBounded<ChatMessage>(new BoundedChannelOptions(queueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }

        internal long DroppedFullCount { get; private set; }
        internal long StoreFailureCount { get; private set; }
        internal int QueueDepth { get { lock (sync) return queueDepth; } }

        public void Start()
        {
            lock (sync)
            {
                if (accepting || stopped) return;
                consumer = Task.Factory.StartNew(ConsumeAsync, CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
                accepting = true;
            }
        }

        internal bool TryRecord(ChatMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            lock (sync)
            {
                if (!accepting) return false;
                if (channel.Writer.TryWrite(message))
                {
                    queueDepth++;
                    return true;
                }
                DroppedFullCount++;
                AddGap("queue-full", message.OccurredAtUtc, 1);
                return false;
            }
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
                while (channel.Reader.TryRead(out var message))
                {
                    lock (sync)
                    {
                        queueDepth--;
                        AddGap("shutdown-timeout", message.OccurredAtUtc, 1);
                    }
                }
            }
            TryFlushGaps();
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
                while (await channel.Reader.WaitToReadAsync(cancellation.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var message))
                    {
                        lock (sync) queueDepth--;
                        TryFlushGaps();
                        try { store.Append(message); }
                        catch
                        {
                            lock (sync)
                            {
                                StoreFailureCount++;
                                AddGap("store-failure", message.OccurredAtUtc, 1);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        private void TryFlushGaps()
        {
            KeyValuePair<string, GapWindow>[] gaps;
            lock (sync)
            {
                gaps = new KeyValuePair<string, GapWindow>[pendingGaps.Count];
                var index = 0;
                foreach (var pair in pendingGaps) gaps[index++] = pair;
            }
            foreach (var pair in gaps)
            {
                try
                {
                    store.AppendGap(new ChatHistoryGap
                    {
                        StartedAtUtc = pair.Value.Start,
                        EndedAtUtc = pair.Value.End,
                        DroppedMessageCount = pair.Value.Count,
                        Reason = pair.Key
                    });
                    lock (sync) pendingGaps.Remove(pair.Key);
                }
                catch { return; }
            }
        }

        private void AddGap(string reason, DateTimeOffset at, long count)
        {
            if (pendingGaps.TryGetValue(reason, out var gap)) pendingGaps[reason] = gap.Include(at, count);
            else pendingGaps[reason] = new GapWindow(at, at, count);
        }

        private readonly struct GapWindow
        {
            public GapWindow(DateTimeOffset start, DateTimeOffset end, long count) { Start = start; End = end; Count = count; }
            public DateTimeOffset Start { get; }
            public DateTimeOffset End { get; }
            public long Count { get; }
            public GapWindow Include(DateTimeOffset at, long count) => new GapWindow(at < Start ? at : Start, at > End ? at : End, Count + count);
        }
    }
}
