using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Activity
{
    public sealed class SevenDaysRecentActivityRecorder : IDisposable
    {
        private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);
        private readonly Func<Action<string>, IDisposable> subscribeJoined;
        private readonly Func<Action<string>, IDisposable> subscribeLeft;
        private readonly IRecentActivityWriter writer;
        private readonly Action<string> log;
        private readonly object lifecycleGate = new object();
        private readonly HashSet<Task> pendingWrites = new HashSet<Task>();
        private IDisposable? joinedSubscription;
        private IDisposable? leftSubscription;
        private bool started;
        private bool disposed;

        public SevenDaysRecentActivityRecorder(
            IRecentActivityWriter writer,
            Action<string>? log = null)
            : this(SubscribeJoined, SubscribeLeft, writer, log ?? (_ => { }))
        {
        }

        internal SevenDaysRecentActivityRecorder(
            Func<Action<string>, IDisposable> subscribeJoined,
            Func<Action<string>, IDisposable> subscribeLeft,
            IRecentActivityWriter writer,
            Action<string> log)
        {
            this.subscribeJoined = subscribeJoined ?? throw new ArgumentNullException(nameof(subscribeJoined));
            this.subscribeLeft = subscribeLeft ?? throw new ArgumentNullException(nameof(subscribeLeft));
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            lock (lifecycleGate)
            {
                if (disposed || started) return;

                IDisposable? candidateJoined = null;
                IDisposable? candidateLeft = null;
                try
                {
                    candidateJoined = subscribeJoined(RecordJoined);
                    if (disposed)
                    {
                        SafeDispose(candidateJoined);
                        return;
                    }

                    candidateLeft = subscribeLeft(RecordLeft);
                    if (disposed)
                    {
                        SafeDispose(candidateLeft);
                        SafeDispose(candidateJoined);
                        return;
                    }

                    joinedSubscription = candidateJoined;
                    leftSubscription = candidateLeft;
                    started = true;
                }
                catch
                {
                    SafeDispose(candidateLeft);
                    SafeDispose(candidateJoined);
                    throw;
                }
            }
        }

        public void Dispose()
        {
            Task[] writes;
            lock (lifecycleGate)
            {
                if (!disposed)
                {
                    disposed = true;
                    started = false;
                    SafeDispose(leftSubscription);
                    SafeDispose(joinedSubscription);
                    leftSubscription = null;
                    joinedSubscription = null;
                }

                writes = new Task[pendingWrites.Count];
                pendingWrites.CopyTo(writes);
            }

            if (writes.Length == 0) return;
            if (Task.WaitAll(writes, DrainTimeout)) return;

            WarnDrainTimeout();
            throw new TimeoutException(
                "Recent activity writes did not drain before the shutdown timeout.");
        }

        private void RecordJoined(string displayName)
        {
            Record(displayName, writer.RecordPlayerJoinedAsync);
        }

        private void RecordLeft(string displayName)
        {
            Record(displayName, writer.RecordPlayerLeftAsync);
        }

        private void Record(
            string displayName,
            Func<string, DateTimeOffset, CancellationToken, Task> record)
        {
            lock (lifecycleGate)
            {
                if (disposed || !started) return;

                try
                {
                    var occurredAtUtc = DateTimeOffset.UtcNow;
                    var pending = Task.Run(() => RecordSafelyAsync(
                        displayName,
                        occurredAtUtc,
                        record));
                    pendingWrites.Add(pending);
                    _ = pending.ContinueWith(
                        _ => RemovePending(pending),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                catch
                {
                    Warn();
                }
            }
        }

        private async Task RecordSafelyAsync(
            string displayName,
            DateTimeOffset occurredAtUtc,
            Func<string, DateTimeOffset, CancellationToken, Task> record)
        {
            try
            {
                await record(
                    displayName,
                    occurredAtUtc,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                Warn();
            }
        }

        private void RemovePending(Task pending)
        {
            lock (lifecycleGate)
            {
                pendingWrites.Remove(pending);
            }
        }

        private void Warn()
        {
            try { log("Recent activity recording failed; player activity continues."); } catch { }
        }

        private void WarnDrainTimeout()
        {
            try { log("Recent activity drain timed out; runtime resources remain active."); } catch { }
        }

        private static void SafeDispose(IDisposable? subscription)
        {
            try { subscription?.Dispose(); } catch { }
        }

        private static IDisposable SubscribeJoined(Action<string> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerJoinedGameData> callback =
                delegate(ref ModEvents.SPlayerJoinedGameData data)
                {
                    handler(CopyDisplayName(data.ClientInfo));
                };
            ModEvents.PlayerJoinedGame.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerJoinedGame.UnregisterHandler(callback));
        }

        private static IDisposable SubscribeLeft(Action<string> handler)
        {
            ModEvents.ModEventHandlerDelegate<ModEvents.SPlayerDisconnectedData> callback =
                delegate(ref ModEvents.SPlayerDisconnectedData data)
                {
                    handler(CopyDisplayName(data.ClientInfo));
                };
            ModEvents.PlayerDisconnected.RegisterHandler(callback);
            return new Subscription(() => ModEvents.PlayerDisconnected.UnregisterHandler(callback));
        }

        private static string CopyDisplayName(global::ClientInfo? client)
        {
            return client?.playerName ?? string.Empty;
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
