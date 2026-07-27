using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application.Automations;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Inbound.Automations
{
    public sealed class AutomationTriggerRuntime : IAutomationTriggerIngress, IDisposable
    {
        public const int DefaultQueueCapacity = 256;
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);
        private readonly Func<AutomationTriggerSnapshot, CancellationToken, Task> execute;
        private readonly Channel<AutomationTriggerSnapshot> channel;
        private readonly TimeSpan drainTimeout;
        private readonly CancellationTokenSource cancellation = new();
        private readonly object sync = new();
        private Task? consumer;
        private bool accepting;
        private bool completed;
        private bool? lastBloodMoonPhase;
        private int stopStarted;
        private int disposed;
        private int queueDepth;

        public AutomationTriggerRuntime(
            AutomationExecutionEngine engine,
            int queueCapacity = DefaultQueueCapacity,
            TimeSpan? drainTimeout = null)
            : this(
                engine == null
                    ? throw new ArgumentNullException(nameof(engine))
                    : async (trigger, token) =>
                    {
                        await engine.ExecuteAsync(trigger, token).ConfigureAwait(false);
                    },
                queueCapacity,
                drainTimeout ?? DefaultDrainTimeout)
        {
        }

        internal AutomationTriggerRuntime(
            Func<AutomationTriggerSnapshot, CancellationToken, Task> execute,
            int queueCapacity,
            TimeSpan drainTimeout)
        {
            this.execute = execute ?? throw new ArgumentNullException(nameof(execute));
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.drainTimeout = drainTimeout;
            channel = Channel.CreateBounded<AutomationTriggerSnapshot>(
                new BoundedChannelOptions(queueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });
        }

        public Task Completion
        {
            get
            {
                lock (sync) return consumer ?? Task.CompletedTask;
            }
        }

        public int QueueDepth => Volatile.Read(ref queueDepth);
        public long QueueFullCount { get; private set; }
        public long ExecutionFailureCount { get; private set; }

        public void Start()
        {
            ThrowIfDisposed();
            lock (sync)
            {
                if (accepting) return;
                if (completed) throw new InvalidOperationException("automation_ingress_completed");
                consumer = Task.Factory.StartNew(
                    ConsumeAsync,
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default).Unwrap();
                accepting = true;
            }
        }

        public bool TryWrite(AutomationTriggerSnapshot trigger)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            if (trigger.GapIds == null)
                throw new ArgumentException("Trigger gap IDs are required.", nameof(trigger));
            var gaps = trigger.GapIds.ToArray();
            if (gaps.Any(string.IsNullOrWhiteSpace) ||
                gaps.Distinct(StringComparer.Ordinal).Count() != gaps.Length)
            {
                throw new ArgumentException("Trigger gap IDs must be unique stable IDs.", nameof(trigger));
            }
            var copy = trigger with { GapIds = Array.AsReadOnly(gaps) };
            lock (sync)
            {
                if (!accepting) return false;
                if (channel.Writer.TryWrite(copy))
                {
                    Interlocked.Increment(ref queueDepth);
                    return true;
                }
                QueueFullCount++;
                return false;
            }
        }

        public bool ObserveBloodMoonPhase(
            bool? isActive,
            DateTimeOffset observedAtUtc)
        {
            if (observedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC observation time is required.", nameof(observedAtUtc));
            lock (sync)
            {
                if (isActive != true)
                {
                    lastBloodMoonPhase = isActive;
                    return false;
                }
                if (lastBloodMoonPhase == true) return false;

                var accepted = TryWrite(new AutomationTriggerSnapshot(
                    "blood-moon-entered:" + observedAtUtc.ToUnixTimeMilliseconds(),
                    "BloodMoonPhaseEntered",
                    observedAtUtc,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Active",
                    Array.Empty<string>()));
                if (accepted) lastBloodMoonPhase = true;
                return accepted;
            }
        }

        public void Complete()
        {
            lock (sync)
            {
                if (completed) return;
                completed = true;
                accepting = false;
                channel.Writer.TryComplete();
            }
        }

        public async Task DrainAsync(CancellationToken cancellationToken)
        {
            Task pending;
            lock (sync) pending = consumer ?? Task.CompletedTask;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(drainTimeout, timeout.Token);
            var completedTask = await Task.WhenAny(pending, delay).ConfigureAwait(false);
            if (completedTask == pending)
            {
                timeout.Cancel();
                await pending.ConfigureAwait(false);
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            cancellation.Cancel();
            throw new TimeoutException("automation_ingress_drain_timeout");
        }

        public async Task StopAsync(
            Action stopProducers,
            CancellationToken cancellationToken)
        {
            if (stopProducers == null) throw new ArgumentNullException(nameof(stopProducers));
            if (Interlocked.Exchange(ref stopStarted, 1) != 0)
            {
                await DrainAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            Exception? producerFailure = null;
            try { stopProducers(); }
            catch (Exception exception) { producerFailure = exception; }
            Complete();
            try
            {
                await DrainAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception drainFailure) when (producerFailure != null)
            {
                throw new AggregateException(producerFailure, drainFailure);
            }
            if (producerFailure != null) throw producerFailure;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Complete();
            var pending = Completion;
            if (!pending.IsCompleted)
            {
                try
                {
                    if (!pending.Wait(drainTimeout)) cancellation.Cancel();
                }
                catch { cancellation.Cancel(); }
            }
            if (pending.IsCompleted) cancellation.Dispose();
            else pending.ContinueWith(
                _ => cancellation.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task ConsumeAsync()
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(cancellation.Token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var trigger))
                    {
                        Interlocked.Decrement(ref queueDepth);
                        try { await execute(trigger, cancellation.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                        catch { ExecutionFailureCount++; }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(AutomationTriggerRuntime));
        }
    }
}
