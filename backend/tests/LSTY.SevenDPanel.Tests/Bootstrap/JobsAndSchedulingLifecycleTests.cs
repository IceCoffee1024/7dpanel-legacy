using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.DependencyInjection;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Bootstrap
{
    public sealed class JobsAndSchedulingLifecycleTests
    {
        [Fact]
        public void Starts_worker_then_scheduler_then_http_and_stops_in_reverse_order()
        {
            var events = new List<string>();
            var startup = RecordingStartup(events);
            var worker = RecordingLoop("worker", events);
            var scheduler = RecordingLoop("scheduler", events);
            var inner = new RecordingRuntime(events);
            using var runtime = new JobsAndSchedulingRuntime(
                startup,
                worker,
                scheduler,
                inner,
                TimeSpan.FromSeconds(1));

            runtime.Start();
            runtime.Stop();

            Assert.Equal(
                new[]
                {
                    "restore",
                    "migration",
                    "reconcile",
                    "worker:start",
                    "scheduler:start",
                    "http:start",
                    "http:stop",
                    "scheduler:stop",
                    "worker:stop"
                },
                events);
        }

        [Fact]
        public void Startup_failure_stops_only_components_that_were_started()
        {
            var events = new List<string>();
            var startup = RecordingStartup(events);
            var worker = RecordingLoop("worker", events);
            var scheduler = RecordingLoop("scheduler", events);
            var inner = new RecordingRuntime(events, failStart: true);
            using var runtime = new JobsAndSchedulingRuntime(
                startup,
                worker,
                scheduler,
                inner,
                TimeSpan.FromSeconds(1));

            Assert.Throws<InvalidOperationException>(() => runtime.Start());

            Assert.Equal(
                new[]
                {
                    "restore",
                    "migration",
                    "reconcile",
                    "worker:start",
                    "scheduler:start",
                    "http:start",
                    "scheduler:stop",
                    "worker:stop"
                },
                events);
        }

        [Fact]
        public void Startup_step_failure_does_not_stop_components_that_never_started()
        {
            var events = new List<string>();
            var startup = new PendingRestoreStartupStep(
                () =>
                {
                    events.Add("restore");
                    throw new InvalidOperationException("restore failed");
                },
                () => events.Add("migration"),
                () => events.Add("reconcile"));
            using var runtime = new JobsAndSchedulingRuntime(
                startup,
                RecordingLoop("worker", events),
                RecordingLoop("scheduler", events),
                new RecordingRuntime(events),
                TimeSpan.FromSeconds(1));

            Assert.Throws<InvalidOperationException>(() => runtime.Start());

            Assert.Equal(new[] { "restore" }, events);
        }

        [Fact]
        public void Recovery_finishes_before_background_work_and_http_are_exposed()
        {
            var events = new List<string>();
            var runningJobStillExists = true;
            var scheduleRuns = new HashSet<string> { "schedule-1:2026-07-27T00:00:00Z" };
            var receiptPending = true;
            var startup = new PendingRestoreStartupStep(
                () => events.Add("restore:applied"),
                () => events.Add("database:migrated"),
                () =>
                {
                    events.Add("receipt:reconciled");
                    receiptPending = false;
                });
            using var runtime = new JobsAndSchedulingRuntime(
                startup,
                cancellationToken =>
                {
                    Assert.False(receiptPending);
                    Assert.True(runningJobStillExists);
                    events.Add("worker:observed-durable-running-job");
                    return WaitUntilCancelled(cancellationToken);
                },
                cancellationToken =>
                {
                    Assert.False(receiptPending);
                    Assert.False(scheduleRuns.Add(
                        "schedule-1:2026-07-27T00:00:00Z"));
                    events.Add("scheduler:kept-existing-run-once");
                    return WaitUntilCancelled(cancellationToken);
                },
                new RecordingRuntime(events, () => Assert.False(receiptPending)),
                TimeSpan.FromSeconds(1));

            runtime.Start();
            runtime.Stop();

            Assert.True(runningJobStillExists);
            Assert.Single(scheduleRuns);
            Assert.False(receiptPending);
            Assert.Equal(
                new[]
                {
                    "restore:applied",
                    "database:migrated",
                    "receipt:reconciled",
                    "worker:observed-durable-running-job",
                    "scheduler:kept-existing-run-once",
                    "http:start",
                    "http:stop"
                },
                events);
        }

        [Fact]
        public void Game_ready_still_flows_through_the_existing_runtime_chain_after_stop()
        {
            var events = new List<string>();
            var inner = new RecordingRuntime(events);
            using var runtime = new JobsAndSchedulingRuntime(
                RecordingStartup(events),
                RecordingLoop("worker", events),
                RecordingLoop("scheduler", events),
                inner,
                TimeSpan.FromSeconds(1));

            runtime.Start();
            runtime.Stop();
            runtime.MarkGameReady();

            Assert.Equal(1, inner.MarkGameReadyCalls);
        }

        [Fact]
        public void Stop_timeout_keeps_loop_ownership_until_a_retry_can_finish()
        {
            var events = new List<string>();
            var releaseLoops = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var inner = new RecordingRuntime(events);
            using var runtime = new JobsAndSchedulingRuntime(
                RecordingStartup(events),
                _ => releaseLoops.Task,
                _ => releaseLoops.Task,
                inner,
                TimeSpan.FromMilliseconds(25));
            runtime.Start();

            Assert.Throws<AggregateException>(() => runtime.Stop());
            Assert.Throws<AggregateException>(() => runtime.Stop());
            Assert.Equal(1, inner.StopCalls);

            releaseLoops.SetResult(true);
            runtime.Stop();
        }

        [Fact]
        public void Pending_restore_step_runs_before_migration_and_reconciliation()
        {
            var events = new List<string>();
            var step = new PendingRestoreStartupStep(
                () => events.Add("restore"),
                () => events.Add("migration"),
                () => events.Add("reconcile"));

            step.Execute();

            Assert.Equal(new[] { "restore", "migration", "reconcile" }, events);
        }

        private static Func<CancellationToken, Task> RecordingLoop(
            string name,
            ICollection<string> events)
        {
            return async cancellationToken =>
            {
                events.Add(name + ":start");
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    events.Add(name + ":stop");
                }
            };
        }

        private static PendingRestoreStartupStep RecordingStartup(
            ICollection<string> events) => new PendingRestoreStartupStep(
                () => events.Add("restore"),
                () => events.Add("migration"),
                () => events.Add("reconcile"));

        private static async Task WaitUntilCancelled(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly ICollection<string> events;
            private readonly bool failStart;
            private readonly Action? assertStart;

            public RecordingRuntime(
                ICollection<string> events,
                bool failStart = false,
                Action? assertStart = null)
            {
                this.events = events;
                this.failStart = failStart;
                this.assertStart = assertStart;
            }

            public RecordingRuntime(ICollection<string> events, Action assertStart)
                : this(events, false, assertStart)
            {
            }

            public int MarkGameReadyCalls { get; private set; }
            public int StopCalls { get; private set; }

            public void Start()
            {
                assertStart?.Invoke();
                events.Add("http:start");
                if (failStart) throw new InvalidOperationException("http failed");
            }

            public void MarkGameReady()
            {
                MarkGameReadyCalls++;
            }

            public void Stop()
            {
                StopCalls++;
                events.Add("http:stop");
            }
        }
    }
}
