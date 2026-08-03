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
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class ResetPlayerDataUseCaseTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Request_exposes_only_the_fixed_complete_reset_confirmation()
        {
            var request = Request();

            Assert.Equal("CompletePlayerData", request.ConfirmationSummary.Scope);
            Assert.True(request.ConfirmationSummary.RequiresStrongConfirmation);
            Assert.True(request.ConfirmationSummary.PreservesStableIdentity);
            Assert.True(request.ConfirmationSummary.PreservesWorld);
            Assert.DoesNotContain(
                typeof(ResetPlayerDataRequest).GetProperties(),
                property => property.Name.IndexOf("Force", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Theory]
        [InlineData("before_player_snapshot_unavailable")]
        [InlineData("before_inventory_snapshot_unavailable")]
        [InlineData("before_skill_snapshot_unavailable")]
        public async Task Missing_complete_preparation_evidence_is_rejected_without_start_or_execute(
            string failureCode)
        {
            var fixture = new Fixture();
            fixture.Gateway.Preparation = ResetPlayerDataPreparationResult.Rejected(failureCode);

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(ResetPlayerDataOperationStatus.Rejected, result.Status);
            Assert.Equal(failureCode, result.FailureCode);
            Assert.Equal(0, fixture.Store.StartCalls);
            Assert.Equal(0, fixture.Gateway.ExecuteCalls);
        }

        [Fact]
        public async Task Missing_strong_confirmation_never_prepares_or_executes()
        {
            var fixture = new Fixture();

            var result = await fixture.UseCase.ExecuteAsync(
                Request(dangerConfirmed: false),
                CancellationToken.None);

            Assert.Equal(ResetPlayerDataOperationStatus.Rejected, result.Status);
            Assert.Equal("strong_confirmation_required", result.FailureCode);
            Assert.Equal(0, fixture.Gateway.PrepareCalls);
            Assert.Equal(0, fixture.Gateway.ExecuteCalls);
        }

        [Fact]
        public async Task Complete_preparation_is_linked_to_pending_before_started_and_execute()
        {
            var fixture = new Fixture();

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(
                new[] { "prepare", "pending", "started", "execute", "complete" },
                fixture.Order);
            Assert.Equal(ResetPlayerDataOperationStatus.Succeeded, result.Status);
            Assert.Equal(101, result.BeforeInventorySnapshotId);
            Assert.Equal(102, result.BeforeSkillSnapshotId);
            Assert.True(result.TerminalPersisted);
            Assert.False(result.ManualVerificationRequired);
            Assert.NotNull(fixture.Store.Intent);
            Assert.True(fixture.Store.Intent!.HasCompletePreparationEvidence);
        }

        [Fact]
        public async Task Started_interruption_is_unknown_once_and_requires_manual_verification()
        {
            var fixture = new Fixture();
            fixture.Gateway.ExecuteException = new TimeoutException("dispatch interrupted");

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(ResetPlayerDataOperationStatus.ResultUnknown, result.Status);
            Assert.Equal("reset_player_data_result_unknown", result.FailureCode);
            Assert.Equal(1, fixture.Gateway.ExecuteCalls);
            Assert.True(result.ManualVerificationRequired);
            Assert.Equal("verify_player_state_before_retry", result.ManualVerificationCode);
            Assert.Equal(
                ResetPlayerDataOperationStatus.ResultUnknown,
                Assert.Single(fixture.Store.Completions).Status);
        }

        [Fact]
        public async Task Terminal_store_failure_does_not_rewrite_native_success()
        {
            var fixture = new Fixture();
            fixture.Store.CompleteResult = false;

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(ResetPlayerDataOperationStatus.Succeeded, result.Status);
            Assert.False(result.TerminalPersisted);
            Assert.Equal(1, fixture.Gateway.ExecuteCalls);
        }

        [Fact]
        public void Execute_revalidation_rejects_changed_identity_before_native_reset()
        {
            var nativeSideEffects = 0;

            var result = SevenDaysResetPlayerDataGateway.ResolveAndReset(
                new[]
                {
                    new ResetPlayerDataConnectionSnapshot(7, "EOS_replacement", new object())
                },
                Target(),
                "Navezgane",
                Now,
                (_, _) => nativeSideEffects++);

            Assert.Equal(ResetPlayerDataNativeExecutionStatus.Rejected, result.Status);
            Assert.Equal("target_identity_changed", result.FailureCode);
            Assert.Equal(0, nativeSideEffects);
        }

        [Fact]
        public void Matched_execute_uses_the_complete_dedicated_reset_options_once()
        {
            ResetPlayerDataNativeOptions? observed = null;

            var result = SevenDaysResetPlayerDataGateway.ResolveAndReset(
                new[]
                {
                    new ResetPlayerDataConnectionSnapshot(7, "EOS_123", new object())
                },
                Target(),
                "Navezgane",
                Now,
                (_, options) => observed = options);

            Assert.Equal(ResetPlayerDataNativeExecutionStatus.Succeeded, result.Status);
            Assert.NotNull(observed);
            Assert.True(observed!.ResetLevels);
            Assert.True(observed.ResetSkills);
            Assert.True(observed.RemoveLandClaims);
            Assert.True(observed.RemoveSleepingBag);
            Assert.True(observed.RemoveBooks);
            Assert.True(observed.RemoveCrafting);
            Assert.True(observed.RemoveQuests);
            Assert.True(observed.RemoveChallenges);
            Assert.True(observed.RemoveBackpack);
            Assert.True(observed.ResetStats);
            Assert.DoesNotContain(
                typeof(ResetPlayerDataNativeOptions).GetProperties(),
                property => property.Name.IndexOf("Command", StringComparison.OrdinalIgnoreCase) >= 0
                    || property.Name.IndexOf("Path", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static ResetPlayerDataRequest Request(bool dangerConfirmed = true) =>
            new ResetPlayerDataRequest(
                "owner",
                Target(),
                "reset-data-key",
                "correlation-reset-data",
                dangerConfirmed);

        private static PlayerTargetStamp Target() =>
            new PlayerTargetStamp("EOS_123", 7, Now, "Navezgane");

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class Fixture
        {
            public Fixture()
            {
                Store = new RecordingStore(Order);
                Gateway = new RecordingGateway(Order);
                UseCase = new ResetPlayerDataUseCase(
                    Store,
                    Gateway,
                    () => "reset-data-operation",
                    () => Now,
                    TimeSpan.FromSeconds(30));
            }

            public List<string> Order { get; } = new List<string>();
            public RecordingStore Store { get; }
            public RecordingGateway Gateway { get; }
            public ResetPlayerDataUseCase UseCase { get; }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingGateway : IResetPlayerDataGateway
        {
            private readonly List<string> order;

            public RecordingGateway(List<string> order) => this.order = order;

            public int PrepareCalls { get; private set; }
            public int ExecuteCalls { get; private set; }
            public Exception? ExecuteException { get; set; }
            public ResetPlayerDataPreparationResult Preparation { get; set; } =
                ResetPlayerDataPreparationResult.Ready(101, 102);
            public ResetPlayerDataGatewayResult Execution { get; set; } =
                ResetPlayerDataGatewayResult.Succeeded();

            public Task<ResetPlayerDataPreparationResult> PrepareAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                PrepareCalls++;
                order.Add("prepare");
                return Task.FromResult(Preparation);
            }

            public Task<ResetPlayerDataGatewayResult> ExecuteAsync(
                PlayerTargetStamp target,
                CancellationToken cancellationToken)
            {
                ExecuteCalls++;
                order.Add("execute");
                if (ExecuteException != null) throw ExecuteException;
                return Task.FromResult(Execution);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingStore : IResetPlayerDataOperationStore
        {
            private readonly List<string> order;
            private PlayerActionOperation? operation;

            public RecordingStore(List<string> order) => this.order = order;

            public ResetPlayerDataPendingIntent? Intent { get; private set; }
            public int StartCalls { get; private set; }
            public bool CompleteResult { get; set; } = true;
            public List<ResetPlayerDataOperationCompletion> Completions { get; } =
                new List<ResetPlayerDataOperationCompletion>();

            public PlayerActionOperation CreatePending(ResetPlayerDataPendingIntent intent)
            {
                order.Add("pending");
                if (Intent != null)
                {
                    if (!Intent.HasSameParameters(intent))
                    {
                        throw new ResetPlayerDataIdempotencyConflictException(
                            intent.OperatorId,
                            intent.ClientRequestKey,
                            Intent.OperationId);
                    }
                    return operation!;
                }

                Intent = intent;
                operation = ToOperation(intent, PlayerActionStatus.Pending, null, null);
                return operation;
            }

            public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
            {
                StartCalls++;
                order.Add("started");
                Assert.NotNull(Intent);
                Assert.True(Intent!.HasCompletePreparationEvidence);
                if (operation == null || operation.StartedAtUtc.HasValue || operation.CompletedAtUtc.HasValue)
                    return false;
                operation = ToOperation(Intent, PlayerActionStatus.Pending, startedAtUtc, null);
                return true;
            }

            public bool TryComplete(ResetPlayerDataOperationCompletion completion)
            {
                order.Add("complete");
                Completions.Add(completion);
                if (!CompleteResult || operation == null || operation.CompletedAtUtc.HasValue)
                    return false;
                operation = ToOperation(
                    Intent!,
                    ToPlayerActionStatus(completion.Status),
                    operation.StartedAtUtc,
                    completion);
                return true;
            }

            private static PlayerActionOperation ToOperation(
                ResetPlayerDataPendingIntent intent,
                PlayerActionStatus status,
                DateTimeOffset? startedAtUtc,
                ResetPlayerDataOperationCompletion? completion) =>
                new PlayerActionOperation(
                    intent.OperationId,
                    PlayerActionOperationTypes.ResetPlayerData,
                    intent.OperatorId,
                    intent.Target,
                    status,
                    intent.CreatedAtUtc,
                    startedAtUtc,
                    completion?.CompletedAtUtc,
                    completion?.FailureCode,
                    intent.BeforeInventorySnapshotId,
                    null,
                    intent.BeforeSkillSnapshotId,
                    null,
                    intent.CorrelationId);

            private static PlayerActionStatus ToPlayerActionStatus(
                ResetPlayerDataOperationStatus status) => status switch
            {
                ResetPlayerDataOperationStatus.Succeeded => PlayerActionStatus.Succeeded,
                ResetPlayerDataOperationStatus.Rejected => PlayerActionStatus.Rejected,
                ResetPlayerDataOperationStatus.Failed => PlayerActionStatus.Failed,
                ResetPlayerDataOperationStatus.Cancelled => PlayerActionStatus.Cancelled,
                ResetPlayerDataOperationStatus.ResultUnknown => PlayerActionStatus.ResultUnknown,
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }
    }
}
