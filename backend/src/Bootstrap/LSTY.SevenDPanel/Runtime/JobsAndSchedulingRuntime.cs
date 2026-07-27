using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Hosting;

namespace LSTY.SevenDPanel.DependencyInjection
{
    internal sealed class JobsAndSchedulingRuntime : IModRuntime, IDisposable
    {
        private readonly PendingRestoreStartupStep startup;
        private readonly Func<CancellationToken, Task> runWorker;
        private readonly Func<CancellationToken, Task> runScheduler;
        private readonly IModRuntime inner;
        private readonly TimeSpan stopTimeout;
        private readonly object sync = new object();
        private CancellationTokenSource? workerLifetime;
        private CancellationTokenSource? schedulerLifetime;
        private Task? workerTask;
        private Task? schedulerTask;
        private bool started;
        private bool innerStopped;
        private bool stopped;

        public JobsAndSchedulingRuntime(
            PendingRestoreStartupStep startup,
            BackgroundWorkerJobStore workerJobs,
            BackgroundWorkConsumer worker,
            BackgroundScheduler scheduler,
            IModRuntime inner,
            TimeSpan stopTimeout)
            : this(
                startup,
                CreateWorkerRunner(workerJobs, worker),
                (scheduler ?? throw new ArgumentNullException(nameof(scheduler))).RunAsync,
                inner,
                stopTimeout)
        {
        }

        internal JobsAndSchedulingRuntime(
            PendingRestoreStartupStep startup,
            Func<CancellationToken, Task> runWorker,
            Func<CancellationToken, Task> runScheduler,
            IModRuntime inner,
            TimeSpan stopTimeout)
        {
            this.startup = startup ?? throw new ArgumentNullException(nameof(startup));
            this.runWorker = runWorker ?? throw new ArgumentNullException(nameof(runWorker));
            this.runScheduler = runScheduler ?? throw new ArgumentNullException(nameof(runScheduler));
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
                if (stopped) throw new ObjectDisposedException(nameof(JobsAndSchedulingRuntime));

                try
                {
                    startup.Execute();
                    workerLifetime = new CancellationTokenSource();
                    schedulerLifetime = new CancellationTokenSource();
                    workerTask = runWorker(workerLifetime.Token) ??
                        throw new InvalidOperationException("job_worker_returned_no_task");
                    ThrowIfFaulted(workerTask);
                    schedulerTask = runScheduler(schedulerLifetime.Token) ??
                        throw new InvalidOperationException("scheduler_returned_no_task");
                    ThrowIfFaulted(schedulerTask);
                    inner.Start();
                    started = true;
                }
                catch
                {
                    StopLoop(schedulerLifetime, schedulerTask, null);
                    StopLoop(workerLifetime, workerTask, null);
                    stopped = true;
                    throw;
                }
            }
        }

        public void MarkGameReady()
        {
            lock (sync)
            {
                inner.MarkGameReady();
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (stopped) return;
                var failures = new List<Exception>();

                if (started && !innerStopped)
                {
                    try
                    {
                        inner.Stop();
                        innerStopped = true;
                    }
                    catch (Exception exception) { failures.Add(exception); }
                }
                if (StopLoop(schedulerLifetime, schedulerTask, failures))
                {
                    schedulerTask = null;
                }
                if (StopLoop(workerLifetime, workerTask, failures))
                {
                    workerTask = null;
                }
                if ((!started || innerStopped) &&
                    schedulerTask == null &&
                    workerTask == null)
                {
                    started = false;
                    stopped = true;
                }

                if (failures.Count != 0) throw new AggregateException(failures);
            }
        }

        public void Dispose()
        {
            Stop();
            schedulerLifetime?.Dispose();
            workerLifetime?.Dispose();
        }

        private bool StopLoop(
            CancellationTokenSource? lifetime,
            Task? task,
            ICollection<Exception>? failures)
        {
            if (lifetime == null || task == null) return true;
            try { lifetime.Cancel(); }
            catch (Exception exception) { failures?.Add(exception); }

            try
            {
                if (!task.Wait(stopTimeout))
                {
                    failures?.Add(new TimeoutException(
                        "Timed out while stopping a jobs and scheduling loop."));
                    return false;
                }
            }
            catch (AggregateException exception)
            {
                if (task.IsCanceled) return true;
                if (failures == null) return true;
                foreach (var failure in exception.Flatten().InnerExceptions)
                    failures.Add(failure);
            }
            return true;
        }

        private static void ThrowIfFaulted(Task task)
        {
            if (!task.IsFaulted) return;
            throw task.Exception?.Flatten().InnerException ??
                new InvalidOperationException("background_loop_start_failed");
        }

        private static Func<CancellationToken, Task> CreateWorkerRunner(
            BackgroundWorkerJobStore workerJobs,
            BackgroundWorkConsumer worker)
        {
            if (workerJobs == null)
                throw new ArgumentNullException(nameof(workerJobs));
            if (worker == null) throw new ArgumentNullException(nameof(worker));
            return cancellationToken =>
            {
                workerJobs.InterruptRunningJobs(DateTimeOffset.UtcNow);
                return worker.RunAsync(cancellationToken);
            };
        }
    }
}
