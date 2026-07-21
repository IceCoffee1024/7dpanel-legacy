using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs
{
    public sealed class ServerEventHub : IServerEventStream
    {
        private const int DefaultMaxSubscribers = 8;
        private readonly object sync = new object();
        private readonly ServerEventLiveWindow liveWindow;
        private readonly int maxSubscribers;
        private readonly HashSet<Subscription> subscriptions = new HashSet<Subscription>();
        private bool completed;

        public ServerEventHub(ServerEventLiveWindow liveWindow)
            : this(liveWindow, DefaultMaxSubscribers)
        {
        }

        internal ServerEventHub(ServerEventLiveWindow liveWindow, int maxSubscribers)
        {
            this.liveWindow = liveWindow ?? throw new ArgumentNullException(nameof(liveWindow));
            if (maxSubscribers <= 0) throw new ArgumentOutOfRangeException(nameof(maxSubscribers));
            this.maxSubscribers = maxSubscribers;
        }

        public IReadOnlyList<ServerEvent> ReadAfter(
            long? afterSequence,
            int limit,
            out bool hasGap)
        {
            var window = liveWindow.ReadAfter(afterSequence, limit);
            var cursorIsAhead = afterSequence.HasValue &&
                ((!window.LatestSequence.HasValue && afterSequence.Value > 0) ||
                 (window.LatestSequence.HasValue &&
                  afterSequence.Value > window.LatestSequence.GetValueOrDefault()));
            if (cursorIsAhead) window = liveWindow.ReadAfter(null, limit);
            hasGap = window.HasGap || cursorIsAhead;
            return window.Entries;
        }

        public bool TrySubscribe(int capacity, out IServerEventSubscription? subscription)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            lock (sync)
            {
                if (completed || subscriptions.Count >= maxSubscribers)
                {
                    subscription = null;
                    return false;
                }

                var candidate = new Subscription(this, capacity);
                subscriptions.Add(candidate);
                subscription = candidate;
                return true;
            }
        }

        internal int SubscriberCount
        {
            get { lock (sync) return subscriptions.Count; }
        }

        internal void Publish(ServerEvent serverEvent)
        {
            if (serverEvent == null) throw new ArgumentNullException(nameof(serverEvent));
            List<Subscription>? overflowed = null;

            lock (sync)
            {
                if (completed) return;
                foreach (var subscription in subscriptions)
                {
                    if (subscription.TryWrite(serverEvent)) continue;
                    if (overflowed == null) overflowed = new List<Subscription>();
                    overflowed.Add(subscription);
                }

                if (overflowed != null)
                {
                    foreach (var subscription in overflowed)
                        subscriptions.Remove(subscription);
                }
            }

            if (overflowed == null) return;
            foreach (var subscription in overflowed) subscription.Complete(overflowed: true);
        }

        internal void Complete()
        {
            Subscription[] current;
            lock (sync)
            {
                if (completed) return;
                completed = true;
                current = new Subscription[subscriptions.Count];
                subscriptions.CopyTo(current);
                subscriptions.Clear();
            }

            foreach (var subscription in current) subscription.Complete(overflowed: false);
        }

        private void Remove(Subscription subscription)
        {
            lock (sync) subscriptions.Remove(subscription);
        }

        private sealed class Subscription : IServerEventSubscription
        {
            private readonly ServerEventHub owner;
            private readonly Channel<ServerEvent> channel;
            private int overflowed;
            private int disposed;

            public Subscription(ServerEventHub owner, int capacity)
            {
                this.owner = owner;
                channel = Channel.CreateBounded<ServerEvent>(
                    new BoundedChannelOptions(capacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = true,
                        SingleWriter = false,
                        AllowSynchronousContinuations = false
                    });
            }

            public bool IsOverflowed => Volatile.Read(ref overflowed) != 0;

            public async Task<ServerEvent?> ReadAsync(CancellationToken cancellationToken)
            {
                try
                {
                    return await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) != 0) return;
                channel.Writer.TryComplete();
                owner.Remove(this);
            }

            internal bool TryWrite(ServerEvent serverEvent) =>
                Volatile.Read(ref disposed) == 0 && channel.Writer.TryWrite(serverEvent);

            internal void Complete(bool overflowed)
            {
                if (overflowed) Interlocked.Exchange(ref this.overflowed, 1);
                channel.Writer.TryComplete();
            }
        }
    }
}
