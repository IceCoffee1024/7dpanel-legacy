using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysPlayerResetGatewayTests
    {
        [Fact]
        public async Task Reset_skills_dispatches_dedicated_native_calls_and_persists_only_skill_snapshots()
        {
            var store = new RecordingEvidenceStore();
            var dispatchedNames = new List<string>();
            var gateway = new SevenDaysResetSkillsGateway(
                Dispatcher(dispatchedNames),
                _ => ResetSkillsNativeSnapshotResult.Ready(SkillScalar(11)),
                _ => ResetSkillsNativeExecutionResult.Succeeded(SkillScalar(12)),
                store,
                Sequence(101, 102));

            var before = await gateway.PrepareAsync(Target(), CancellationToken.None);
            var after = await gateway.ExecuteAsync(Target(), CancellationToken.None);

            Assert.Equal(new[] { "7DPanel.Players.ResetSkills.Prepare", "7DPanel.Players.ResetSkills.Execute" }, dispatchedNames);
            Assert.Equal(101, before.BeforeSkillSnapshotId);
            Assert.Equal(102, after.AfterSkillSnapshotId);
            Assert.Equal(new long[] { 101, 102 }, store.SkillSnapshots.Select(snapshot => snapshot.SnapshotId));
            Assert.Empty(store.InventorySnapshots);
        }

        [Fact]
        public async Task Clear_inventory_dispatches_dedicated_native_calls_and_marks_inventory_boundaries()
        {
            var store = new RecordingEvidenceStore();
            var dispatchedNames = new List<string>();
            var gateway = new SevenDaysClearInventoryGateway(
                Dispatcher(dispatchedNames),
                _ => ClearInventoryNativeSnapshotResult.Ready(InventoryScalar("before", 1)),
                _ => ClearInventoryNativeExecutionResult.Succeeded(InventoryScalar("after", 0)),
                store,
                Sequence(201, 202));

            var before = await gateway.PrepareAsync(Target(), CancellationToken.None);
            var after = await gateway.ExecuteAsync(Target(), CancellationToken.None);

            Assert.Equal(new[] { "7DPanel.Players.ClearInventory.Prepare", "7DPanel.Players.ClearInventory.Execute" }, dispatchedNames);
            Assert.Equal(201, before.BeforeInventorySnapshotId);
            Assert.Equal(202, after.AfterInventorySnapshotId);
            Assert.All(store.InventorySnapshots, snapshot => Assert.True(snapshot.AdminBoundary));
            Assert.Empty(store.SkillSnapshots);
        }

        [Theory]
        [InlineData("EOS_replacement", "Navezgane", 0, "target_identity_changed")]
        [InlineData("EOS_123", "Pregen10k", 0, "world_changed")]
        [InlineData("EOS_123", "Navezgane", 31, "target_not_fresh")]
        public void Reset_skills_revalidation_rejects_without_invoking_native_progression(
            string combinedId,
            string worldId,
            int ageSeconds,
            string failureCode)
        {
            var nativeCalls = 0;
            var now = Now.AddSeconds(ageSeconds);

            var result = SevenDaysResetSkillsGateway.ResolveAndReset(
                new[] { new ResetSkillsConnectionSnapshot(7, combinedId, new object()) },
                Target(),
                worldId,
                now,
                _ => nativeCalls++,
                _ => SkillScalar(12));

            Assert.Equal(ResetSkillsNativeExecutionStatus.Rejected, result.Status);
            Assert.Equal(failureCode, result.FailureCode);
            Assert.Equal(0, nativeCalls);
        }

        [Theory]
        [InlineData("EOS_replacement", "Navezgane", 0, "target_identity_changed")]
        [InlineData("EOS_123", "Pregen10k", 0, "world_changed")]
        [InlineData("EOS_123", "Navezgane", 31, "target_not_fresh")]
        public void Clear_inventory_revalidation_rejects_without_invoking_native_bag_clear(
            string combinedId,
            string worldId,
            int ageSeconds,
            string failureCode)
        {
            var nativeCalls = 0;
            var now = Now.AddSeconds(ageSeconds);

            var result = SevenDaysClearInventoryGateway.ResolveAndClearBag(
                new[] { new ClearInventoryConnectionSnapshot(7, combinedId, new object()) },
                Target(),
                worldId,
                now,
                _ => nativeCalls++,
                _ => InventoryScalar("after", 0));

            Assert.Equal(ClearInventoryNativeExecutionStatus.Rejected, result.Status);
            Assert.Equal(failureCode, result.FailureCode);
            Assert.Equal(0, nativeCalls);
        }

        [Fact]
        public void Matching_reset_target_invokes_only_the_progression_callback()
        {
            var handle = new object();
            object? actual = null;

            var result = SevenDaysResetSkillsGateway.ResolveAndReset(
                new[] { new ResetSkillsConnectionSnapshot(7, "EOS_123", handle) },
                Target(),
                "Navezgane",
                Now,
                value => actual = value,
                _ => SkillScalar(12));

            Assert.Equal(ResetSkillsNativeExecutionStatus.Succeeded, result.Status);
            Assert.Same(handle, actual);
        }

        [Fact]
        public void Matching_clear_target_invokes_only_the_bag_callback()
        {
            var handle = new object();
            object? actual = null;

            var result = SevenDaysClearInventoryGateway.ResolveAndClearBag(
                new[] { new ClearInventoryConnectionSnapshot(7, "EOS_123", handle) },
                Target(),
                "Navezgane",
                Now,
                value => actual = value,
                _ => InventoryScalar("after", 0));

            Assert.Equal(ClearInventoryNativeExecutionStatus.Succeeded, result.Status);
            Assert.Same(handle, actual);
        }

        [Fact]
        public async Task Preparation_failure_has_no_execute_dispatch_and_after_started_timeout_is_unknown()
        {
            var store = new RecordingEvidenceStore();
            var reset = new SevenDaysResetSkillsGateway(
                (_, _, _, _) => Task.FromException<object>(new TimeoutException()),
                _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException(),
                store,
                Sequence(1));
            var preparation = await reset.PrepareAsync(Target(), CancellationToken.None);
            Assert.Equal(ResetSkillsPreparationStatus.Failed, preparation.Status);

            var clear = new SevenDaysClearInventoryGateway(
                (name, action, _, _) => name.EndsWith("Execute", StringComparison.Ordinal)
                    ? Task.FromException<object>(new TimeoutException())
                    : Task.FromResult(action()),
                _ => ClearInventoryNativeSnapshotResult.Ready(InventoryScalar("before", 1)),
                _ => throw new InvalidOperationException(),
                store,
                Sequence(2));
            await clear.PrepareAsync(Target(), CancellationToken.None);
            var result = await clear.ExecuteAsync(Target(), CancellationToken.None);
            Assert.Equal(ClearInventoryGatewayStatus.ResultUnknown, result.Status);
        }

        [Fact]
        public async Task Snapshot_store_failure_is_pre_dispatch_failure_before_and_unknown_after_mutation()
        {
            var beforeStore = new RecordingEvidenceStore { AppendSkillException = new InvalidOperationException() };
            var reset = new SevenDaysResetSkillsGateway(
                Dispatcher(new List<string>()),
                _ => ResetSkillsNativeSnapshotResult.Ready(SkillScalar(11)),
                _ => ResetSkillsNativeExecutionResult.Succeeded(SkillScalar(12)),
                beforeStore,
                Sequence(1, 2));
            var before = await reset.PrepareAsync(Target(), CancellationToken.None);
            Assert.Equal(ResetSkillsPreparationStatus.Failed, before.Status);

            var afterStore = new RecordingEvidenceStore { FailInventoryAppendNumber = 2 };
            var clear = new SevenDaysClearInventoryGateway(
                Dispatcher(new List<string>()),
                _ => ClearInventoryNativeSnapshotResult.Ready(InventoryScalar("before", 1)),
                _ => ClearInventoryNativeExecutionResult.Succeeded(InventoryScalar("after", 0)),
                afterStore,
                Sequence(3, 4));
            await clear.PrepareAsync(Target(), CancellationToken.None);
            var after = await clear.ExecuteAsync(Target(), CancellationToken.None);
            Assert.Equal(ClearInventoryGatewayStatus.ResultUnknown, after.Status);
        }

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        private static PlayerTargetStamp Target() =>
            new PlayerTargetStamp("EOS_123", 7, Now, "Navezgane");

        private static Func<string, Func<object>, TimeSpan, CancellationToken, Task<object>> Dispatcher(
            ICollection<string> names) =>
            (name, action, timeout, cancellationToken) =>
            {
                names.Add(name);
                Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                return Task.FromResult(action());
            };

        private static Func<long> Sequence(params long[] values)
        {
            var index = -1;
            return () => values[Interlocked.Increment(ref index)];
        }

        private static ResetSkillsSnapshotScalar SkillScalar(int points) =>
            new ResetSkillsSnapshotScalar(
                "EOS_123",
                "local",
                "Navezgane",
                Now,
                "v3.0.1-b4",
                12,
                points,
                new[]
                {
                    new PlayerSkillValue("perkAthletics", SkillValueState.Known, 1, 0, 5, 1, null)
                });

        private static ClearInventorySnapshotScalar InventoryScalar(string fingerprint, int count) =>
            new ClearInventorySnapshotScalar(
                "EOS_123",
                "local",
                "Navezgane",
                Now,
                "v3.0.1-b4",
                "catalog-1",
                CatalogResolutionState.Resolved,
                fingerprint,
                count == 0
                    ? Array.Empty<InventoryItemScalar>()
                    : new[]
                    {
                        new InventoryItemScalar("bag", 0, "resourceRockSmall", count, null, null, Array.Empty<string>())
                    });

        private sealed class RecordingEvidenceStore : IPlayerEvidenceStore
        {
            private int inventoryAppendCount;
            public Exception? AppendSkillException { get; set; }
            public int? FailInventoryAppendNumber { get; set; }
            public List<PlayerInventorySnapshot> InventorySnapshots { get; } = new List<PlayerInventorySnapshot>();
            public List<PlayerSkillSnapshot> SkillSnapshots { get; } = new List<PlayerSkillSnapshot>();

            public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                inventoryAppendCount++;
                if (FailInventoryAppendNumber == inventoryAppendCount) throw new InvalidOperationException();
                InventorySnapshots.Add(snapshot);
            }

            public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot)
            {
                if (AppendSkillException != null) throw AppendSkillException;
                SkillSnapshots.Add(snapshot);
            }

            public void AppendSession(PlayerSession session) => throw new NotSupportedException();
            public void AppendActivity(PlayerActivityEvent activity) => throw new NotSupportedException();
            public void AppendInventoryGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public void AppendSkillGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query) => throw new NotSupportedException();
            public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query) => throw new NotSupportedException();
            public PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query) => throw new NotSupportedException();
            public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query) => throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query) => throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query) => throw new NotSupportedException();
            public void Compact(PlayerEvidenceCompactionRequest request) => throw new NotSupportedException();
        }
    }
}
