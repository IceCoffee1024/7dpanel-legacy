using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class GrantItemUseCaseTests
    {
        private static readonly DateTimeOffset ObservedAt =
            new DateTimeOffset(2026, 7, 27, 1, 2, 3, TimeSpan.Zero);
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 7, 27, 2, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Public_item_action_requests_do_not_accept_catalog_derived_identity()
        {
            foreach (var requestType in new[] { typeof(GrantItemRequest), typeof(RemoveItemRequest) })
            {
                var parameterNames = requestType.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.Name)
                    .ToArray();
                var propertyNames = requestType.GetProperties()
                    .Select(property => property.Name)
                    .ToArray();

                Assert.DoesNotContain("internalName", parameterNames);
                Assert.DoesNotContain("itemKind", parameterNames);
                Assert.DoesNotContain("InternalName", propertyNames);
                Assert.DoesNotContain("ItemKind", propertyNames);
            }
        }

        [Fact]
        public async Task Same_idempotency_key_and_parameters_reuse_the_original_operation()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var operationIds = new Queue<string>(new[] { "operation-1", "operation-2" });
            var useCase = CreateUseCase(store, gateway, operationIds.Dequeue);
            var request = Request(quantity: 5);

            var first = await useCase.ExecuteAsync(request, CancellationToken.None);
            var second = await useCase.ExecuteAsync(request, CancellationToken.None);

            Assert.Equal("operation-1", first.OperationId);
            Assert.Equal("operation-1", second.OperationId);
            Assert.False(first.Reused);
            Assert.True(second.Reused);
            Assert.Equal(PlayerActionStatus.Succeeded, second.Status);
            Assert.Equal(5, second.ActualQuantity);
            Assert.Equal(1, gateway.GrantCalls);
        }

        [Fact]
        public async Task Same_idempotency_key_with_different_parameters_conflicts_without_a_second_dispatch()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var useCase = CreateUseCase(store, gateway);

            await useCase.ExecuteAsync(Request(quantity: 5), CancellationToken.None);

            var error = await Assert.ThrowsAsync<GrantItemIdempotencyConflictException>(() =>
                useCase.ExecuteAsync(Request(quantity: 6), CancellationToken.None));
            Assert.Equal("operation-1", error.ExistingOperationId);
            Assert.Equal(1, gateway.GrantCalls);
        }

        [Fact]
        public async Task Pending_store_failure_does_not_capture_or_dispatch()
        {
            var store = new RecordingStore { CreateFailure = new InvalidOperationException("store") };
            var gateway = new RecordingGateway();
            var evidence = new RecordingEvidenceStore();
            var useCase = CreateUseCase(store, gateway, evidence: evidence);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                useCase.ExecuteAsync(Request(), CancellationToken.None));

            Assert.Equal(0, gateway.SnapshotCalls);
            Assert.Equal(0, gateway.GrantCalls);
            Assert.Empty(evidence.InventorySnapshots);
        }

        [Fact]
        public async Task Catalog_resolution_is_fixed_in_pending_intent_and_dispatched_command_in_order()
        {
            var events = new List<string>();
            var catalog = Catalog(events: events);
            var store = new RecordingStore(events);
            var gateway = new RecordingGateway(events);
            var evidence = new RecordingEvidenceStore(events);
            var useCase = CreateUseCase(
                store,
                gateway,
                evidence: evidence,
                catalog: catalog,
                snapshotId: Sequence(31, 32));

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(
                new[]
                {
                    "catalog",
                    "pending",
                    "before",
                    "evidence:before",
                    "dispatch",
                    "start",
                    "after",
                    "evidence:after",
                    "complete"
                },
                events);
            Assert.NotNull(store.Intent);
            Assert.Equal("catalog-v1", store.Intent!.CatalogVersion);
            Assert.Equal("resource-iron", store.Intent.ResourceId);
            Assert.Equal("resourceIron", store.Intent.InternalName);
            Assert.Equal(GameResourceKind.Item, store.Intent.ItemKind);
            Assert.NotNull(gateway.Command);
            Assert.Equal(store.Intent.CatalogVersion, gateway.Command!.CatalogVersion);
            Assert.Equal(store.Intent.ResourceId, gateway.Command.ResourceId);
            Assert.Equal(store.Intent.InternalName, gateway.Command.InternalName);
            Assert.Equal(store.Intent.ItemKind, gateway.Command.ItemKind);
            Assert.Equal(PlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(new long[] { 31, 32 }, evidence.InventorySnapshots.Select(x => x.SnapshotId));
        }

        [Fact]
        public async Task Unknown_resource_id_is_rejected_before_pending()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var useCase = CreateUseCase(store, gateway);

            var error = await Assert.ThrowsAsync<GrantItemRequestRejectedException>(() =>
                useCase.ExecuteAsync(
                    Request(resourceId: "resource-other"),
                    CancellationToken.None));

            Assert.Equal(GrantItemFailureCodes.ResourceNotFound, error.Code);
            Assert.Equal(0, store.CreateCalls);
            Assert.Equal(0, gateway.SnapshotCalls);
            Assert.Equal(0, gateway.GrantCalls);
        }

        [Fact]
        public async Task Hidden_item_requires_explicit_strong_confirmation_before_pending()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var useCase = CreateUseCase(
                store,
                gateway,
                catalog: Catalog(visibility: GameResourceVisibility.Hidden));

            var error = await Assert.ThrowsAsync<GrantItemRequestRejectedException>(() =>
                useCase.ExecuteAsync(Request(hiddenConfirmed: false), CancellationToken.None));

            Assert.Equal(GrantItemFailureCodes.HiddenItemConfirmationRequired, error.Code);
            Assert.Equal(0, store.CreateCalls);
            Assert.Equal(0, gateway.GrantCalls);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Quantity_must_be_positive(int quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Request(quantity: quantity));
        }

        [Fact]
        public async Task Server_total_quantity_limit_is_enforced_before_pending()
        {
            var store = new RecordingStore();
            var useCase = CreateUseCase(store, new RecordingGateway(), maximumQuantity: 100);

            var error = await Assert.ThrowsAsync<GrantItemRequestRejectedException>(() =>
                useCase.ExecuteAsync(Request(quantity: 101), CancellationToken.None));

            Assert.Equal(GrantItemFailureCodes.QuantityLimitExceeded, error.Code);
            Assert.Equal(0, store.CreateCalls);
        }

        [Fact]
        public async Task Catalog_stack_size_and_server_stack_limit_are_enforced_before_pending()
        {
            var store = new RecordingStore();
            var useCase = CreateUseCase(
                store,
                new RecordingGateway(),
                catalog: Catalog(maxStack: 10),
                maximumStacks: 2);

            var error = await Assert.ThrowsAsync<GrantItemRequestRejectedException>(() =>
                useCase.ExecuteAsync(Request(quantity: 21), CancellationToken.None));

            Assert.Equal(GrantItemFailureCodes.StackLimitExceeded, error.Code);
            Assert.Equal(0, store.CreateCalls);
        }

        [Theory]
        [InlineData(true, null)]
        [InlineData(true, 0)]
        [InlineData(true, 7)]
        [InlineData(false, 1)]
        public async Task Quality_must_match_the_catalog_capability_and_supported_range(
            bool hasQuality,
            int? quality)
        {
            var store = new RecordingStore();
            var useCase = CreateUseCase(
                store,
                new RecordingGateway(),
                catalog: Catalog(hasQuality: hasQuality));

            var error = await Assert.ThrowsAsync<GrantItemRequestRejectedException>(() =>
                useCase.ExecuteAsync(Request(quality: quality), CancellationToken.None));

            Assert.Equal(GrantItemFailureCodes.QualityUnsupported, error.Code);
            Assert.Equal(0, store.CreateCalls);
        }

        [Fact]
        public async Task Success_returns_actual_quantity_and_links_before_and_after_snapshots()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway(
                result: GrantItemGatewayResult.Succeeded(7));
            var evidence = new RecordingEvidenceStore();
            var useCase = CreateUseCase(
                store,
                gateway,
                evidence: evidence,
                snapshotId: Sequence(41, 42));

            var result = await useCase.ExecuteAsync(Request(quantity: 7), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(7, result.ActualQuantity);
            Assert.Equal(41, result.BeforeInventorySnapshotId);
            Assert.Equal(42, result.AfterInventorySnapshotId);
            Assert.True(result.TerminalStatePersisted);
            Assert.NotNull(store.Completion);
            Assert.Equal(41, store.Completion!.BeforeInventorySnapshotId);
            Assert.Equal(42, store.Completion.AfterInventorySnapshotId);
            Assert.Equal(7, store.Completion.ActualQuantity);
            Assert.Equal(2, evidence.InventorySnapshots.Count);
            Assert.All(evidence.InventorySnapshots, snapshot =>
            {
                Assert.Equal("EOS_123", snapshot.CrossplatformId);
                Assert.Equal("server-1", snapshot.ServerId);
                Assert.Equal("world-1", snapshot.WorldId);
                Assert.True(snapshot.AdminBoundary);
            });
        }

        [Fact]
        public async Task Before_snapshot_store_failure_prevents_dispatch()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var evidence = new RecordingEvidenceStore
            {
                FailAppendNumber = 1
            };
            var useCase = CreateUseCase(store, gateway, evidence: evidence);

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.Failed, result.Status);
            Assert.Equal(GrantItemFailureCodes.SnapshotUnavailable, result.FailureCode);
            Assert.Equal(1, gateway.SnapshotCalls);
            Assert.Equal(0, gateway.GrantCalls);
            Assert.Equal(0, store.StartCalls);
        }

        [Fact]
        public async Task After_snapshot_store_failure_is_result_unknown_after_the_grant_started()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway();
            var evidence = new RecordingEvidenceStore
            {
                FailAppendNumber = 2
            };
            var useCase = CreateUseCase(
                store,
                gateway,
                evidence: evidence,
                snapshotId: Sequence(61, 62));

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.ResultUnknown, result.Status);
            Assert.Equal(GrantItemFailureCodes.ResultUnknown, result.FailureCode);
            Assert.Equal(1, store.StartCalls);
            Assert.Equal(1, gateway.GrantCalls);
            Assert.Equal(2, gateway.SnapshotCalls);
            Assert.Equal(61, result.BeforeInventorySnapshotId);
            Assert.Null(result.AfterInventorySnapshotId);
        }

        [Fact]
        public async Task Cancellation_before_side_effect_is_persisted_as_cancelled()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway(result: GrantItemGatewayResult.Cancelled());
            var useCase = CreateUseCase(store, gateway);

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.Cancelled, result.Status);
            Assert.Equal(0, store.StartCalls);
            Assert.Equal(1, gateway.SnapshotCalls);
            Assert.Equal(1, gateway.GrantCalls);
            Assert.Equal(1, gateway.SnapshotCalls);
        }

        [Fact]
        public async Task Interruption_after_start_is_persisted_as_result_unknown()
        {
            var store = new RecordingStore();
            var gateway = new RecordingGateway(
                result: GrantItemGatewayResult.ResultUnknown(GrantItemFailureCodes.ResultUnknown),
                markStarted: true);
            var useCase = CreateUseCase(store, gateway);

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.ResultUnknown, result.Status);
            Assert.Equal(1, store.StartCalls);
            Assert.Null(result.AfterInventorySnapshotId);
            Assert.Equal(GrantItemFailureCodes.ResultUnknown, result.FailureCode);
        }

        [Fact]
        public async Task Terminal_store_failure_does_not_rewrite_a_successful_action_as_failed()
        {
            var store = new RecordingStore
            {
                CompletionFailure = new InvalidOperationException("terminal store unavailable")
            };
            var gateway = new RecordingGateway(
                result: GrantItemGatewayResult.Succeeded(5));
            var useCase = CreateUseCase(
                store,
                gateway,
                snapshotId: Sequence(51, 52));

            var result = await useCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(5, result.ActualQuantity);
            Assert.False(result.TerminalStatePersisted);
            Assert.Equal(51, result.BeforeInventorySnapshotId);
            Assert.Equal(52, result.AfterInventorySnapshotId);
        }

        private static GrantItemUseCase CreateUseCase(
            RecordingStore store,
            RecordingGateway gateway,
            Func<string>? operationId = null,
            RecordingEvidenceStore? evidence = null,
            StubCatalog? catalog = null,
            Func<long>? snapshotId = null,
            int maximumQuantity = 100,
            int maximumStacks = 10)
        {
            return new GrantItemUseCase(
                catalog ?? Catalog(),
                store,
                gateway,
                evidence ?? new RecordingEvidenceStore(),
                "server-1",
                operationId ?? (() => "operation-1"),
                snapshotId ?? Sequence(11, 12),
                () => Now,
                maximumQuantity,
                maximumStacks);
        }

        private static GrantItemRequest Request(
            int quantity = 5,
            int? quality = null,
            bool hiddenConfirmed = true,
            string resourceId = "resource-iron")
        {
            return new GrantItemRequest(
                "owner-1",
                Target(),
                "catalog-v1",
                resourceId,
                quantity,
                quality,
                hiddenConfirmed,
                "request-key-1",
                "correlation-1");
        }

        private static Func<long> Sequence(params long[] values)
        {
            var queue = new Queue<long>(values);
            return queue.Dequeue;
        }

        private static PlayerTargetStamp Target() =>
            new PlayerTargetStamp("EOS_123", 7, ObservedAt, "world-1");

        private static StubCatalog Catalog(
            GameResourceVisibility visibility = GameResourceVisibility.Public,
            int? maxStack = 10,
            bool? hasQuality = false,
            IList<string>? events = null)
        {
            var entry = new GameResourceCatalogEntry(
                "resource-iron",
                17,
                "resourceIron",
                "铁资源",
                "Iron Resource",
                GameResourceKind.Item,
                visibility,
                maxStack,
                hasQuality,
                GameResourceIconStatus.Missing,
                null);
            var snapshot = new GameResourceCatalogSnapshot(
                "catalog-v1",
                "V 3.0 (b4)",
                ObservedAt,
                new[] { entry },
                Array.Empty<string>());
            return new StubCatalog(GameResourceCatalogReadResult.Available(snapshot), events);
        }

        private sealed class StubCatalog : IGameResourceCatalog
        {
            private readonly GameResourceCatalogReadResult result;
            private readonly IList<string>? events;

            public StubCatalog(GameResourceCatalogReadResult result, IList<string>? events)
            {
                this.result = result;
                this.events = events;
            }

            public GameResourceCatalogReadResult Read()
            {
                events?.Add("catalog");
                return result;
            }

            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken) =>
                Task.FromResult(GameResourceIconReadResult.Missing());
        }

        private sealed class RecordingStore : IGrantItemOperationStore
        {
            private readonly IList<string>? events;
            private GrantItemPendingIntent? storedIntent;
            private PlayerActionOperation? operation;

            public RecordingStore(IList<string>? events = null)
            {
                this.events = events;
            }

            public Exception? CreateFailure { get; set; }
            public Exception? CompletionFailure { get; set; }
            public int CreateCalls { get; private set; }
            public int StartCalls { get; private set; }
            public GrantItemPendingIntent? Intent { get; private set; }
            public GrantItemOperationCompletion? Completion { get; private set; }

            public PlayerActionOperation CreatePending(GrantItemPendingIntent intent)
            {
                CreateCalls++;
                events?.Add("pending");
                if (CreateFailure != null) throw CreateFailure;
                Intent = intent;
                if (storedIntent != null)
                {
                    if (!Matches(storedIntent, intent))
                    {
                        throw new GrantItemIdempotencyConflictException(
                            intent.OperatorId,
                            intent.ClientRequestKey,
                            operation!.OperationId);
                    }

                    return operation!;
                }

                storedIntent = intent;
                operation = Operation(intent, PlayerActionStatus.Pending, null, null, null, null, null);
                return operation;
            }

            public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
            {
                StartCalls++;
                events?.Add("start");
                if (operation == null || operation.OperationId != operationId ||
                    operation.Status != PlayerActionStatus.Pending || operation.StartedAtUtc.HasValue)
                {
                    return false;
                }

                operation = Operation(
                    storedIntent!,
                    PlayerActionStatus.Pending,
                    startedAtUtc,
                    null,
                    null,
                    null,
                    null);
                return true;
            }

            public bool TryComplete(GrantItemOperationCompletion completion)
            {
                events?.Add("complete");
                Completion = completion;
                if (CompletionFailure != null) throw CompletionFailure;
                if (operation == null || operation.Status != PlayerActionStatus.Pending)
                    return false;

                operation = Operation(
                    storedIntent!,
                    completion.Status,
                    operation.StartedAtUtc,
                    completion.CompletedAtUtc,
                    completion.FailureCode,
                    completion.BeforeInventorySnapshotId,
                    completion.AfterInventorySnapshotId);
                return true;
            }

            private static bool Matches(
                GrantItemPendingIntent left,
                GrantItemPendingIntent right)
            {
                return left.OperatorId == right.OperatorId &&
                       left.ClientRequestKey == right.ClientRequestKey &&
                       left.Target == right.Target &&
                       left.CatalogVersion == right.CatalogVersion &&
                       left.ResourceId == right.ResourceId &&
                       left.InternalName == right.InternalName &&
                       left.ItemKind == right.ItemKind &&
                       left.Quantity == right.Quantity &&
                       left.Quality == right.Quality &&
                       left.HiddenItemConfirmed == right.HiddenItemConfirmed;
            }

            private static PlayerActionOperation Operation(
                GrantItemPendingIntent intent,
                PlayerActionStatus status,
                DateTimeOffset? startedAtUtc,
                DateTimeOffset? completedAtUtc,
                string? failureCode,
                long? beforeSnapshotId,
                long? afterSnapshotId)
            {
                return new PlayerActionOperation(
                    intent.OperationId,
                    PlayerActionOperationTypes.GrantItem,
                    intent.OperatorId,
                    intent.Target,
                    status,
                    intent.CreatedAtUtc,
                    startedAtUtc,
                    completedAtUtc,
                    failureCode,
                    beforeSnapshotId,
                    afterSnapshotId,
                    null,
                    null,
                    intent.CorrelationId);
            }
        }

        private sealed class RecordingGateway : IGrantItemGateway
        {
            private readonly Queue<GrantItemInventorySnapshot> snapshots;
            private readonly GrantItemGatewayResult result;
            private readonly IList<string>? events;
            private readonly bool markStarted;

            public RecordingGateway(
                IList<string>? events = null,
                GrantItemGatewayResult? result = null,
                IEnumerable<GrantItemInventorySnapshot>? snapshots = null,
                bool markStarted = false)
            {
                this.events = events;
                this.result = result ?? GrantItemGatewayResult.Succeeded(5);
                this.snapshots = new Queue<GrantItemInventorySnapshot>(
                    snapshots ?? new[] { Snapshot("before"), Snapshot("after") });
                this.markStarted = markStarted;
            }

            public int SnapshotCalls { get; private set; }
            public int GrantCalls { get; private set; }
            public GrantItemCommand? Command { get; private set; }

            public Task<GrantItemInventorySnapshot> CaptureInventorySnapshotAsync(
                GrantItemSnapshotCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SnapshotCalls++;
                events?.Add(SnapshotCalls == 1 ? "before" : "after");
                return Task.FromResult(snapshots.Dequeue());
            }

            public Task<GrantItemGatewayResult> GrantAsync(
                GrantItemCommand command,
                Func<DateTimeOffset, bool> tryStart,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GrantCalls++;
                Command = command;
                events?.Add("dispatch");
                if (markStarted || result.Status == GrantItemGatewayStatus.Succeeded)
                    Assert.True(tryStart(Now));
                return Task.FromResult(result);
            }

            private static GrantItemInventorySnapshot Snapshot(string fingerprint) =>
                new GrantItemInventorySnapshot(
                    Now,
                    "V 3.0 (b4)",
                    "catalog-v1",
                    CatalogResolutionState.Resolved,
                    fingerprint,
                    new[]
                    {
                        new InventoryItemScalar(
                            "bag",
                            0,
                            "resourceIron",
                            5,
                            null,
                            null,
                            Array.Empty<string>())
                    });
        }

        private sealed class RecordingEvidenceStore : IPlayerEvidenceStore
        {
            private readonly IList<string>? events;
            private int appendCalls;

            public RecordingEvidenceStore(IList<string>? events = null)
            {
                this.events = events;
            }

            public int? FailAppendNumber { get; set; }
            public List<PlayerInventorySnapshot> InventorySnapshots { get; } =
                new List<PlayerInventorySnapshot>();

            public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                appendCalls++;
                if (FailAppendNumber == appendCalls)
                    throw new InvalidOperationException("inventory evidence unavailable");
                InventorySnapshots.Add(snapshot);
                events?.Add(appendCalls == 1 ? "evidence:before" : "evidence:after");
            }

            public void AppendSession(PlayerSession session) => throw new NotSupportedException();
            public void AppendActivity(PlayerActivityEvent activity) => throw new NotSupportedException();
            public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot) => throw new NotSupportedException();
            public void AppendInventoryGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public void AppendSkillGap(PlayerEvidenceGap gap) => throw new NotSupportedException();
            public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query) =>
                throw new NotSupportedException();
            public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query) =>
                throw new NotSupportedException();
            public void Compact(PlayerEvidenceCompactionRequest request) =>
                throw new NotSupportedException();
        }
    }
}
