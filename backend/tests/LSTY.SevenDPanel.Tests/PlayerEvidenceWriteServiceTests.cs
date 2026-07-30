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
    public sealed class PlayerEvidenceWriteServiceTests
    {
        [Fact]
        public void Producer_is_nonblocking_and_queue_full_creates_separate_inventory_and_skill_gaps()
        {
            var store = new BlockingStore();
            using var service = new PlayerEvidenceWriteService(
                store, 1, TimeSpan.FromSeconds(1), () => Utc(20));
            service.Start();

            Assert.True(service.TryRecord(Draft(1)));
            WaitFor(store.Entered);
            Assert.True(service.TryRecord(Draft(2)));
            var elapsed = Stopwatch.StartNew();
            Assert.False(service.TryRecord(Draft(3)));
            elapsed.Stop();
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));

            store.Release.Set();
            service.Stop();

            Assert.Equal(new[] { Utc(1), Utc(2) }, store.InventorySnapshots.Select(value => value.ObservedAtUtc));
            Assert.Equal(new[] { Utc(1), Utc(2) }, store.SkillSnapshots.Select(value => value.ObservedAtUtc));
            var inventoryGap = Assert.Single(store.InventoryGaps);
            var skillGap = Assert.Single(store.SkillGaps);
            Assert.Equal(PlayerEvidenceWriteService.QueueFullReason, inventoryGap.Reason);
            Assert.Equal(PlayerEvidenceWriteService.QueueFullReason, skillGap.Reason);
            Assert.Equal(Utc(3), inventoryGap.StartedAtUtc);
            Assert.Equal(1, inventoryGap.EstimatedLostCount);
            Assert.Equal(1, skillGap.EstimatedLostCount);
            Assert.Equal(1, store.MaximumConcurrentCalls);
        }

        [Fact]
        public void Store_failures_are_aggregated_per_evidence_kind_before_recovery()
        {
            var store = new FailFirstSnapshotStore();
            using var service = new PlayerEvidenceWriteService(
                store, 4, TimeSpan.FromSeconds(1), () => Utc(20));
            service.Start();

            Assert.True(service.TryRecord(Draft(4)));
            Assert.True(service.TryRecord(Draft(5)));
            WaitFor(store.SnapshotsPersisted);
            service.Stop();

            Assert.Equal(new[] { Utc(5) }, store.InventorySnapshots.Select(value => value.ObservedAtUtc));
            Assert.Equal(new[] { Utc(5) }, store.SkillSnapshots.Select(value => value.ObservedAtUtc));
            Assert.Equal(PlayerEvidenceWriteService.StoreFailureReason, Assert.Single(store.InventoryGaps).Reason);
            Assert.Equal(PlayerEvidenceWriteService.StoreFailureReason, Assert.Single(store.SkillGaps).Reason);
            Assert.Equal(1, store.InventoryGaps[0].EstimatedLostCount);
            Assert.Equal(1, store.SkillGaps[0].EstimatedLostCount);
        }

        [Fact]
        public void Stop_rejects_new_input_then_bounds_drain_and_records_all_outstanding_kinds()
        {
            var store = new BlockingStore();
            using var service = new PlayerEvidenceWriteService(
                store, 2, TimeSpan.FromMilliseconds(50), () => Utc(20));
            service.Start();
            Assert.True(service.TryRecord(Draft(6)));
            WaitFor(store.Entered);
            Assert.True(service.TryRecord(Draft(7)));

            var elapsed = Stopwatch.StartNew();
            service.Stop();
            elapsed.Stop();

            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
            Assert.False(service.TryRecord(Draft(8)));
            store.Release.Set();
            WaitFor(store.DrainTimeoutGapsRecorded);

            var inventoryGap = store.InventoryGaps.Single(gap =>
                gap.Reason == PlayerEvidenceWriteService.DrainTimeoutReason);
            var skillGap = store.SkillGaps.Single(gap =>
                gap.Reason == PlayerEvidenceWriteService.DrainTimeoutReason);
            Assert.Equal(2, inventoryGap.EstimatedLostCount);
            Assert.Equal(2, skillGap.EstimatedLostCount);
        }

        [Fact]
        public void Same_player_server_and_observed_utc_is_idempotent()
        {
            var store = new RecordingStore();
            using var service = new PlayerEvidenceWriteService(
                store, 4, TimeSpan.FromSeconds(1), () => Utc(20));
            service.Start();
            var draft = Draft(9);

            Assert.True(service.TryRecord(draft));
            Assert.True(service.TryRecord(draft));
            WaitFor(store.SnapshotsPersisted);
            service.Stop();

            Assert.Single(store.InventorySnapshots);
            Assert.Single(store.SkillSnapshots);
        }

        [Fact]
        public void Persisted_session_subscription_publishes_only_successful_appends_and_can_be_cancelled()
        {
            var store = new BlockingFailFirstSessionStore();
            using var service = new PlayerEvidenceWriteService(
                store, 4, TimeSpan.FromSeconds(1), () => Utc(20));
            var publishedSessionIds = new List<long>();
            var published = new ManualResetEventSlim();
            var producerThread = Environment.CurrentManagedThreadId;
            var publisherThread = producerThread;
            var subscription = service.SubscribePersisted(session =>
            {
                lock (publishedSessionIds) publishedSessionIds.Add(session.SessionId);
                publisherThread = Environment.CurrentManagedThreadId;
                published.Set();
            });

            service.Start();
            try
            {
                Assert.True(service.TryRecord(SessionDraft(1)));
                WaitFor(store.FirstAppendStarted);
                lock (publishedSessionIds) Assert.Empty(publishedSessionIds);

                store.ReleaseFirstAppend.Set();
                WaitFor(store.FirstAppendFailed);
                Assert.True(service.TryRecord(SessionDraft(2)));
                WaitFor(published);

                subscription.Dispose();
                Assert.True(service.TryRecord(SessionDraft(3)));
                service.Stop();

                lock (publishedSessionIds) Assert.Equal(new long[] { 2 }, publishedSessionIds);
                Assert.NotEqual(producerThread, publisherThread);
            }
            finally
            {
                store.ReleaseFirstAppend.Set();
                subscription.Dispose();
                service.Stop();
            }
        }

        private static PlayerEvidenceDraft Draft(int minute) =>
            SevenDaysPlayerEvidenceSnapshotReader.CreateDraft(
                "EOS_player",
                "local",
                "world-a",
                Utc(minute),
                "V 3.0.1 (b4)",
                GameResourceCatalogReadResult.Unavailable(),
                new[]
                {
                    new InventoryItemScalar(
                        "bag", 0, "resourceFood", minute, null, null, Array.Empty<string>())
                },
                new PlayerPosition(minute, 2, 3),
                minute,
                minute,
                new[]
                {
                    new PlayerSkillValue(
                        "perkAllowed", SkillValueState.Known, minute, 0, 100, 1, null)
                });

        private static PlayerEvidenceDraft SessionDraft(int minute) =>
            new PlayerEvidenceDraft(
                "EOS_player",
                "local",
                "world-a",
                Utc(minute),
                new PlayerEvidenceSessionDraft(
                    minute,
                    Utc(minute),
                    null,
                    null,
                    null,
                    PlayerProfileSectionState.Available),
                null,
                null,
                null,
                null);

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 10, minute, 0, TimeSpan.Zero);

        private static void WaitFor(ManualResetEventSlim signal) =>
            signal.Wait(TestContext.Current.CancellationToken);

        private class RecordingStore : IPlayerEvidenceStore
        {
            private readonly object sync = new object();
            private int activeCalls;
            private int maximumConcurrentCalls;
            private int inventorySnapshotPersisted;
            private int skillSnapshotPersisted;
            private readonly List<PlayerInventorySnapshot> inventorySnapshots = new List<PlayerInventorySnapshot>();
            private readonly List<PlayerSkillSnapshot> skillSnapshots = new List<PlayerSkillSnapshot>();
            private readonly List<PlayerEvidenceGap> inventoryGaps = new List<PlayerEvidenceGap>();
            private readonly List<PlayerEvidenceGap> skillGaps = new List<PlayerEvidenceGap>();
            private readonly List<PlayerSession> sessions = new List<PlayerSession>();

            public IReadOnlyList<PlayerInventorySnapshot> InventorySnapshots { get { lock (sync) return inventorySnapshots.ToArray(); } }
            public IReadOnlyList<PlayerSkillSnapshot> SkillSnapshots { get { lock (sync) return skillSnapshots.ToArray(); } }
            public IReadOnlyList<PlayerEvidenceGap> InventoryGaps { get { lock (sync) return inventoryGaps.ToArray(); } }
            public IReadOnlyList<PlayerEvidenceGap> SkillGaps { get { lock (sync) return skillGaps.ToArray(); } }
            public IReadOnlyList<PlayerSession> Sessions { get { lock (sync) return sessions.ToArray(); } }
            public int MaximumConcurrentCalls => Volatile.Read(ref maximumConcurrentCalls);
            public ManualResetEventSlim SnapshotsPersisted { get; } = new ManualResetEventSlim(false);

            public virtual void AppendSession(PlayerSession session) =>
                Call(() => { lock (sync) sessions.Add(session); });
            public virtual void AppendActivity(PlayerActivityEvent activity) => Call(() => { });
            public virtual void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                Call(() => { lock (sync) inventorySnapshots.Add(snapshot); });
                Interlocked.Exchange(ref inventorySnapshotPersisted, 1);
                SignalSnapshotsPersisted();
            }

            public virtual void AppendSkillSnapshot(PlayerSkillSnapshot snapshot)
            {
                Call(() => { lock (sync) skillSnapshots.Add(snapshot); });
                Interlocked.Exchange(ref skillSnapshotPersisted, 1);
                SignalSnapshotsPersisted();
            }
            public virtual void AppendInventoryGap(PlayerEvidenceGap gap) =>
                Call(() => { lock (sync) inventoryGaps.Add(gap); });
            public virtual void AppendSkillGap(PlayerEvidenceGap gap) =>
                Call(() => { lock (sync) skillGaps.Add(gap); });

            public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query) => Array.Empty<PlayerSession>();
            public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query) => Array.Empty<PlayerActivityEvent>();
            public PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query) =>
                new PlayerInventorySnapshotsPage(Array.Empty<PlayerInventorySnapshot>(), null, Array.Empty<PlayerEvidenceGap>());
            public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query) =>
                new PlayerSkillSnapshotsPage(Array.Empty<PlayerSkillSnapshot>(), null, Array.Empty<PlayerEvidenceGap>());
            public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query) => InventoryGaps;
            public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query) => SkillGaps;
            public void Compact(PlayerEvidenceCompactionRequest request) => Call(() => { });

            protected void Call(Action action)
            {
                var active = Interlocked.Increment(ref activeCalls);
                UpdateMaximum(active);
                try { action(); }
                finally { Interlocked.Decrement(ref activeCalls); }
            }

            private void SignalSnapshotsPersisted()
            {
                if (Volatile.Read(ref inventorySnapshotPersisted) == 1 &&
                    Volatile.Read(ref skillSnapshotPersisted) == 1)
                    SnapshotsPersisted.Set();
            }

            private void UpdateMaximum(int active)
            {
                while (true)
                {
                    var current = Volatile.Read(ref maximumConcurrentCalls);
                    if (active <= current || Interlocked.CompareExchange(
                            ref maximumConcurrentCalls, active, current) == current)
                        return;
                }
            }
        }

        private sealed class BlockingStore : RecordingStore
        {
            private int blocked;
            private int inventoryDrainTimeoutRecorded;
            private int skillDrainTimeoutRecorded;
            public ManualResetEventSlim Entered { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim Release { get; } = new ManualResetEventSlim(false);
            public ManualResetEventSlim DrainTimeoutGapsRecorded { get; } = new ManualResetEventSlim(false);

            public override void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                if (Interlocked.Exchange(ref blocked, 1) == 0)
                {
                    Entered.Set();
                    Release.Wait();
                }
                base.AppendInventorySnapshot(snapshot);
            }

            public override void AppendInventoryGap(PlayerEvidenceGap gap)
            {
                base.AppendInventoryGap(gap);
                if (gap.Reason == PlayerEvidenceWriteService.DrainTimeoutReason)
                {
                    Interlocked.Exchange(ref inventoryDrainTimeoutRecorded, 1);
                    SetDrainTimeoutGapsRecorded();
                }
            }

            public override void AppendSkillGap(PlayerEvidenceGap gap)
            {
                base.AppendSkillGap(gap);
                if (gap.Reason == PlayerEvidenceWriteService.DrainTimeoutReason)
                {
                    Interlocked.Exchange(ref skillDrainTimeoutRecorded, 1);
                    SetDrainTimeoutGapsRecorded();
                }
            }

            private void SetDrainTimeoutGapsRecorded()
            {
                if (Volatile.Read(ref inventoryDrainTimeoutRecorded) == 1 &&
                    Volatile.Read(ref skillDrainTimeoutRecorded) == 1)
                    DrainTimeoutGapsRecorded.Set();
            }
        }

        private sealed class FailFirstSnapshotStore : RecordingStore
        {
            private int inventoryFailures;
            private int skillFailures;

            public override void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                if (Interlocked.Increment(ref inventoryFailures) == 1)
                    throw new InvalidOperationException("inventory unavailable");
                base.AppendInventorySnapshot(snapshot);
            }

            public override void AppendSkillSnapshot(PlayerSkillSnapshot snapshot)
            {
                if (Interlocked.Increment(ref skillFailures) == 1)
                    throw new InvalidOperationException("skills unavailable");
                base.AppendSkillSnapshot(snapshot);
            }
        }

        private sealed class BlockingFailFirstSessionStore : RecordingStore
        {
            private int appendCount;

            public ManualResetEventSlim FirstAppendStarted { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim ReleaseFirstAppend { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim FirstAppendFailed { get; } = new ManualResetEventSlim();

            public override void AppendSession(PlayerSession session)
            {
                if (Interlocked.Increment(ref appendCount) == 1)
                {
                    FirstAppendStarted.Set();
                    ReleaseFirstAppend.Wait();
                    FirstAppendFailed.Set();
                    throw new InvalidOperationException("session unavailable");
                }

                base.AppendSession(session);
            }
        }
    }
}
