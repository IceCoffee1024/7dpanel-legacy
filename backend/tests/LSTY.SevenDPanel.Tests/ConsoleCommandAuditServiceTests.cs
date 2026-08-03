using System;
using System.Collections.Generic;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Runtime.ConsoleCommands;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Application")]
    public sealed class ConsoleCommandAuditServiceTests
    {
        [Fact]
        public void Full_queue_is_rejected_without_blocking_and_recovery_writes_gap_first()
        {
            var store = new BlockingAuditStore();
            var messages = new List<string>();
            using var service = new ConsoleCommandAuditService(
                store,
                1,
                TimeSpan.FromSeconds(5),
                _ => new TestSubscription(),
                messages.Add);
            service.Start();

            Assert.True(service.TryPublish(Observation("first")));
            Assert.True(store.AppendEntered.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
            Assert.True(service.TryPublish(Observation("second")));
            var startedAt = DateTime.UtcNow;
            Assert.False(service.TryPublish(Observation("dropped")));
            Assert.True(DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(1));

            store.ReleaseAppend.Set();
            Assert.True(SpinWait.SpinUntil(
                () => store.Entries.Count == 2,
                TimeSpan.FromSeconds(5)));
            Assert.True(service.TryPublish(Observation("after-gap")));
            Assert.True(SpinWait.SpinUntil(
                () => store.Entries.Count == 3,
                TimeSpan.FromSeconds(5)));
            service.Stop();

            Assert.Equal(new[] { "entry:first", "gap:1", "entry:second", "entry:after-gap" }, store.Calls);
            Assert.Equal(1L, service.DroppedFullCount);
            Assert.Equal(3L, service.ConsumedCount);
            Assert.Single(messages.FindAll(message => message.Contains("queue is full")));
        }

        [Fact]
        public void Store_failure_is_counted_and_later_entries_continue()
        {
            var store = new BlockingAuditStore { FailNextAppend = true };
            store.ReleaseAppend.Set();
            var messages = new List<string>();
            using var service = new ConsoleCommandAuditService(
                store,
                2,
                TimeSpan.FromSeconds(5),
                _ => new TestSubscription(),
                messages.Add);
            service.Start();

            Assert.True(service.TryPublish(Observation("first")));
            Assert.True(service.TryPublish(Observation("second")));
            Assert.True(SpinWait.SpinUntil(
                () => service.ConsumerFailureCount == 1 && service.ConsumedCount == 1,
                TimeSpan.FromSeconds(5)));
            service.Stop();

            Assert.Equal("second", Assert.Single(store.Entries).RawCommand);
            Assert.Equal(new[] { "gap:1", "entry:second" }, store.Calls);
            Assert.Single(messages.FindAll(message => message.Contains("persistence failed")));
        }

        [Fact]
        public void Stop_unsubscribes_then_drains_and_rejects_late_observations()
        {
            var subscription = new TestSubscription();
            var store = new BlockingAuditStore();
            store.ReleaseAppend.Set();
            using var service = new ConsoleCommandAuditService(
                store,
                2,
                TimeSpan.FromSeconds(5),
                _ => subscription);
            service.Start();
            Assert.True(service.TryPublish(Observation("accepted")));

            service.Stop();
            service.Stop();

            Assert.Equal(1, subscription.DisposeCount);
            Assert.False(service.TryPublish(Observation("late")));
            Assert.Equal("accepted", Assert.Single(store.Entries).RawCommand);
            Assert.Equal(1L, service.RejectedStoppingCount);
        }

        [Fact]
        public void Drain_timeout_can_be_retried_after_the_store_unblocks()
        {
            var store = new BlockingAuditStore();
            using var service = new ConsoleCommandAuditService(
                store,
                1,
                TimeSpan.FromMilliseconds(50),
                _ => new TestSubscription());
            service.Start();
            Assert.True(service.TryPublish(Observation("blocked")));
            Assert.True(store.AppendEntered.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            Assert.Throws<AggregateException>(() => service.Stop());
            store.ReleaseAppend.Set();
            Assert.True(SpinWait.SpinUntil(
                () => store.Entries.Count == 1,
                TimeSpan.FromSeconds(5)));

            service.Stop();

            Assert.Equal("blocked", Assert.Single(store.Entries).RawCommand);
        }

        [Fact]
        public void Stop_summary_reports_unrecovered_store_failure_gap()
        {
            var store = new BlockingAuditStore { FailNextAppend = true };
            store.ReleaseAppend.Set();
            var messages = new List<string>();
            using var service = new ConsoleCommandAuditService(
                store,
                1,
                TimeSpan.FromSeconds(5),
                _ => new TestSubscription(),
                messages.Add);
            service.Start();
            Assert.True(service.TryPublish(Observation("failed")));
            Assert.True(SpinWait.SpinUntil(
                () => service.ConsumerFailureCount == 1,
                TimeSpan.FromSeconds(5)));

            service.Stop();

            Assert.Contains(messages, message =>
                message.Contains("unrecoveredGaps=1") &&
                message.Contains("unrecoveredDropped=1"));
        }

        private static ConsoleCommandExecutionObservation Observation(string command)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            return new ConsoleCommandExecutionObservation(
                Guid.NewGuid().ToString("N"),
                command,
                new[] { command },
                new[] { command + "-output" },
                "local-game",
                null,
                startedAtUtc,
                startedAtUtc.AddMilliseconds(1),
                ConsoleCommandCompletionKind.Completed,
                null);
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class BlockingAuditStore : IConsoleCommandAuditStore
        {
            private readonly object sync = new object();

            public ManualResetEventSlim AppendEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim ReleaseAppend { get; } = new ManualResetEventSlim();
            public bool FailNextAppend { get; set; }
            public List<ConsoleCommandAuditEntry> Entries { get; } = new List<ConsoleCommandAuditEntry>();
            public List<string> Calls { get; } = new List<string>();

            public void Append(ConsoleCommandAuditEntry entry)
            {
                AppendEntered.Set();
                ReleaseAppend.Wait(TestContext.Current.CancellationToken);
                lock (sync)
                {
                    if (FailNextAppend)
                    {
                        FailNextAppend = false;
                        throw new InvalidOperationException("store failed");
                    }
                    Entries.Add(entry);
                    Calls.Add("entry:" + entry.RawCommand);
                }
            }

            public void AppendGap(ConsoleCommandAuditGap gap)
            {
                lock (sync) Calls.Add("gap:" + gap.DroppedCount);
            }
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class TestSubscription : IDisposable
        {
            private int disposed;
            public int DisposeCount => Volatile.Read(ref disposed);
            public void Dispose() => Interlocked.Increment(ref disposed);
        }
    }
}