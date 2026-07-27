using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerActionRecoveryTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Committed_terminals_for_all_five_types_are_retried_without_game_replay()
        {
            var stores = CreateStores();
            var statuses = new[]
            {
                PlayerActionStatus.Succeeded,
                PlayerActionStatus.Rejected,
                PlayerActionStatus.Failed,
                PlayerActionStatus.Cancelled,
                PlayerActionStatus.ResultUnknown
            };
            for (var index = 0; index < stores.Length; index++)
            {
                var operation = Operation(stores[index].OperationType, "operation-" + index);
                stores[index].Records.Add(new PlayerActionRecoveryRecord(
                    operation,
                    new PlayerActionRecoveryTerminal(
                        statuses[index],
                        Now.AddMinutes(-1),
                        statuses[index] == PlayerActionStatus.Succeeded ? null : "committed_terminal",
                        operation.BeforeInventorySnapshotId,
                        201,
                        operation.BeforeSkillSnapshotId,
                        202)));
            }

            var report = Service(stores).Recover();

            Assert.Equal(5, report.AttemptedCompletionCount);
            Assert.Equal(5, report.PersistedCompletionCount);
            Assert.Equal(statuses, stores.Select(store => Assert.Single(store.Completions).Status));
            Assert.Equal(new[] { "operation-4" }, report.ManualVerificationOperationIds);
        }

        [Fact]
        public void Stale_unstarted_is_cancelled_started_is_unknown_and_fresh_pending_is_untouched()
        {
            var stores = CreateStores();
            stores[0].Records.Add(new PlayerActionRecoveryRecord(
                Operation(stores[0].OperationType, "stale", createdAtUtc: Now.AddMinutes(-10)),
                null));
            stores[1].Records.Add(new PlayerActionRecoveryRecord(
                Operation(
                    stores[1].OperationType,
                    "started",
                    createdAtUtc: Now.AddMinutes(-10),
                    startedAtUtc: Now.AddMinutes(-9)),
                null));
            stores[2].Records.Add(new PlayerActionRecoveryRecord(
                Operation(stores[2].OperationType, "fresh", createdAtUtc: Now.AddMinutes(-1)),
                null));

            var report = Service(stores).Recover();

            var cancelled = Assert.Single(stores[0].Completions);
            Assert.Equal(PlayerActionStatus.Cancelled, cancelled.Status);
            Assert.Equal("stale_pending_not_started", cancelled.FailureCode);
            var unknown = Assert.Single(stores[1].Completions);
            Assert.Equal(PlayerActionStatus.ResultUnknown, unknown.Status);
            Assert.Equal("process_interrupted_after_start", unknown.FailureCode);
            Assert.Empty(stores[2].Completions);
            Assert.Equal(new[] { "started" }, report.ManualVerificationOperationIds);
        }

        [Theory]
        [InlineData(PlayerActionStatus.Succeeded)]
        [InlineData(PlayerActionStatus.Rejected)]
        [InlineData(PlayerActionStatus.Failed)]
        public void Existing_terminal_is_never_overwritten(PlayerActionStatus existingStatus)
        {
            var stores = CreateStores();
            stores[0].Records.Add(new PlayerActionRecoveryRecord(
                Operation(
                    stores[0].OperationType,
                    "terminal",
                    existingStatus,
                    Now.AddMinutes(-10),
                    Now.AddMinutes(-9),
                    Now.AddMinutes(-8)),
                new PlayerActionRecoveryTerminal(
                    PlayerActionStatus.ResultUnknown,
                    Now,
                    "late_recovery",
                    101,
                    null,
                    102,
                    null)));

            var report = Service(stores).Recover();

            Assert.Empty(stores[0].Completions);
            Assert.Equal(0, report.AttemptedCompletionCount);
        }

        [Fact]
        public void Completion_compare_and_set_miss_is_reported_without_overwriting()
        {
            var stores = CreateStores();
            stores[0].CompleteResult = false;
            stores[0].Records.Add(new PlayerActionRecoveryRecord(
                Operation(stores[0].OperationType, "cas-miss", createdAtUtc: Now.AddMinutes(-10)),
                null));

            var report = Service(stores).Recover();

            Assert.Equal(1, report.AttemptedCompletionCount);
            Assert.Equal(0, report.PersistedCompletionCount);
            Assert.Equal(new[] { "cas-miss" }, report.UnpersistedOperationIds);
        }

        [Fact]
        public void Runtime_recovers_once_before_starting_inner_runtime()
        {
            var order = new List<string>();
            var stores = CreateStores(order);
            stores[0].Records.Add(new PlayerActionRecoveryRecord(
                Operation(stores[0].OperationType, "stale", createdAtUtc: Now.AddMinutes(-10)),
                null));
            var runtime = new PlayerActionRecoveryRuntime(
                Service(stores),
                new RecordingRuntime(order));

            runtime.Start();
            runtime.Start();

            Assert.Equal(new[] { "read:GrantItem", "complete:GrantItem", "read:RemoveItem",
                "read:ResetSkills", "read:ClearInventory", "read:ResetPlayerData", "inner:start" }, order);
        }

        [Fact]
        public void Runtime_delegates_ready_and_stop_without_running_recovery_again()
        {
            var order = new List<string>();
            var runtime = new PlayerActionRecoveryRuntime(
                Service(CreateStores(order)),
                new RecordingRuntime(order));

            runtime.MarkGameReady();
            runtime.Stop();

            Assert.Equal(new[] { "inner:ready", "inner:stop" }, order);
        }

        private static PlayerActionRecoveryService Service(RecordingRecoveryStore[] stores) =>
            new PlayerActionRecoveryService(
                stores[0],
                stores[1],
                stores[2],
                stores[3],
                stores[4],
                () => Now,
                TimeSpan.FromMinutes(5));

        private static RecordingRecoveryStore[] CreateStores(List<string>? order = null) =>
            new[]
            {
                new RecordingRecoveryStore(PlayerActionOperationTypes.GrantItem, order),
                new RecordingRecoveryStore(PlayerActionOperationTypes.RemoveItem, order),
                new RecordingRecoveryStore(PlayerActionOperationTypes.ResetSkills, order),
                new RecordingRecoveryStore(PlayerActionOperationTypes.ClearInventory, order),
                new RecordingRecoveryStore(PlayerActionOperationTypes.ResetPlayerData, order)
            };

        private static PlayerActionOperation Operation(
            string operationType,
            string operationId,
            PlayerActionStatus status = PlayerActionStatus.Pending,
            DateTimeOffset? createdAtUtc = null,
            DateTimeOffset? startedAtUtc = null,
            DateTimeOffset? completedAtUtc = null) =>
            new PlayerActionOperation(
                operationId,
                operationType,
                "owner",
                new PlayerTargetStamp("EOS_123", 7, Now.AddMinutes(-10), "Navezgane"),
                status,
                createdAtUtc ?? Now.AddMinutes(-10),
                startedAtUtc,
                completedAtUtc,
                null,
                101,
                null,
                102,
                null,
                "correlation");

        private sealed class RecordingRecoveryStore : IPlayerActionRecoveryStore
        {
            private readonly List<string>? order;

            public RecordingRecoveryStore(string operationType, List<string>? order)
            {
                OperationType = operationType;
                this.order = order;
            }

            public string OperationType { get; }
            public bool CompleteResult { get; set; } = true;
            public List<PlayerActionRecoveryRecord> Records { get; } =
                new List<PlayerActionRecoveryRecord>();
            public List<PlayerActionRecoveryCompletion> Completions { get; } =
                new List<PlayerActionRecoveryCompletion>();

            public IReadOnlyList<PlayerActionRecoveryRecord> ReadRecoverable()
            {
                order?.Add("read:" + OperationType);
                return Records.ToArray();
            }

            public bool TryComplete(PlayerActionRecoveryCompletion completion)
            {
                order?.Add("complete:" + OperationType);
                Completions.Add(completion);
                return CompleteResult;
            }
        }

        private sealed class RecordingRuntime : IModRuntime
        {
            private readonly List<string> order;

            public RecordingRuntime(List<string> order) => this.order = order;

            public void Start() => order.Add("inner:start");
            public void MarkGameReady() => order.Add("inner:ready");
            public void Stop() => order.Add("inner:stop");
        }
    }
}
