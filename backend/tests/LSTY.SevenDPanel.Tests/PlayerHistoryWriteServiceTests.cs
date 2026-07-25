using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerHistoryWriteServiceTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(2);

        [Fact]
        public void Producer_never_calls_the_store_and_a_full_queue_preserves_older_snapshots()
        {
            var store = new BlockingStore();
            using var service = CreateService(store, queueCapacity: 1);
            var first = CreateSnapshot("First");
            var second = CreateSnapshot("Second");
            var dropped = CreateSnapshot("Dropped");
            var producerThread = Environment.CurrentManagedThreadId;

            service.Start();
            try
            {
                Assert.True(service.TryRecord(first));
                Assert.True(store.FirstAppendStarted.Wait(TestTimeout));

                Assert.True(service.TryRecord(second));
                Assert.False(service.TryRecord(dropped));
                Assert.All(store.AppendThreadIds, threadId => Assert.NotEqual(producerThread, threadId));

                store.ReleaseFirstAppend.Set();
                service.Stop();

                Assert.Equal(new[] { "First", "Second" }, store.AppendedNames);
                Assert.Equal(2, service.AcceptedCount);
                Assert.Equal(1, service.DroppedFullCount);
            }
            finally
            {
                store.ReleaseFirstAppend.Set();
                service.Stop();
            }
        }

        [Fact]
        public void Missing_crossplatform_identity_is_skipped_without_calling_the_store()
        {
            var store = new RecordingStore();
            using var service = CreateService(store);

            service.Start();
            Assert.False(service.TryRecord(CreateSnapshot("No EOS", includeCrossplatformIdentity: false)));
            service.Stop();

            Assert.Empty(store.AppendedNames);
            Assert.Equal(1, service.SkippedMissingCrossplatformIdCount);
            Assert.Equal(0, service.AcceptedCount);
        }

        [Fact]
        public void Store_failure_records_its_gap_before_the_next_snapshot()
        {
            var store = new FailFirstAppendStore();
            using var service = CreateService(store);

            service.Start();
            Assert.True(service.TryRecord(CreateSnapshot("Failed")));
            Assert.True(store.FirstAppendAttempted.Wait(TestTimeout));
            Assert.True(service.TryRecord(CreateSnapshot("Recovered")));
            service.Stop();

            var gap = Assert.Single(store.Events.Where(value => value == "gap:StoreFailure"));
            Assert.True(store.Events.IndexOf(gap) < store.Events.IndexOf("append:Recovered"));
            Assert.Equal(1, service.StoreFailureCount);
            Assert.Equal(1, service.PersistedCount);
        }

        [Fact]
        public void All_pending_gap_reasons_are_persisted_before_the_recovered_snapshot()
        {
            var store = new StoreFailureThenQueueFullStore();
            using var service = CreateService(store, queueCapacity: 1);

            service.Start();
            try
            {
                Assert.True(service.TryRecord(CreateSnapshot("Failed")));
                Assert.True(store.FailedAppendAttempted.Wait(TestTimeout));
                Assert.True(service.TryRecord(CreateSnapshot("Recovered")));
                Assert.True(store.FirstGapStarted.Wait(TestTimeout));
                Assert.True(service.TryRecord(CreateSnapshot("Queued")));
                Assert.False(service.TryRecord(CreateSnapshot("Queue full")));

                store.ReleaseFirstGap.Set();
                service.Stop();

                var recoveredIndex = store.Events.IndexOf("append:Recovered");
                var queueFullGapIndex = store.Events.IndexOf("gap:QueueFull");
                var storeFailureGapIndex = store.Events.IndexOf("gap:StoreFailure");
                Assert.True(recoveredIndex >= 0);
                Assert.True(queueFullGapIndex >= 0);
                Assert.True(storeFailureGapIndex >= 0);
                Assert.True(queueFullGapIndex < recoveredIndex);
                Assert.True(storeFailureGapIndex < recoveredIndex);
            }
            finally
            {
                store.ReleaseFirstGap.Set();
                service.Stop();
            }
        }

        [Fact]
        public void Empty_consumer_runs_compaction_on_its_background_worker()
        {
            var store = new RecordingStore();
            using var service = CreateService(store);
            var producerThread = Environment.CurrentManagedThreadId;

            service.Start();
            try
            {
                Assert.True(store.CompactCalled.Wait(TestTimeout));
                Assert.Equal(1000, store.CompactMaximumDeletes);
                Assert.NotEqual(producerThread, store.CompactThreadId);
            }
            finally
            {
                service.Stop();
            }
        }

        private static PlayerHistoryWriteService CreateService(IPlayerHistoryStore store, int queueCapacity = 4) =>
            new PlayerHistoryWriteService(
                store,
                queueCapacity,
                TimeSpan.FromMilliseconds(250),
                () => new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero));

        private static PlayerSnapshot CreateSnapshot(string name, bool includeCrossplatformIdentity = true) =>
            new PlayerSnapshot(
                7,
                name,
                new PlayerPlatformIdentity("Steam_76561198000000000", "Steam"),
                includeCrossplatformIdentity
                    ? new PlayerPlatformIdentity("EOS_0002d12af0fe4add9c7de0fbc238d431", "EOS")
                    : null,
                PlayerDeviceType.Windows,
                null,
                0,
                null,
                null,
                1000,
                new PlayerPosition(1, 2, 3),
                false,
                100,
                100,
                1,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero));

        private class RecordingStore : IPlayerHistoryStore
        {
            private readonly object sync = new object();
            private readonly List<string> appendedNames = new List<string>();

            public ManualResetEventSlim CompactCalled { get; } = new ManualResetEventSlim();

            public int CompactMaximumDeletes { get; private set; }

            public int CompactThreadId { get; private set; }

            public IReadOnlyList<string> AppendedNames
            {
                get
                {
                    lock (sync) return appendedNames.ToArray();
                }
            }

            public virtual void Append(PlayerSnapshot snapshot)
            {
                lock (sync) appendedNames.Add(snapshot.Name);
            }

            public virtual void AppendGap(PlayerHistoryGap gap)
            {
            }

            public int Compact(DateTimeOffset utcNow, int maximumDeletes)
            {
                CompactThreadId = Environment.CurrentManagedThreadId;
                CompactMaximumDeletes = maximumDeletes;
                CompactCalled.Set();
                return 0;
            }

            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) =>
                throw new NotSupportedException();

            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) =>
                throw new NotSupportedException();

            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) =>
                throw new NotSupportedException();
        }

        private sealed class BlockingStore : RecordingStore
        {
            private readonly object sync = new object();
            private readonly List<int> appendThreadIds = new List<int>();
            private int appendCount;

            public ManualResetEventSlim FirstAppendStarted { get; } = new ManualResetEventSlim();

            public ManualResetEventSlim ReleaseFirstAppend { get; } = new ManualResetEventSlim();

            public IReadOnlyList<int> AppendThreadIds
            {
                get
                {
                    lock (sync) return appendThreadIds.ToArray();
                }
            }

            public override void Append(PlayerSnapshot snapshot)
            {
                lock (sync) appendThreadIds.Add(Environment.CurrentManagedThreadId);
                if (Interlocked.Increment(ref appendCount) == 1)
                {
                    FirstAppendStarted.Set();
                    if (!ReleaseFirstAppend.Wait(TestTimeout))
                        throw new TimeoutException("The test did not release the first append.");
                }

                base.Append(snapshot);
            }
        }

        private sealed class FailFirstAppendStore : RecordingStore
        {
            private int appendCount;

            public ManualResetEventSlim FirstAppendAttempted { get; } = new ManualResetEventSlim();

            public List<string> Events { get; } = new List<string>();

            public override void Append(PlayerSnapshot snapshot)
            {
                Events.Add("append:" + snapshot.Name);
                if (Interlocked.Increment(ref appendCount) == 1)
                {
                    FirstAppendAttempted.Set();
                    throw new InvalidOperationException("simulated store failure");
                }

                base.Append(snapshot);
            }

            public override void AppendGap(PlayerHistoryGap gap)
            {
                Events.Add("gap:" + gap.Reason);
            }
        }

        private sealed class StoreFailureThenQueueFullStore : RecordingStore
        {
            private int appendCount;

            public ManualResetEventSlim FailedAppendAttempted { get; } = new ManualResetEventSlim();

            public ManualResetEventSlim FirstGapStarted { get; } = new ManualResetEventSlim();

            public ManualResetEventSlim ReleaseFirstGap { get; } = new ManualResetEventSlim();

            public List<string> Events { get; } = new List<string>();

            public override void Append(PlayerSnapshot snapshot)
            {
                Events.Add("append:" + snapshot.Name);
                var count = Interlocked.Increment(ref appendCount);
                if (count == 1)
                {
                    FailedAppendAttempted.Set();
                    throw new InvalidOperationException("simulated store failure");
                }

                base.Append(snapshot);
            }

            public override void AppendGap(PlayerHistoryGap gap)
            {
                Events.Add("gap:" + gap.Reason);
                if (gap.Reason == PlayerHistoryGapReason.StoreFailure && !FirstGapStarted.IsSet)
                {
                    FirstGapStarted.Set();
                    if (!ReleaseFirstGap.Wait(TestTimeout))
                        throw new TimeoutException("The test did not release the first gap.");
                }
            }
        }

    }
}
