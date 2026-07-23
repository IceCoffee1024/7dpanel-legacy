using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.ConsoleCommands
{
    internal delegate Task<ConsoleCommandResult> DispatchConsoleCommand(
        ConsoleCommandRequest request,
        TimeSpan startTimeout,
        CancellationToken cancellationToken);

    public sealed class SevenDaysConsoleCommandService :
        IConsoleCommandGateway,
        IModRuntime,
        IDisposable
    {
        public const int DefaultQueueCapacity = 32;

        private static readonly TimeSpan DefaultStartTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        private readonly object operationSync = new();
        private readonly Channel<ConsoleCommandWorkItem> channel;
        private readonly TimeSpan startTimeout;
        private readonly TimeSpan drainTimeout;
        private readonly DispatchConsoleCommand dispatch;
        private Task? consumerTask;
        private bool accepting;
        private bool stopped;

        public SevenDaysConsoleCommandService()
            : this(
                DefaultQueueCapacity,
                DefaultStartTimeout,
                DispatchOnGameThreadAsync,
                DefaultDrainTimeout)
        {
        }

        internal SevenDaysConsoleCommandService(
            int queueCapacity,
            TimeSpan startTimeout,
            DispatchConsoleCommand dispatch,
            TimeSpan drainTimeout)
        {
            if (queueCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            if (startTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(startTimeout));
            if (drainTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(drainTimeout));
            this.startTimeout = startTimeout;
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.drainTimeout = drainTimeout;
            channel = Channel.CreateBounded<ConsoleCommandWorkItem>(
                new BoundedChannelOptions(queueCapacity)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
        }

        public void Start()
        {
            lock (operationSync)
            {
                if (accepting) return;
                if (stopped) throw new ObjectDisposedException(nameof(SevenDaysConsoleCommandService));
                var ready = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                consumerTask = Task.Run(() => ConsumeAsync(ready));
                ready.Task.GetAwaiter().GetResult();
                accepting = true;
            }
        }

        public Task<ConsoleCommandResult> ExecuteAsync(
            ConsoleCommandRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ConsoleCommandResult>(cancellationToken);

            lock (operationSync)
            {
                if (!accepting)
                    return Task.FromException<ConsoleCommandResult>(
                        new ConsoleCommandUnavailableException());

                var workItem = new ConsoleCommandWorkItem(request, cancellationToken);
                if (channel.Writer.TryWrite(workItem)) return workItem.Task;
                workItem.Dispose();
                return Task.FromException<ConsoleCommandResult>(
                    new ConsoleCommandQueueFullException());
            }
        }

        public void MarkGameReady()
        {
        }

        public void Stop()
        {
            lock (operationSync)
            {
                if (stopped) return;
                stopped = true;
                accepting = false;
                channel.Writer.TryComplete();
            }

            var consumer = consumerTask;
            if (consumer != null && !consumer.Wait(drainTimeout))
                throw new TimeoutException(
                    "Console command service did not drain before the shutdown deadline.");
        }

        public void Dispose() => Stop();

        private async Task ConsumeAsync(TaskCompletionSource<bool> ready)
        {
            ready.TrySetResult(true);
            while (await channel.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var workItem))
                {
                    using (workItem)
                    {
                        lock (operationSync)
                        {
                            if (stopped)
                            {
                                workItem.RejectUnavailable();
                                continue;
                            }
                        }
                        if (!workItem.TryStart()) continue;
                        try
                        {
                            var result = await dispatch(
                                    workItem.Request,
                                    startTimeout,
                                    CancellationToken.None)
                                .ConfigureAwait(false);
                            workItem.Complete(result);
                        }
                        catch (Exception exception)
                        {
                            workItem.Fail(exception);
                        }
                    }
                }
            }
        }

        private static Task<ConsoleCommandResult> DispatchOnGameThreadAsync(
            ConsoleCommandRequest request,
            TimeSpan startTimeout,
            CancellationToken cancellationToken)
        {
            return GameThreadDispatcher.Enqueue(
                "7DPanel.Console." + request.Command,
                () =>
                {
                    using (ConsoleCommandSourceContext.Push(
                        "7dpanel-http",
                        request.ActorSubject))
                    {
                        var output = SdtdConsole.Instance.ExecuteSync(request.Command, null);
                        return new ConsoleCommandResult(
                            request.Command,
                            output ?? (IEnumerable<string>)Array.Empty<string>());
                    }
                },
                startTimeout,
                cancellationToken);
        }
    }
}