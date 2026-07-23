using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands
{
    public sealed class ConsoleCommandAuditService : IModRuntime, IDisposable
    {
        public const int DefaultQueueCapacity = 256;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly object operationSync = new object();
        private readonly object metricsSync = new object();
        private readonly IConsoleCommandAuditStore store;
        private readonly Channel<ConsoleCommandExecutionObservation> channel;
        private readonly TimeSpan drainTimeout;
        private readonly Func<Action<ConsoleCommandExecutionObservation>, IDisposable> subscribe;
        private readonly Action<string> log;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private IDisposable? subscription;
        private Task? consumerTask;
        private bool started;
        private bool stopped;
        private bool stopCompleted;
        private bool stopSummaryWritten;
        private bool accepting;
        private long acceptedCount;
        private long consumedCount;
        private long droppedFullCount;
        private long rejectedStoppingCount;
        private long consumerFailureCount;
        private int currentDepth;
        private int highWaterMark;
        private readonly List<GapWindow> pendingGaps = new List<GapWindow>();
        private bool dropLogged;
        private bool failureLogged;

        public ConsoleCommandAuditService(
            IConsoleCommandAuditStore store,
            Action<string>? log = null)
            : this(
                store,
                DefaultQueueCapacity,
                DefaultDrainTimeout,
                ConsoleCommandExecutionPatch.Subscribe,
                log)
        {
        }

        internal ConsoleCommandAuditService(
            IConsoleCommandAuditStore store,
            int queueCapacity,
            TimeSpan drainTimeout,
            Func<Action<ConsoleCommandExecutionObservation>, IDisposable> subscribe,
            Action<string>? log = null)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            this.log = log ?? (_ => { });
            channel = Channel.CreateBounded<ConsoleCommandExecutionObservation>(
                new BoundedChannelOptions(queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
        }

        internal long AcceptedCount { get { lock (metricsSync) return acceptedCount; } }
        internal long ConsumedCount { get { lock (metricsSync) return consumedCount; } }
        internal long DroppedFullCount { get { lock (metricsSync) return droppedFullCount; } }
        internal long RejectedStoppingCount { get { lock (metricsSync) return rejectedStoppingCount; } }
        internal long ConsumerFailureCount { get { lock (metricsSync) return consumerFailureCount; } }
        internal int CurrentDepth { get { lock (metricsSync) return currentDepth; } }
        internal int HighWaterMark { get { lock (metricsSync) return highWaterMark; } }

        public void Start()
        {
            lock (operationSync)
            {
                if (started || stopped) return;
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                consumerTask = Task.Run(() => ConsumeAsync(ready, cancellation.Token));
                ready.Task.GetAwaiter().GetResult();
                lock (metricsSync) accepting = true;
                try
                {
                    subscription = subscribe(TryPublishIgnoringResult);
                    started = true;
                }
                catch
                {
                    lock (metricsSync) accepting = false;
                    stopped = true;
                    channel.Writer.TryComplete();
                    WaitForConsumer();
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
        }

        internal bool TryPublish(ConsoleCommandExecutionObservation observation)
        {
            if (observation == null) throw new ArgumentNullException(nameof(observation));
            var shouldLogDrop = false;
            lock (metricsSync)
            {
                if (!accepting)
                {
                    rejectedStoppingCount++;
                    return false;
                }
                if (!channel.Writer.TryWrite(observation))
                {
                    droppedFullCount++;
                    AddGapLocked(
                        observation.StartedAtUtc,
                        observation.CompletedAtUtc,
                        "queue_full");
                    if (!dropLogged)
                    {
                        dropLogged = true;
                        shouldLogDrop = true;
                    }
                }
                else
                {
                    acceptedCount++;
                    currentDepth++;
                    if (currentDepth > highWaterMark) highWaterMark = currentDepth;
                    return true;
                }
            }
            if (shouldLogDrop) SafeLog("Console command audit queue is full; records are being dropped.");
            return false;
        }

        public void Stop()
        {
            lock (operationSync)
            {
                if (stopCompleted) return;
                var failures = new List<Exception>();
                if (!stopped)
                {
                    stopped = true;
                    lock (metricsSync) accepting = false;
                    var candidateSubscription = subscription;
                    subscription = null;
                    try { candidateSubscription?.Dispose(); }
                    catch (Exception ex) { failures.Add(ex); }
                    channel.Writer.TryComplete();
                }
                var candidateConsumer = consumerTask;
                if (candidateConsumer != null)
                {
                    try
                    {
                        if (!candidateConsumer.Wait(drainTimeout))
                        {
                            cancellation.Cancel();
                            failures.Add(new TimeoutException(
                                "Console command audit service did not drain before the shutdown deadline."));
                        }
                    }
                    catch (AggregateException ex)
                    {
                        failures.AddRange(ex.Flatten().InnerExceptions);
                    }
                }
                if (candidateConsumer == null || candidateConsumer.IsCompleted)
                {
                    stopCompleted = true;
                    if (started && !stopSummaryWritten)
                    {
                        stopSummaryWritten = true;
                        WriteStopSummary();
                    }
                }
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose()
        {
            Stop();
            cancellation.Dispose();
        }

        private void TryPublishIgnoringResult(ConsoleCommandExecutionObservation observation)
        {
            TryPublish(observation);
        }

        private async Task ConsumeAsync(
            TaskCompletionSource<bool> ready,
            CancellationToken cancellationToken)
        {
            ready.TrySetResult(true);
            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var observation))
                    {
                        lock (metricsSync) currentDepth--;
                        try
                        {
                            AppendPendingGap();
                            store.Append(observation.ToAuditEntry());
                            lock (metricsSync) consumedCount++;
                        }
                        catch
                        {
                            var shouldLogFailure = false;
                            lock (metricsSync)
                            {
                                consumerFailureCount++;
                                AddGapLocked(
                                    observation.StartedAtUtc,
                                    observation.CompletedAtUtc,
                                    "store_failure");
                                if (!failureLogged)
                                {
                                    failureLogged = true;
                                    shouldLogFailure = true;
                                }
                            }
                            if (shouldLogFailure)
                                SafeLog("Console command audit persistence failed; command execution continues.");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void AppendPendingGap()
        {
            GapWindow? gap;
            lock (metricsSync)
            {
                gap = pendingGaps.Count == 0 ? null : pendingGaps[0];
                if (gap != null) pendingGaps.RemoveAt(0);
            }
            if (gap == null) return;
            try
            {
                store.AppendGap(new ConsoleCommandAuditGap(
                    Guid.NewGuid().ToString("N"),
                    gap.StartedAtUtc,
                    gap.CompletedAtUtc,
                    gap.DroppedCount,
                    gap.Reason));
            }
            catch
            {
                lock (metricsSync)
                {
                    if (pendingGaps.Count > 0 && pendingGaps[0].Reason == gap.Reason)
                    {
                        pendingGaps[0] = gap.Merge(pendingGaps[0]);
                    }
                    else
                    {
                        pendingGaps.Insert(0, gap);
                    }
                }
                throw;
            }
        }

        private void AddGapLocked(
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            string reason)
        {
            var lastIndex = pendingGaps.Count - 1;
            if (lastIndex >= 0 && pendingGaps[lastIndex].Reason == reason)
            {
                pendingGaps[lastIndex] = pendingGaps[lastIndex].Include(
                    startedAtUtc,
                    completedAtUtc);
                return;
            }
            pendingGaps.Add(GapWindow.Start(startedAtUtc, completedAtUtc, reason));
        }

        private void WaitForConsumer()
        {
            var candidate = consumerTask;
            if (candidate == null) return;
            try { candidate.Wait(drainTimeout); }
            catch { }
        }

        private void SafeLog(string message)
        {
            try { log(message); }
            catch { }
        }

        private void WriteStopSummary()
        {
            int unrecoveredGapCount;
            long unrecoveredDroppedCount = 0;
            lock (metricsSync)
            {
                unrecoveredGapCount = pendingGaps.Count;
                foreach (var gap in pendingGaps)
                    unrecoveredDroppedCount += gap.DroppedCount;
            }
            SafeLog(string.Format(
                CultureInfo.InvariantCulture,
                "Console command audit service stopped: accepted={0}, consumed={1}, droppedFull={2}, rejectedStopping={3}, consumerFailures={4}, highWater={5}, unrecoveredGaps={6}, unrecoveredDropped={7}.",
                AcceptedCount,
                ConsumedCount,
                DroppedFullCount,
                RejectedStoppingCount,
                ConsumerFailureCount,
                HighWaterMark,
                unrecoveredGapCount,
                unrecoveredDroppedCount));
        }

        private sealed class GapWindow
        {
            private GapWindow(
                DateTimeOffset startedAtUtc,
                DateTimeOffset completedAtUtc,
                long droppedCount,
                string reason)
            {
                StartedAtUtc = startedAtUtc;
                CompletedAtUtc = completedAtUtc;
                DroppedCount = droppedCount;
                Reason = reason;
            }

            public DateTimeOffset StartedAtUtc { get; }
            public DateTimeOffset CompletedAtUtc { get; }
            public long DroppedCount { get; }
            public string Reason { get; }

            public static GapWindow Start(
                DateTimeOffset startedAtUtc,
                DateTimeOffset completedAtUtc,
                string reason)
            {
                return new GapWindow(startedAtUtc, completedAtUtc, 1, reason);
            }

            public GapWindow Include(
                DateTimeOffset startedAtUtc,
                DateTimeOffset completedAtUtc)
            {
                return new GapWindow(
                    startedAtUtc < StartedAtUtc ? startedAtUtc : StartedAtUtc,
                    completedAtUtc > CompletedAtUtc ? completedAtUtc : CompletedAtUtc,
                    DroppedCount + 1,
                    Reason);
            }

            public GapWindow Merge(GapWindow other)
            {
                return new GapWindow(
                    other.StartedAtUtc < StartedAtUtc ? other.StartedAtUtc : StartedAtUtc,
                    other.CompletedAtUtc > CompletedAtUtc ? other.CompletedAtUtc : CompletedAtUtc,
                    DroppedCount + other.DroppedCount,
                    Reason);
            }
        }
    }
}