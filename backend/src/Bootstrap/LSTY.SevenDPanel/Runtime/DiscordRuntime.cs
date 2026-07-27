using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Discord;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class DiscordRuntime : IModRuntime, IDisposable
    {
        private readonly DiscordDeliveryWorker worker;
        private readonly DiscordInboundRuntime inbound;
        private readonly Func<DiscordGatewayClient?> createGateway;
        private readonly IModRuntime inner;
        private readonly TimeSpan stopTimeout;
        private readonly object sync = new object();
        private CancellationTokenSource? lifetime;
        private Task? workerTask;
        private DiscordGatewayClient? gateway;
        private bool started;

        public DiscordRuntime(
            DiscordDeliveryWorker worker,
            DiscordInboundRuntime inbound,
            Func<DiscordGatewayClient?> createGateway,
            IModRuntime inner,
            TimeSpan stopTimeout)
        {
            this.worker = worker ?? throw new ArgumentNullException(nameof(worker));
            this.inbound = inbound ?? throw new ArgumentNullException(nameof(inbound));
            this.createGateway = createGateway ?? throw new ArgumentNullException(nameof(createGateway));
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (stopTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(stopTimeout));
            this.stopTimeout = stopTimeout;
        }

        public void Start()
        {
            lock (sync)
            {
                if (started) return;
                worker.RecoverInterrupted();
                lifetime = new CancellationTokenSource();
                workerTask = Task.Factory.StartNew(
                    () => worker.RunAsync(lifetime.Token),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default).Unwrap();
                if (workerTask.IsFaulted)
                    throw workerTask.Exception?.Flatten().InnerException ??
                        new InvalidOperationException("discord_worker_start_failed");
                try
                {
                    inner.Start();
                    inbound.Start();
                    gateway = createGateway();
                    gateway?.Start();
                    started = true;
                }
                catch
                {
                    StopInbound(null);
                    StopWorker(null);
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            lock (sync) inner.MarkGameReady();
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!started && workerTask == null) return;
                var failures = new List<Exception>();
                if (started)
                {
                    StopInbound(failures);
                    try { inner.Stop(); }
                    catch (Exception exception) { failures.Add(exception); }
                }
                StopWorker(failures);
                if (failures.Count == 0) started = false;
                else throw new AggregateException(failures);
            }
        }

        public void Dispose()
        {
            Stop();
            gateway?.Dispose();
            inbound.Dispose();
            lifetime?.Dispose();
        }

        private void StopInbound(ICollection<Exception>? failures)
        {
            var activeGateway = gateway;
            gateway = null;
            if (activeGateway != null)
            {
                try
                {
                    if (!activeGateway.StopAsync(stopTimeout, CancellationToken.None)
                        .GetAwaiter().GetResult())
                    {
                        failures?.Add(new TimeoutException("discord_gateway_stop_timeout"));
                    }
                }
                catch (Exception exception)
                {
                    failures?.Add(exception);
                }
                finally
                {
                    activeGateway.Dispose();
                }
            }

            try
            {
                if (!inbound.StopAsync(stopTimeout, CancellationToken.None)
                    .GetAwaiter().GetResult())
                {
                    failures?.Add(new TimeoutException("discord_inbound_stop_timeout"));
                }
            }
            catch (Exception exception)
            {
                failures?.Add(exception);
            }
        }

        private void StopWorker(ICollection<Exception>? failures)
        {
            var task = workerTask;
            if (task == null) return;
            try { lifetime?.Cancel(); }
            catch (Exception exception) { failures?.Add(exception); }
            try
            {
                if (!task.Wait(stopTimeout))
                {
                    failures?.Add(new TimeoutException("discord_worker_stop_timeout"));
                    return;
                }
            }
            catch (AggregateException exception)
            {
                if (!task.IsCanceled && failures != null)
                {
                    foreach (var failure in exception.Flatten().InnerExceptions)
                        failures.Add(failure);
                }
            }
            workerTask = null;
        }
    }
}
