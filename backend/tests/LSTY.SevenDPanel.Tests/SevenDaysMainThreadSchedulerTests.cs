using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysMainThreadSchedulerTests
    {
        [Fact]
        public async Task Request_executes_on_dispatcher_and_returns_value()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            scheduler.Start();

            var replyTask = scheduler.RequestAsync("server-status", () => 42, TimeSpan.FromSeconds(1));

            Assert.False(replyTask.IsCompleted);
            Assert.Equal(1, dispatcher.PendingCount);
            dispatcher.RunNext();

            var reply = await replyTask;
            Assert.Equal(MainThreadRequestOutcome.Succeeded, reply.Outcome);
            Assert.Equal(42, reply.Value);
        }

        [Fact]
        public async Task Burst_preserves_fifo_and_posts_one_pump_at_a_time()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 3);
            var order = new List<int>();
            scheduler.Start();

            var first = scheduler.RequestAsync("first", () => { order.Add(1); return 1; }, TimeSpan.FromSeconds(1));
            var second = scheduler.RequestAsync("second", () => { order.Add(2); return 2; }, TimeSpan.FromSeconds(1));
            var third = scheduler.RequestAsync("third", () => { order.Add(3); return 3; }, TimeSpan.FromSeconds(1));

            Assert.Equal(1, dispatcher.PendingCount);
            dispatcher.RunNext();
            Assert.Equal(new[] { 1 }, order);
            Assert.Equal(1, dispatcher.PendingCount);
            Assert.False(second.IsCompleted);

            dispatcher.RunNext();
            Assert.Equal(new[] { 1, 2 }, order);
            Assert.Equal(1, dispatcher.PendingCount);
            Assert.False(third.IsCompleted);

            dispatcher.RunNext();
            Assert.Equal(new[] { 1, 2, 3 }, order);
            Assert.Equal(0, dispatcher.PendingCount);
            Assert.Equal(MainThreadRequestOutcome.Succeeded, (await first).Outcome);
            Assert.Equal(MainThreadRequestOutcome.Succeeded, (await second).Outcome);
            Assert.Equal(MainThreadRequestOutcome.Succeeded, (await third).Outcome);
        }

        [Fact]
        public async Task Unavailable_is_returned_when_not_ready_full_or_stopping()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);

            var notReady = await scheduler.RequestAsync("not-ready", () => 1, TimeSpan.FromSeconds(1));
            Assert.Equal(MainThreadRequestOutcome.Unavailable, notReady.Outcome);
            Assert.Equal(MainThreadUnavailableReason.NotReady, notReady.UnavailableReason);

            scheduler.Start();
            var accepted = scheduler.RequestAsync("accepted", () => 1, TimeSpan.FromSeconds(1));
            var full = await scheduler.RequestAsync("full", () => 2, TimeSpan.FromSeconds(1));
            Assert.Equal(MainThreadRequestOutcome.Unavailable, full.Outcome);
            Assert.Equal(MainThreadUnavailableReason.CapacityExceeded, full.UnavailableReason);

            scheduler.Stop();
            var stopping = await scheduler.RequestAsync("stopping", () => 3, TimeSpan.FromSeconds(1));
            Assert.Equal(MainThreadRequestOutcome.Unavailable, stopping.Outcome);
            Assert.Equal(MainThreadUnavailableReason.Stopping, stopping.UnavailableReason);
            Assert.Equal(MainThreadRequestOutcome.Unavailable, (await accepted).Outcome);
        }

        [Fact]
        public async Task Canceled_tombstone_holds_capacity_until_pump_removes_it()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var executed = false;
            scheduler.Start();

            var canceledTask = RequestWithCancellation(
                scheduler,
                "canceled",
                () => { executed = true; return 1; },
                TimeSpan.FromSeconds(1),
                cancellation);
            cancellation.Cancel();

            Assert.Equal(MainThreadRequestOutcome.Canceled, (await canceledTask).Outcome);
            var full = await scheduler.RequestAsync("still-full", () => 2, TimeSpan.FromSeconds(1));
            Assert.Equal(MainThreadUnavailableReason.CapacityExceeded, full.UnavailableReason);

            dispatcher.RunNext();
            Assert.False(executed);

            var accepted = scheduler.RequestAsync("accepted", () => 3, TimeSpan.FromSeconds(1));
            dispatcher.RunNext();
            Assert.Equal(MainThreadRequestOutcome.Succeeded, (await accepted).Outcome);
        }

        [Fact]
        public async Task Queued_timeout_guarantees_operation_does_not_execute()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            var executed = false;
            scheduler.Start();

            var replyTask = scheduler.RequestAsync(
                "timeout",
                () => { executed = true; return 1; },
                TimeSpan.FromSeconds(1));
            deadlines.FireNext();

            Assert.Equal(MainThreadRequestOutcome.TimedOut, (await replyTask).Outcome);
            dispatcher.RunNext();
            Assert.False(executed);
        }

        [Fact]
        public async Task Cancellation_after_start_returns_unknown_and_operation_finishes()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var testCancellation = TestContext.Current.CancellationToken;
            scheduler.Start();

            var replyTask = RequestWithCancellation(
                scheduler,
                "running-cancel",
                () => { entered.Set(); release.Wait(testCancellation); return 1; },
                TimeSpan.FromSeconds(1),
                cancellation);
            var pumpTask = Task.Run(dispatcher.RunNext, testCancellation);

            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5), testCancellation));
                cancellation.Cancel();
                Assert.Equal(MainThreadRequestOutcome.Unknown, (await replyTask).Outcome);
            }
            finally
            {
                release.Set();
                await pumpTask;
            }
        }

        [Fact]
        public async Task Timeout_after_start_returns_unknown_and_operation_finishes()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var testCancellation = TestContext.Current.CancellationToken;
            scheduler.Start();

            var replyTask = scheduler.RequestAsync(
                "running-timeout",
                () => { entered.Set(); release.Wait(testCancellation); return 1; },
                TimeSpan.FromSeconds(1));
            var pumpTask = Task.Run(dispatcher.RunNext, testCancellation);

            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5), testCancellation));
                deadlines.FireNext();
                Assert.Equal(MainThreadRequestOutcome.Unknown, (await replyTask).Outcome);
            }
            finally
            {
                release.Set();
                await pumpTask;
            }
        }

        [Fact]
        public async Task Stop_is_idempotent_completes_pending_and_leaves_posted_pump_as_noop()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 2);
            var executed = false;
            scheduler.Start();
            var first = scheduler.RequestAsync("first", () => { executed = true; return 1; }, TimeSpan.FromSeconds(1));
            var second = scheduler.RequestAsync("second", () => { executed = true; return 2; }, TimeSpan.FromSeconds(1));

            scheduler.Stop();
            scheduler.Stop();

            Assert.Equal(MainThreadRequestOutcome.Unavailable, (await first).Outcome);
            Assert.Equal(MainThreadRequestOutcome.Unavailable, (await second).Outcome);
            dispatcher.RunNext();
            Assert.False(executed);
            Assert.Equal(0, dispatcher.PendingCount);
        }

        [Fact]
        public async Task Stop_after_start_returns_unknown()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 1);
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var testCancellation = TestContext.Current.CancellationToken;
            scheduler.Start();
            var replyTask = scheduler.RequestAsync(
                "running-stop",
                () => { entered.Set(); release.Wait(testCancellation); return 1; },
                TimeSpan.FromSeconds(1));
            var pumpTask = Task.Run(dispatcher.RunNext, testCancellation);

            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5), testCancellation));
                scheduler.Stop();
                Assert.Equal(MainThreadRequestOutcome.Unknown, (await replyTask).Outcome);
            }
            finally
            {
                release.Set();
                await pumpTask;
            }
        }

        [Fact]
        public async Task Exception_returns_failed_and_does_not_block_next_request()
        {
            var dispatcher = new ControllableDispatcher();
            var deadlines = new ControllableDeadlineScheduler();
            var scheduler = CreateScheduler(dispatcher, deadlines, capacity: 2);
            scheduler.Start();
            var failedTask = scheduler.RequestAsync<int>(
                "failed",
                () => throw new InvalidOperationException("boom"),
                TimeSpan.FromSeconds(1));
            var nextTask = scheduler.RequestAsync("next", () => 7, TimeSpan.FromSeconds(1));

            dispatcher.RunNext();
            var failed = await failedTask;
            Assert.Equal(MainThreadRequestOutcome.Failed, failed.Outcome);
            Assert.IsType<InvalidOperationException>(failed.Exception);

            dispatcher.RunNext();
            Assert.Equal(MainThreadRequestOutcome.Succeeded, (await nextTask).Outcome);
        }

        [Fact]
        public async Task Dispatcher_failure_stops_scheduler_and_completes_accepted_request()
        {
            var scheduler = CreateScheduler(
                new ThrowingDispatcher(),
                new ControllableDeadlineScheduler(),
                capacity: 1);
            scheduler.Start();

            var accepted = await scheduler.RequestAsync("accepted", () => 1, TimeSpan.FromSeconds(1));
            var future = await scheduler.RequestAsync("future", () => 2, TimeSpan.FromSeconds(1));

            Assert.Equal(MainThreadRequestOutcome.Unavailable, accepted.Outcome);
            Assert.Equal(MainThreadUnavailableReason.Stopping, accepted.UnavailableReason);
            Assert.Equal(MainThreadRequestOutcome.Unavailable, future.Outcome);
            Assert.Equal(MainThreadUnavailableReason.Stopping, future.UnavailableReason);
        }

        [Fact]
        public void Deadline_registration_failure_is_propagated()
        {
            var scheduler = CreateScheduler(
                new ControllableDispatcher(),
                new ThrowingDeadlineScheduler(),
                capacity: 1);
            scheduler.Start();

            Action request = () =>
            {
                _ = scheduler.RequestAsync("deadline-failure", () => 1, TimeSpan.FromSeconds(1));
            };
            var exception = Assert.Throws<InvalidOperationException>(request);

            Assert.Equal("deadline unavailable", exception.Message);
        }

        private static SevenDaysMainThreadScheduler CreateScheduler(
            IMainThreadDispatcher dispatcher,
            IMainThreadDeadlineScheduler deadlines,
            int capacity)
        {
            return new SevenDaysMainThreadScheduler(dispatcher, deadlines, capacity);
        }

        private static Task<MainThreadReply<T>> RequestWithCancellation<T>(
            SevenDaysMainThreadScheduler scheduler,
            string operationName,
            Func<T> operation,
            TimeSpan timeout,
            CancellationTokenSource cancellation)
        {
#pragma warning disable xUnit1051 // This helper links explicit scheduler cancellation to the test cancellation token.
            return scheduler.RequestAsync(operationName, operation, timeout, cancellation.Token);
#pragma warning restore xUnit1051
        }

        private sealed class ControllableDispatcher : IMainThreadDispatcher
        {
            private readonly Queue<Action> actions = new Queue<Action>();

            public int PendingCount
            {
                get { lock (actions) return actions.Count; }
            }

            public void Post(string operationName, Action action)
            {
                lock (actions) actions.Enqueue(action);
            }

            public void RunNext()
            {
                Action action;
                lock (actions) action = actions.Dequeue();
                action();
            }
        }

        private sealed class ThrowingDispatcher : IMainThreadDispatcher
        {
            public void Post(string operationName, Action action)
            {
                throw new InvalidOperationException("dispatcher unavailable");
            }
        }

        private sealed class ThrowingDeadlineScheduler : IMainThreadDeadlineScheduler
        {
            public IDisposable Schedule(TimeSpan timeout, Action callback)
            {
                throw new InvalidOperationException("deadline unavailable");
            }
        }

        private sealed class ControllableDeadlineScheduler : IMainThreadDeadlineScheduler
        {
            private readonly Queue<Registration> registrations = new Queue<Registration>();

            public IDisposable Schedule(TimeSpan timeout, Action callback)
            {
                var registration = new Registration(callback);
                lock (registrations) registrations.Enqueue(registration);
                return registration;
            }

            public void FireNext()
            {
                Registration registration;
                lock (registrations) registration = registrations.Dequeue();
                registration.Fire();
            }

            private sealed class Registration : IDisposable
            {
                private Action? callback;

                public Registration(Action callback)
                {
                    this.callback = callback;
                }

                public void Fire()
                {
                    Interlocked.Exchange(ref callback, null)?.Invoke();
                }

                public void Dispose()
                {
                    Interlocked.Exchange(ref callback, null);
                }
            }
        }
    }
}
