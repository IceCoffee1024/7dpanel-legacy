using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Application.Schedules;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class BackgroundSchedulerTests
    {
        [Fact]
        public async Task Startup_ticks_immediately_and_cancellation_stops_future_claims()
        {
            var store = new RecordingScheduleStore();
            using var cancellation = new CancellationTokenSource();
            var now = Utc(10);
            store.OnClaim = cancellation.Cancel;
            var scheduler = new BackgroundScheduler(
                store,
                " scheduler-1 ",
                () => now,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(8));

            await scheduler.RunAsync(cancellation.Token);

            Assert.Equal(1, store.ClaimCalls);
            Assert.Equal("scheduler-1", store.LastOwnerId);
            Assert.Equal(now, store.LastNow);
        }

        [Fact]
        public async Task Cancellation_before_start_returns_without_claiming()
        {
            var store = new RecordingScheduleStore();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var scheduler = Scheduler(store);

            await scheduler.RunAsync(cancellation.Token);

            Assert.Equal(0, store.ClaimCalls);
        }

        [Fact]
        public async Task A_scheduler_instance_runs_only_one_poll_loop_at_a_time()
        {
            var store = new RecordingScheduleStore();
            using var cancellation = new CancellationTokenSource();
            var scheduler = new BackgroundScheduler(
                store,
                "scheduler-1",
                () => Utc(10),
                TimeSpan.FromDays(1),
                TimeSpan.FromDays(1));
            var firstRun = scheduler.RunAsync(cancellation.Token);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scheduler.RunAsync(cancellation.Token));

            Assert.Equal("scheduler_already_running", exception.Message);
            cancellation.Cancel();
            await firstRun;
            Assert.Equal(1, store.ClaimCalls);
        }

        [Fact]
        public async Task Claim_failures_retry_with_a_capped_backoff()
        {
            var store = new RecordingScheduleStore { FailuresRemaining = 11 };
            using var cancellation = new CancellationTokenSource();
            store.OnClaim = () =>
            {
                if (store.ClaimCalls == 12) cancellation.Cancel();
            };
            var scheduler = new BackgroundScheduler(
                store,
                "scheduler-1",
                () => Utc(10),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(10));
            var elapsed = Stopwatch.StartNew();

            await scheduler.RunAsync(cancellation.Token);

            elapsed.Stop();
            Assert.Equal(12, store.ClaimCalls);
            Assert.InRange(
                elapsed.Elapsed,
                TimeSpan.FromMilliseconds(40),
                TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void Tick_rejects_a_non_utc_clock_before_claiming()
        {
            var store = new RecordingScheduleStore();
            var scheduler = new BackgroundScheduler(
                store,
                "scheduler-1",
                () => new DateTimeOffset(2026, 7, 26, 8, 10, 0, TimeSpan.FromHours(8)),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(8));

            var exception = Assert.Throws<InvalidOperationException>(
                () => scheduler.Tick(TestContext.Current.CancellationToken));

            Assert.Equal("scheduler_clock_not_utc", exception.Message);
            Assert.Equal(0, store.ClaimCalls);
        }

        private static BackgroundScheduler Scheduler(RecordingScheduleStore store) =>
            new BackgroundScheduler(
                store,
                "scheduler-1",
                () => Utc(10),
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(8));

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private sealed class RecordingScheduleStore : IScheduleStore
        {
            public int ClaimCalls { get; private set; }
            public int FailuresRemaining { get; set; }
            public Action? OnClaim { get; set; }
            public DateTimeOffset? LastNow { get; private set; }
            public string? LastOwnerId { get; private set; }

            public IReadOnlyList<ScheduleRecord> List() =>
                Array.Empty<ScheduleRecord>();

            public ScheduleRecord? Get(Guid scheduleId) =>
                throw new NotSupportedException();

            public ScheduleRecord Upsert(ScheduleDefinition definition) =>
                throw new NotSupportedException();

            public bool Delete(Guid scheduleId, long expectedRowVersion) =>
                throw new NotSupportedException();

            public IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId)
            {
                ClaimCalls++;
                LastNow = now;
                LastOwnerId = ownerId;
                OnClaim?.Invoke();
                if (FailuresRemaining > 0)
                {
                    FailuresRemaining--;
                    throw new InvalidOperationException("transient_claim_failure");
                }
                return Array.Empty<ScheduleRecord>();
            }

            public void RecordOutcome(ScheduleRunOutcome outcome) =>
                throw new NotSupportedException();
        }
    }
}
