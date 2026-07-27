using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerPartialResetUseCaseTests
    {
        [Fact]
        public void Requests_statuses_stores_gateways_and_confirmation_summaries_are_action_specific()
        {
            Assert.NotEqual(typeof(ResetSkillsRequest), typeof(ClearInventoryRequest));
            Assert.NotEqual(typeof(IResetSkillsOperationStore), typeof(IClearInventoryOperationStore));
            Assert.NotEqual(typeof(IResetSkillsGateway), typeof(IClearInventoryGateway));
            Assert.NotEqual(typeof(ResetSkillsOperationStatus), typeof(ClearInventoryOperationStatus));
            Assert.NotEqual(typeof(ResetSkillsConfirmationSummary), typeof(ClearInventoryConfirmationSummary));
            Assert.DoesNotContain(typeof(ResetSkillsRequest).GetProperties(),
                property => property.Name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain(typeof(ClearInventoryRequest).GetProperties(),
                property => property.Name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Confirmation_summaries_fix_the_two_non_overlapping_scopes()
        {
            var reset = ResetRequest();
            var clear = ClearRequest();

            Assert.Equal("CurrentVersionProgressionOnly", reset.ConfirmationSummary.Scope);
            Assert.True(reset.ConfirmationSummary.PreservesIdentity);
            Assert.True(reset.ConfirmationSummary.PreservesPosition);
            Assert.True(reset.ConfirmationSummary.PreservesInventory);
            Assert.Equal(PlayerItemRemovalScope.BagOnly, clear.ConfirmationSummary.Scope);
            Assert.True(clear.ConfirmationSummary.PreservesEquipment);
            Assert.True(clear.ConfirmationSummary.PreservesToolbelt);
            Assert.True(clear.ConfirmationSummary.PreservesOtherContainers);
        }

        [Fact]
        public async Task Pending_failure_prevents_both_gateways_from_dispatching()
        {
            var reset = new ResetFixture();
            reset.Store.CreateException = new InvalidOperationException("pending unavailable");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None));
            Assert.Equal(0, reset.Gateway.CallCount);

            var clear = new ClearFixture();
            clear.Store.CreateException = new InvalidOperationException("pending unavailable");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None));
            Assert.Equal(0, clear.Gateway.CallCount);
        }

        [Fact]
        public async Task Missing_confirmation_and_stale_targets_never_reach_a_gateway()
        {
            var reset = new ResetFixture();
            var resetResult = await reset.UseCase.ExecuteAsync(
                ResetRequest(dangerConfirmed: false), CancellationToken.None);
            Assert.Equal(ResetSkillsOperationStatus.Rejected, resetResult.Status);
            Assert.Equal("danger_confirmation_required", resetResult.FailureCode);
            Assert.Equal(0, reset.Gateway.CallCount);

            var clear = new ClearFixture();
            var clearResult = await clear.UseCase.ExecuteAsync(
                ClearRequest(observedAtUtc: Now.AddMinutes(-1)), CancellationToken.None);
            Assert.Equal(ClearInventoryOperationStatus.Rejected, clearResult.Status);
            Assert.Equal("target_not_fresh", clearResult.FailureCode);
            Assert.Equal(0, clear.Gateway.CallCount);
        }

        [Fact]
        public async Task Reset_skills_records_only_skill_snapshots_and_success_confirmation()
        {
            var fixture = new ResetFixture();

            var result = await fixture.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);

            Assert.Equal(new[] { "pending", "prepare", "started", "execute", "complete" }, fixture.Order);
            Assert.Equal(ResetSkillsOperationStatus.Succeeded, result.Status);
            Assert.Equal(101, result.BeforeSkillSnapshotId);
            Assert.Equal(102, result.AfterSkillSnapshotId);
            Assert.True(result.TerminalPersisted);
            Assert.NotNull(result.ConfirmationSummary);
            var completion = Assert.Single(fixture.Store.Completions);
            Assert.Equal(101, completion.BeforeSkillSnapshotId);
            Assert.Equal(102, completion.AfterSkillSnapshotId);
            Assert.DoesNotContain(typeof(ResetSkillsOperationCompletion).GetProperties(),
                property => property.Name.IndexOf("InventorySnapshot", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public async Task Clear_inventory_records_only_inventory_snapshots_and_success_confirmation()
        {
            var fixture = new ClearFixture();

            var result = await fixture.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);

            Assert.Equal(new[] { "pending", "prepare", "started", "execute", "complete" }, fixture.Order);
            Assert.Equal(ClearInventoryOperationStatus.Succeeded, result.Status);
            Assert.Equal(201, result.BeforeInventorySnapshotId);
            Assert.Equal(202, result.AfterInventorySnapshotId);
            Assert.True(result.TerminalPersisted);
            Assert.NotNull(result.ConfirmationSummary);
            var completion = Assert.Single(fixture.Store.Completions);
            Assert.Equal(201, completion.BeforeInventorySnapshotId);
            Assert.Equal(202, completion.AfterInventorySnapshotId);
            Assert.DoesNotContain(typeof(ClearInventoryOperationCompletion).GetProperties(),
                property => property.Name.IndexOf("SkillSnapshot", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public async Task Revalidation_failure_after_preparation_has_zero_native_side_effects()
        {
            var reset = new ResetFixture();
            reset.Gateway.ExecuteResult = ResetSkillsGatewayResult.Rejected("target_identity_changed");
            var resetResult = await reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);
            Assert.Equal(ResetSkillsOperationStatus.Rejected, resetResult.Status);
            Assert.Equal(0, reset.Gateway.NativeSideEffects);

            var clear = new ClearFixture();
            clear.Gateway.ExecuteResult = ClearInventoryGatewayResult.Rejected("world_changed");
            var clearResult = await clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);
            Assert.Equal(ClearInventoryOperationStatus.Rejected, clearResult.Status);
            Assert.Equal(0, clear.Gateway.NativeSideEffects);
        }

        [Fact]
        public async Task Interruption_after_started_is_result_unknown_and_is_not_retried()
        {
            var reset = new ResetFixture();
            reset.Gateway.ExecuteException = new OperationCanceledException();
            var resetResult = await reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);
            Assert.Equal(ResetSkillsOperationStatus.ResultUnknown, resetResult.Status);
            Assert.Equal(1, reset.Gateway.ExecuteCalls);

            var clear = new ClearFixture();
            clear.Gateway.ExecuteException = new TimeoutException();
            var clearResult = await clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);
            Assert.Equal(ClearInventoryOperationStatus.ResultUnknown, clearResult.Status);
            Assert.Equal(1, clear.Gateway.ExecuteCalls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Terminal_store_failure_does_not_rewrite_a_successful_reset(bool throwOnComplete)
        {
            var fixture = new ResetFixture();
            fixture.Store.CompleteResult = false;
            if (throwOnComplete)
                fixture.Store.CompleteException = new InvalidOperationException("terminal unavailable");

            var result = await fixture.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);

            Assert.Equal(ResetSkillsOperationStatus.Succeeded, result.Status);
            Assert.False(result.TerminalPersisted);
            Assert.Single(fixture.Store.Completions);
        }

        [Fact]
        public async Task Terminal_compare_and_set_prevents_a_late_clear_inventory_rewrite()
        {
            var fixture = new ClearFixture();
            fixture.Store.CompleteResult = false;

            var result = await fixture.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);

            Assert.Equal(ClearInventoryOperationStatus.Succeeded, result.Status);
            Assert.False(result.TerminalPersisted);
            Assert.Single(fixture.Store.Completions);
        }

        [Fact]
        public async Task Same_key_and_parameters_reuse_terminal_operations_without_dispatch()
        {
            var reset = new ResetFixture();
            var firstReset = await reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);
            var secondReset = await reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);
            Assert.Equal(firstReset.OperationId, secondReset.OperationId);
            Assert.Equal(1, reset.Gateway.ExecuteCalls);

            var clear = new ClearFixture();
            var firstClear = await clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);
            var secondClear = await clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);
            Assert.Equal(firstClear.OperationId, secondClear.OperationId);
            Assert.Equal(1, clear.Gateway.ExecuteCalls);
        }

        [Fact]
        public async Task Same_key_with_different_parameters_conflicts_independently()
        {
            var reset = new ResetFixture();
            await reset.UseCase.ExecuteAsync(ResetRequest(), CancellationToken.None);
            await Assert.ThrowsAsync<ResetSkillsIdempotencyConflictException>(() =>
                reset.UseCase.ExecuteAsync(
                    ResetRequest(target: Target(entityId: 8)), CancellationToken.None));

            var clear = new ClearFixture();
            await clear.UseCase.ExecuteAsync(ClearRequest(), CancellationToken.None);
            await Assert.ThrowsAsync<ClearInventoryIdempotencyConflictException>(() =>
                clear.UseCase.ExecuteAsync(
                    ClearRequest(target: Target(entityId: 8)), CancellationToken.None));
        }

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        private static PlayerTargetStamp Target(
            int entityId = 7,
            DateTimeOffset? observedAtUtc = null) =>
            new PlayerTargetStamp("EOS_123", entityId, observedAtUtc ?? Now, "Navezgane");

        private static ResetSkillsRequest ResetRequest(
            PlayerTargetStamp? target = null,
            DateTimeOffset? observedAtUtc = null,
            bool dangerConfirmed = true) =>
            new ResetSkillsRequest(
                "owner",
                target ?? Target(observedAtUtc: observedAtUtc),
                "reset-key",
                "correlation-reset",
                dangerConfirmed);

        private static ClearInventoryRequest ClearRequest(
            PlayerTargetStamp? target = null,
            DateTimeOffset? observedAtUtc = null,
            bool dangerConfirmed = true) =>
            new ClearInventoryRequest(
                "owner",
                target ?? Target(observedAtUtc: observedAtUtc),
                "clear-key",
                "correlation-clear",
                dangerConfirmed);

        private sealed class ResetFixture
        {
            public ResetFixture()
            {
                Gateway = new RecordingResetSkillsGateway(Order);
                Store = new RecordingResetSkillsStore(Order);
                UseCase = new ResetSkillsUseCase(
                    Store,
                    Gateway,
                    () => "reset-operation",
                    () => Now,
                    TimeSpan.FromSeconds(30));
            }

            public List<string> Order { get; } = new List<string>();
            public RecordingResetSkillsStore Store { get; }
            public RecordingResetSkillsGateway Gateway { get; }
            public ResetSkillsUseCase UseCase { get; }
        }

        private sealed class ClearFixture
        {
            public ClearFixture()
            {
                Gateway = new RecordingClearInventoryGateway(Order);
                Store = new RecordingClearInventoryStore(Order);
                UseCase = new ClearInventoryUseCase(
                    Store,
                    Gateway,
                    () => "clear-operation",
                    () => Now,
                    TimeSpan.FromSeconds(30));
            }

            public List<string> Order { get; } = new List<string>();
            public RecordingClearInventoryStore Store { get; }
            public RecordingClearInventoryGateway Gateway { get; }
            public ClearInventoryUseCase UseCase { get; }
        }

        private sealed class RecordingResetSkillsStore : IResetSkillsOperationStore
        {
            private readonly List<string> order;
            private ResetSkillsPendingIntent? intent;
            private PlayerActionOperation? operation;

            public RecordingResetSkillsStore(List<string> order) => this.order = order;
            public Exception? CreateException { get; set; }
            public bool CompleteResult { get; set; } = true;
            public Exception? CompleteException { get; set; }
            public List<ResetSkillsOperationCompletion> Completions { get; } = new List<ResetSkillsOperationCompletion>();

            public PlayerActionOperation CreatePending(ResetSkillsPendingIntent value)
            {
                if (CreateException != null) throw CreateException;
                order.Add("pending");
                if (intent != null)
                {
                    if (!intent.HasSameParameters(value))
                        throw new ResetSkillsIdempotencyConflictException(
                            value.OperatorId, value.ClientRequestKey, intent.OperationId);
                    return operation!;
                }
                intent = value;
                operation = Operation(value, PlayerActionStatus.Pending, null);
                return operation;
            }

            public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
            {
                order.Add("started");
                if (operation == null || operation.Status != PlayerActionStatus.Pending) return false;
                operation = Operation(intent!, PlayerActionStatus.Pending, null, startedAtUtc);
                return true;
            }

            public bool TryComplete(ResetSkillsOperationCompletion completion)
            {
                order.Add("complete");
                Completions.Add(completion);
                if (CompleteException != null) throw CompleteException;
                if (!CompleteResult || operation == null || operation.CompletedAtUtc.HasValue) return false;
                operation = Operation(
                    intent!,
                    completion.Status.ToPlayerActionStatus(),
                    completion,
                    operation.StartedAtUtc);
                return true;
            }

            private static PlayerActionOperation Operation(
                ResetSkillsPendingIntent value,
                PlayerActionStatus status,
                ResetSkillsOperationCompletion? completion,
                DateTimeOffset? startedAtUtc = null) =>
                new PlayerActionOperation(
                    value.OperationId,
                    PlayerActionOperationTypes.ResetSkills,
                    value.OperatorId,
                    value.Target,
                    status,
                    value.CreatedAtUtc,
                    startedAtUtc,
                    completion?.CompletedAtUtc,
                    completion?.FailureCode,
                    null,
                    null,
                    completion?.BeforeSkillSnapshotId,
                    completion?.AfterSkillSnapshotId,
                    value.CorrelationId);
        }

        private sealed class RecordingClearInventoryStore : IClearInventoryOperationStore
        {
            private readonly List<string> order;
            private ClearInventoryPendingIntent? intent;
            private PlayerActionOperation? operation;

            public RecordingClearInventoryStore(List<string> order) => this.order = order;
            public Exception? CreateException { get; set; }
            public bool CompleteResult { get; set; } = true;
            public Exception? CompleteException { get; set; }
            public List<ClearInventoryOperationCompletion> Completions { get; } = new List<ClearInventoryOperationCompletion>();

            public PlayerActionOperation CreatePending(ClearInventoryPendingIntent value)
            {
                if (CreateException != null) throw CreateException;
                order.Add("pending");
                if (intent != null)
                {
                    if (!intent.HasSameParameters(value))
                        throw new ClearInventoryIdempotencyConflictException(
                            value.OperatorId, value.ClientRequestKey, intent.OperationId);
                    return operation!;
                }
                intent = value;
                operation = Operation(value, PlayerActionStatus.Pending, null);
                return operation;
            }

            public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
            {
                order.Add("started");
                if (operation == null || operation.Status != PlayerActionStatus.Pending) return false;
                operation = Operation(intent!, PlayerActionStatus.Pending, null, startedAtUtc);
                return true;
            }

            public bool TryComplete(ClearInventoryOperationCompletion completion)
            {
                order.Add("complete");
                Completions.Add(completion);
                if (CompleteException != null) throw CompleteException;
                if (!CompleteResult || operation == null || operation.CompletedAtUtc.HasValue) return false;
                operation = Operation(
                    intent!,
                    completion.Status.ToPlayerActionStatus(),
                    completion,
                    operation.StartedAtUtc);
                return true;
            }

            private static PlayerActionOperation Operation(
                ClearInventoryPendingIntent value,
                PlayerActionStatus status,
                ClearInventoryOperationCompletion? completion,
                DateTimeOffset? startedAtUtc = null) =>
                new PlayerActionOperation(
                    value.OperationId,
                    PlayerActionOperationTypes.ClearInventory,
                    value.OperatorId,
                    value.Target,
                    status,
                    value.CreatedAtUtc,
                    startedAtUtc,
                    completion?.CompletedAtUtc,
                    completion?.FailureCode,
                    completion?.BeforeInventorySnapshotId,
                    completion?.AfterInventorySnapshotId,
                    null,
                    null,
                    value.CorrelationId);
        }

        private sealed class RecordingResetSkillsGateway : IResetSkillsGateway
        {
            private readonly List<string> order;
            public RecordingResetSkillsGateway(List<string> order) => this.order = order;
            public int PrepareCalls { get; private set; }
            public int ExecuteCalls { get; private set; }
            public int CallCount => PrepareCalls + ExecuteCalls;
            public int NativeSideEffects { get; private set; }
            public Exception? ExecuteException { get; set; }
            public ResetSkillsGatewayResult ExecuteResult { get; set; } = ResetSkillsGatewayResult.Succeeded(102);

            public Task<ResetSkillsPreparationResult> PrepareAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                PrepareCalls++;
                order.Add("prepare");
                return Task.FromResult(ResetSkillsPreparationResult.Ready(101));
            }

            public Task<ResetSkillsGatewayResult> ExecuteAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                ExecuteCalls++;
                order.Add("execute");
                if (ExecuteException != null) return Task.FromException<ResetSkillsGatewayResult>(ExecuteException);
                if (ExecuteResult.Status == ResetSkillsGatewayStatus.Succeeded) NativeSideEffects++;
                return Task.FromResult(ExecuteResult);
            }
        }

        private sealed class RecordingClearInventoryGateway : IClearInventoryGateway
        {
            private readonly List<string> order;
            public RecordingClearInventoryGateway(List<string> order) => this.order = order;
            public int PrepareCalls { get; private set; }
            public int ExecuteCalls { get; private set; }
            public int CallCount => PrepareCalls + ExecuteCalls;
            public int NativeSideEffects { get; private set; }
            public Exception? ExecuteException { get; set; }
            public ClearInventoryGatewayResult ExecuteResult { get; set; } = ClearInventoryGatewayResult.Succeeded(202);

            public Task<ClearInventoryPreparationResult> PrepareAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                PrepareCalls++;
                order.Add("prepare");
                return Task.FromResult(ClearInventoryPreparationResult.Ready(201));
            }

            public Task<ClearInventoryGatewayResult> ExecuteAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                ExecuteCalls++;
                order.Add("execute");
                if (ExecuteException != null) return Task.FromException<ClearInventoryGatewayResult>(ExecuteException);
                if (ExecuteResult.Status == ClearInventoryGatewayStatus.Succeeded) NativeSideEffects++;
                return Task.FromResult(ExecuteResult);
            }
        }
    }
}
