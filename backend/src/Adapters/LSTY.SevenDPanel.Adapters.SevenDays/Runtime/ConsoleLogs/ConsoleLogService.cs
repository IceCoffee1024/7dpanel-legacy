using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Hosting;
using LSTY.SevenDPanel.Hosting.ServerEvents;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleLogs
{
    public sealed class ConsoleLogService : IDisposable
    {
        private const int DefaultQueueCapacity = 1024;
        private const int DefaultLiveWindowCapacity = 5000;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly object operationSync = new object();
        private readonly object metricsSync = new object();
        private readonly Channel<PendingServerEvent> channel;
        private readonly ServerEventLiveWindow liveWindow;
        private readonly ServerEventHub hub;
        private readonly Func<Action<ConsoleLogEntry>, IDisposable> subscribe;
        private readonly Func<ConsoleLogEntry, ServerEvent> appendConsoleLog;
        private readonly TimeSpan drainTimeout;
        private readonly Action<string> log;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private IDisposable? subscription;
        private Task? consumerTask;
        private bool started;
        private bool stopped;
        private bool accepting;
        private long acceptedCount;
        private long consumedCount;
        private long droppedFullCount;
        private long rejectedStoppingCount;
        private long consumerFailureCount;
        private int currentDepth;
        private int highWaterMark;

        public ConsoleLogService(Action<string>? log = null)
            : this(
                new ServerEventLiveWindow(DefaultLiveWindowCapacity),
                DefaultQueueCapacity,
                DefaultDrainTimeout,
                SubscribeToGameLogs,
                null,
                log)
        {
        }

        internal ConsoleLogService(
            ServerEventLiveWindow liveWindow,
            int queueCapacity,
            TimeSpan drainTimeout,
            Func<Action<ConsoleLogEntry>, IDisposable> subscribe,
            Func<ConsoleLogEntry, ServerEvent>? appendConsoleLog = null,
            Action<string>? log = null)
        {
            this.liveWindow = liveWindow ?? throw new ArgumentNullException(nameof(liveWindow));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            this.subscribe = subscribe ?? throw new ArgumentNullException(nameof(subscribe));
            this.appendConsoleLog = appendConsoleLog ?? liveWindow.AppendConsoleLog;
            this.log = log ?? (_ => { });
            hub = new ServerEventHub(liveWindow);
            channel = Channel.CreateBounded<PendingServerEvent>(
                new BoundedChannelOptions(queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
        }

        public ServerEventLiveWindow LiveWindow => liveWindow;
        public IServerEventStream Stream => hub;

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
                    subscription = subscribe(entry => TryPublish(entry));
                    started = true;
                }
                catch
                {
                    lock (metricsSync) accepting = false;
                    stopped = true;
                    channel.Writer.TryComplete();
                    WaitForConsumer();
                    hub.Complete();
                    throw;
                }
            }
        }

        internal bool TryPublish(ConsoleLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (metricsSync)
            {
                if (!accepting)
                {
                    rejectedStoppingCount++;
                    return false;
                }

                if (!channel.Writer.TryWrite(PendingServerEvent.FromConsoleLog(entry)))
                {
                    droppedFullCount++;
                    return false;
                }

                acceptedCount++;
                currentDepth++;
                if (currentDepth > highWaterMark) highWaterMark = currentDepth;
                return true;
            }
        }

        public void Stop()
        {
            lock (operationSync)
            {
                if (stopped) return;
                stopped = true;
                lock (metricsSync) accepting = false;

                var failures = new List<Exception>();
                var candidateSubscription = subscription;
                subscription = null;
                try
                {
                    candidateSubscription?.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }

                if (started)
                {
                    try
                    {
                        EnqueueLifecycleEvent(PendingServerEvent.ServerStopping(DateTime.UtcNow));
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex);
                    }
                }

                channel.Writer.TryComplete();
                var candidateConsumer = consumerTask;
                if (candidateConsumer != null)
                {
                    try
                    {
                        if (!candidateConsumer.Wait(drainTimeout))
                        {
                            cancellation.Cancel();
                            failures.Add(new TimeoutException(
                                "Console log service did not drain before the shutdown deadline."));
                        }
                    }
                    catch (AggregateException ex)
                    {
                        failures.AddRange(ex.Flatten().InnerExceptions);
                    }
                }

                hub.Complete();
                if (candidateSubscription != null) WriteStopSummary();
                if (failures.Count > 0) throw new AggregateException(failures);
            }
        }

        public void Dispose()
        {
            Stop();
            cancellation.Dispose();
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
                    while (true)
                    {
                        if (!channel.Reader.TryRead(out var pendingEvent)) break;
                        if (pendingEvent.Kind == PendingServerEventKind.ConsoleLog)
                        {
                            lock (metricsSync) currentDepth--;
                        }

                        try
                        {
                            ServerEvent retainedEvent;
                            switch (pendingEvent.Kind)
                            {
                                case PendingServerEventKind.ConsoleLog:
                                    retainedEvent = appendConsoleLog(pendingEvent.ConsoleLog!);
                                    lock (metricsSync) consumedCount++;
                                    break;
                                case PendingServerEventKind.GameReady:
                                    retainedEvent = liveWindow.AppendGameReady(pendingEvent.OccurredAtUtc);
                                    break;
                                case PendingServerEventKind.ServerStopping:
                                    retainedEvent = liveWindow.AppendServerStopping(pendingEvent.OccurredAtUtc);
                                    break;
                                default:
                                    throw new InvalidOperationException("Unknown pending server event kind.");
                            }

                            hub.Publish(retainedEvent);
                        }
                        catch
                        {
                            lock (metricsSync) consumerFailureCount++;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void WaitForConsumer()
        {
            var candidate = consumerTask;
            if (candidate == null) return;
            try { candidate.Wait(drainTimeout); }
            catch { }
        }

        internal bool TryMarkGameReady()
        {
            try
            {
                lock (metricsSync)
                {
                    if (!accepting) return false;
                    EnqueueLifecycleEvent(PendingServerEvent.GameReady(DateTime.UtcNow));
                }
                return true;
            }
            catch (Exception ex)
            {
                try { log("Game-ready server event could not be queued: " + ex.GetType().Name + "."); }
                catch { }
                return false;
            }
        }

        private void EnqueueLifecycleEvent(PendingServerEvent serverEvent)
        {
            if (channel.Writer.TryWrite(serverEvent)) return;

            using var timeout = new CancellationTokenSource(drainTimeout);
            channel.Writer
                .WriteAsync(serverEvent, timeout.Token)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }

        private void WriteStopSummary()
        {
            try
            {
                log(string.Format(
                    CultureInfo.InvariantCulture,
                    "Console log service stopped: accepted={0}, consumed={1}, droppedFull={2}, rejectedStopping={3}, consumerFailures={4}, highWater={5}.",
                    AcceptedCount,
                    ConsumedCount,
                    DroppedFullCount,
                    RejectedStoppingCount,
                    ConsumerFailureCount,
                    HighWaterMark));
            }
            catch
            {
            }
        }

        private static IDisposable SubscribeToGameLogs(Action<ConsoleLogEntry> publish)
        {
            Log.LogCallbackExtendedDelegate callback = (
                formattedMessage,
                message,
                trace,
                logType,
                timestamp,
                uptimeMilliseconds) => publish(new ConsoleLogEntry(
                    formattedMessage,
                    message,
                    trace,
                    (ConsoleLogType)(int)logType,
                    timestamp,
                    uptimeMilliseconds));

            try
            {
                Log.LogCallbacksExtended += callback;
                return new GameLogSubscription(callback);
            }
            catch
            {
                try { Log.LogCallbacksExtended -= callback; } catch { }
                throw;
            }
        }

        private sealed class GameLogSubscription : IDisposable
        {
            private Log.LogCallbackExtendedDelegate? callback;

            public GameLogSubscription(Log.LogCallbackExtendedDelegate callback)
            {
                this.callback = callback;
            }

            public void Dispose()
            {
                var candidate = Interlocked.Exchange(ref callback, null);
                if (candidate != null) Log.LogCallbacksExtended -= candidate;
            }
        }

        private enum PendingServerEventKind
        {
            ConsoleLog,
            GameReady,
            ServerStopping
        }

        private sealed class PendingServerEvent
        {
            private PendingServerEvent(
                PendingServerEventKind kind,
                ConsoleLogEntry? consoleLog,
                DateTime occurredAtUtc)
            {
                Kind = kind;
                ConsoleLog = consoleLog;
                OccurredAtUtc = occurredAtUtc;
            }

            public PendingServerEventKind Kind { get; }
            public ConsoleLogEntry? ConsoleLog { get; }
            public DateTime OccurredAtUtc { get; }

            public static PendingServerEvent FromConsoleLog(ConsoleLogEntry entry) =>
                new PendingServerEvent(
                    PendingServerEventKind.ConsoleLog,
                    entry ?? throw new ArgumentNullException(nameof(entry)),
                    default);

            public static PendingServerEvent GameReady(DateTime occurredAtUtc) =>
                new PendingServerEvent(PendingServerEventKind.GameReady, null, occurredAtUtc);

            public static PendingServerEvent ServerStopping(DateTime occurredAtUtc) =>
                new PendingServerEvent(PendingServerEventKind.ServerStopping, null, occurredAtUtc);
        }
    }

    public sealed class ConsoleLogRuntime : IModRuntime, IDisposable
    {
        private readonly ConsoleLogService consoleLogs;
        private readonly IModRuntime inner;
        private int gameReadyPublished;
        private int stopped;

        public ConsoleLogRuntime(ConsoleLogService consoleLogs, IModRuntime inner)
        {
            this.consoleLogs = consoleLogs ?? throw new ArgumentNullException(nameof(consoleLogs));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Start()
        {
            consoleLogs.Start();
            inner.Start();
        }

        public void MarkGameReady()
        {
            inner.MarkGameReady();
            if (Interlocked.CompareExchange(ref gameReadyPublished, 1, 0) == 0)
                consoleLogs.TryMarkGameReady();
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;

            var failures = new List<Exception>();
            try { consoleLogs.Stop(); } catch (Exception ex) { failures.Add(ex); }
            try { inner.Stop(); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count > 0) throw new AggregateException(failures);
        }

        public void Dispose() => Stop();
    }
}
