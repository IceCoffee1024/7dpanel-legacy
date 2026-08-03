using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using Newtonsoft.Json;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class RemoveItemUseCaseTests
    {
        private static readonly DateTimeOffset OnlineObservedAtUtc =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Request_defaults_and_json_omissions_are_bag_only_and_exact()
        {
            var request = Request();
            var json = JsonConvert.SerializeObject(request);
            var withoutScopeOrMode = JsonConvert.DeserializeObject<RemoveItemRequest>(
                json.Replace(",\"RemovalScope\":0", string.Empty)
                    .Replace(",\"RemovalMode\":0", string.Empty));

            Assert.Equal(new[] { "BagOnly" }, Enum.GetNames(typeof(PlayerItemRemovalScope)));
            Assert.Equal(PlayerItemRemovalScope.BagOnly, default(PlayerItemRemovalScope));
            Assert.Equal(PlayerItemRemovalMode.Exact, default(PlayerItemRemovalMode));
            Assert.NotNull(withoutScopeOrMode);
            Assert.Equal(PlayerItemRemovalScope.BagOnly, withoutScopeOrMode!.RemovalScope);
            Assert.Equal(PlayerItemRemovalMode.Exact, withoutScopeOrMode.RemovalMode);
        }

        [Fact]
        public void Request_is_an_independent_fixed_remove_contract()
        {
            var request = Request(
                quantity: 9,
                quality: 3,
                removalMode: PlayerItemRemovalMode.UpToAvailable);

            Assert.Equal("owner-1", request.OperatorId);
            Assert.Equal("catalog-7", request.CatalogVersion);
            Assert.Equal("resource-iron", request.ResourceId);
            Assert.Equal(9, request.Quantity);
            Assert.Equal(3, request.Quality);
            Assert.Equal(PlayerItemRemovalScope.BagOnly, request.RemovalScope);
            Assert.Equal(PlayerItemRemovalMode.UpToAvailable, request.RemovalMode);
            Assert.DoesNotContain(
                typeof(RemoveItemRequest).GetProperties(),
                property => property.PropertyType.Name.IndexOf("Grant", StringComparison.Ordinal) >= 0);
        }

        [Fact]
        public async Task Catalog_version_and_resource_are_revalidated_before_pending()
        {
            var cases = new[]
            {
                Request(catalogVersion: "stale-catalog"),
                Request(resourceId: "wrong-resource")
            };

            foreach (var request in cases)
            {
                var fixture = new Fixture();

                await Assert.ThrowsAsync<RemoveItemCatalogConflictException>(() =>
                    fixture.UseCase.ExecuteAsync(request, CancellationToken.None));

                Assert.Empty(fixture.Store.Intents);
                Assert.Equal(0, fixture.Gateway.CallCount);
            }
        }

        [Fact]
        public async Task Non_available_catalog_is_rejected_before_pending()
        {
            var fixture = new Fixture(GameResourceCatalogReadResult.Building());

            await Assert.ThrowsAsync<RemoveItemCatalogUnavailableException>(() =>
                fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None));

            Assert.Empty(fixture.Store.Intents);
            Assert.Equal(0, fixture.Gateway.CallCount);
        }

        [Fact]
        public async Task Stale_target_is_rejected_before_pending_and_dispatch()
        {
            var fixture = new Fixture();
            var stale = new RemoveItemRequest(
                "owner-1",
                new PlayerTargetStamp(
                    "EOS_123",
                    7,
                    OnlineObservedAtUtc.AddMinutes(-1),
                    "Navezgane"),
                "catalog-7",
                "resource-iron",
                3,
                null,
                PlayerItemRemovalScope.BagOnly,
                PlayerItemRemovalMode.Exact,
                "request-stale",
                "correlation-1");

            await Assert.ThrowsAsync<RemoveItemTargetNotFreshException>(() =>
                fixture.UseCase.ExecuteAsync(stale, CancellationToken.None));

            Assert.Empty(fixture.Store.Intents);
            Assert.Equal(0, fixture.Gateway.CallCount);
        }

        [Fact]
        public async Task Pending_store_failure_never_starts_or_dispatches()
        {
            var fixture = new Fixture();
            fixture.Store.CreateException = new InvalidOperationException("database unavailable");

            var exception = await Assert.ThrowsAsync<RemoveItemPendingStoreException>(() =>
                fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None));

            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Empty(fixture.Store.StartedOperationIds);
            Assert.Equal(0, fixture.Gateway.CallCount);
        }

        [Fact]
        public async Task Same_client_key_and_parameters_reuse_without_dispatching_again()
        {
            var fixture = new Fixture();

            var first = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);
            var second = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal("operation-1", first.OperationId);
            Assert.Equal(first.OperationId, second.OperationId);
            Assert.Equal(PlayerActionStatus.Succeeded, second.Status);
            Assert.True(second.Reused);
            Assert.Equal(1, fixture.Gateway.CallCount);
            Assert.Single(fixture.Store.Intents);
        }

        [Fact]
        public async Task Same_client_key_with_different_remove_parameters_conflicts()
        {
            var fixture = new Fixture();
            await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            await Assert.ThrowsAsync<RemoveItemIdempotencyConflictException>(() =>
                fixture.UseCase.ExecuteAsync(Request(quantity: 2), CancellationToken.None));

            Assert.Equal(1, fixture.Gateway.CallCount);
        }

        [Fact]
        public async Task Pending_and_start_precede_dispatch_and_success_persists_both_snapshots()
        {
            var order = new List<string>();
            var fixture = new Fixture(order: order);

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(
                new[]
                {
                    "store:pending",
                    "store:start",
                    "gateway:remove",
                    "evidence:before",
                    "evidence:after",
                    "store:Succeeded"
                },
                order);
            Assert.Equal(PlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(3, result.ActualQuantity);
            Assert.Equal(101, result.BeforeInventorySnapshotId);
            Assert.Equal(102, result.AfterInventorySnapshotId);
            Assert.False(result.Reused);
            Assert.True(result.TerminalStatePersisted);
            var completion = Assert.Single(fixture.Store.Completions);
            Assert.Equal(3, completion.ActualQuantity);
            Assert.Equal(101, completion.BeforeInventorySnapshotId);
            Assert.Equal(102, completion.AfterInventorySnapshotId);
        }

        [Fact]
        public async Task Successful_exact_snapshot_links_make_the_diff_confirmed()
        {
            var fixture = new Fixture();

            await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            var snapshots = fixture.Evidence.InventorySnapshots;
            var operation = fixture.Store.CurrentOperation!;
            var diff = new PlayerInventoryDiffService().Compare(
                snapshots[0],
                snapshots[1],
                Array.Empty<PlayerEvidenceGap>(),
                new[] { operation });

            Assert.Equal(EvidenceLevel.Confirmed, Assert.Single(diff.Changes).EvidenceLevel);
            Assert.Equal("operation-1", Assert.Single(diff.Changes).SourceOperationIds.Single());
        }

        [Theory]
        [InlineData(RemoveItemGatewayStatus.Rejected, PlayerActionStatus.Rejected)]
        [InlineData(RemoveItemGatewayStatus.Failed, PlayerActionStatus.Failed)]
        [InlineData(RemoveItemGatewayStatus.Cancelled, PlayerActionStatus.Cancelled)]
        [InlineData(RemoveItemGatewayStatus.ResultUnknown, PlayerActionStatus.ResultUnknown)]
        public async Task Non_success_results_never_persist_or_link_confirmed_snapshots(
            RemoveItemGatewayStatus gatewayStatus,
            PlayerActionStatus expectedStatus)
        {
            var fixture = new Fixture();
            fixture.Gateway.Result = RemoveItemGatewayResult.Terminal(
                gatewayStatus,
                "typed_failure");

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(expectedStatus, result.Status);
            Assert.Null(result.ActualQuantity);
            Assert.Empty(fixture.Evidence.InventorySnapshots);
            var completion = Assert.Single(fixture.Store.Completions);
            Assert.Null(completion.BeforeInventorySnapshotId);
            Assert.Null(completion.AfterInventorySnapshotId);
            Assert.Null(completion.ActualQuantity);
        }

        [Fact]
        public async Task Cancellation_before_dispatch_is_persisted_without_gateway_side_effect()
        {
            var fixture = new Fixture();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await fixture.UseCase.ExecuteAsync(Request(), cancellation.Token);

            Assert.Equal(PlayerActionStatus.Cancelled, result.Status);
            Assert.Equal(0, fixture.Gateway.CallCount);
            Assert.Equal(PlayerActionStatus.Cancelled, Assert.Single(fixture.Store.Completions).Status);
        }

        [Fact]
        public async Task Up_to_available_saves_the_real_removed_quantity()
        {
            var fixture = new Fixture();
            fixture.Gateway.Result = fixture.Success(actualQuantity: 1);

            var result = await fixture.UseCase.ExecuteAsync(
                Request(quantity: 5, removalMode: PlayerItemRemovalMode.UpToAvailable),
                CancellationToken.None);

            Assert.Equal(1, result.ActualQuantity);
            Assert.Equal(1, Assert.Single(fixture.Store.Completions).ActualQuantity);
            Assert.Equal(PlayerItemRemovalMode.UpToAvailable, Assert.Single(fixture.Gateway.Commands).RemovalMode);
        }

        [Fact]
        public async Task Evidence_failure_after_side_effect_is_result_unknown_without_snapshot_links()
        {
            var fixture = new Fixture();
            fixture.Evidence.AppendException = new InvalidOperationException("evidence unavailable");

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.ResultUnknown, result.Status);
            var completion = Assert.Single(fixture.Store.Completions);
            Assert.Equal(PlayerActionStatus.ResultUnknown, completion.Status);
            Assert.Null(completion.BeforeInventorySnapshotId);
            Assert.Null(completion.AfterInventorySnapshotId);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Terminal_store_failure_does_not_reclassify_the_game_result_or_retry(
            bool throwOnComplete)
        {
            var fixture = new Fixture();
            fixture.Store.CompleteResult = false;
            if (throwOnComplete)
                fixture.Store.CompleteException = new InvalidOperationException("database unavailable");

            var result = await fixture.UseCase.ExecuteAsync(Request(), CancellationToken.None);

            Assert.Equal(PlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(3, result.ActualQuantity);
            Assert.False(result.TerminalStatePersisted);
            Assert.Single(fixture.Store.Completions);
            Assert.Equal(1, fixture.Gateway.CallCount);
        }

        private static RemoveItemRequest Request(
            string catalogVersion = "catalog-7",
            string resourceId = "resource-iron",
            int quantity = 3,
            int? quality = null,
            PlayerItemRemovalScope removalScope = PlayerItemRemovalScope.BagOnly,
            PlayerItemRemovalMode removalMode = PlayerItemRemovalMode.Exact,
            string clientRequestKey = "request-1") =>
            new RemoveItemRequest(
                "owner-1",
                new PlayerTargetStamp(
                    "EOS_123",
                    7,
                OnlineObservedAtUtc,
                "Navezgane"),
                catalogVersion,
                resourceId,
                quantity,
                quality,
                removalScope,
                removalMode,
                clientRequestKey,
                "correlation-1");

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class Fixture
        {
            private readonly Queue<string> operationIds =
                new Queue<string>(new[] { "operation-1", "operation-2", "operation-3" });
            private long clockStep;

            public Fixture(
                GameResourceCatalogReadResult? catalogRead = null,
                List<string>? order = null)
            {
                Catalog = new StubCatalog(catalogRead ?? AvailableCatalog());
                Store = new RecordingStore(order);
                Gateway = new RecordingGateway(order);
                Evidence = new RecordingEvidenceStore(order);
                UseCase = new RemoveItemUseCase(
                    Catalog,
                    Store,
                    Gateway,
                    Evidence,
                    "server-1",
                    () => operationIds.Dequeue(),
                    () => 101 + Interlocked.Increment(ref snapshotStep) - 1,
                    () => OnlineObservedAtUtc.AddSeconds(Interlocked.Increment(ref clockStep)));
                Gateway.Result = Success(3);
            }

            private long snapshotStep;

            public StubCatalog Catalog { get; }
            public RecordingStore Store { get; }
            public RecordingGateway Gateway { get; }
            public RecordingEvidenceStore Evidence { get; }
            public RemoveItemUseCase UseCase { get; }

            public RemoveItemGatewayResult Success(int actualQuantity)
            {
                var before = Snapshot(
                    OnlineObservedAtUtc.AddMilliseconds(10),
                    "before-fingerprint",
                    new InventoryItemScalar(
                        "bag", 0, "resourceIron", 5, null, null, Array.Empty<string>()));
                var afterCount = 5 - actualQuantity;
                var afterItems = afterCount == 0
                    ? Array.Empty<InventoryItemScalar>()
                    : new[]
                    {
                        new InventoryItemScalar(
                            "bag", 0, "resourceIron", afterCount, null, null, Array.Empty<string>())
                    };
                var after = Snapshot(
                    OnlineObservedAtUtc.AddMilliseconds(20),
                    "after-fingerprint",
                    afterItems);
                return RemoveItemGatewayResult.Succeeded(actualQuantity, before, after);
            }

            private static RemoveItemInventorySnapshot Snapshot(
                DateTimeOffset observedAtUtc,
                string fingerprint,
                params InventoryItemScalar[] items) =>
                new RemoveItemInventorySnapshot(
                    observedAtUtc,
                    "3.0.1-b4",
                    "catalog-7",
                    CatalogResolutionState.Resolved,
                    fingerprint,
                    items);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class StubCatalog : IGameResourceCatalog
        {
            private readonly GameResourceCatalogReadResult read;

            public StubCatalog(GameResourceCatalogReadResult read) => this.read = read;

            public GameResourceCatalogReadResult Read() => read;

            public Task<GameResourceIconReadResult> ReadIconAsync(
                string catalogVersion,
                string resourceId,
                CancellationToken cancellationToken) =>
                Task.FromResult(GameResourceIconReadResult.Missing());
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingGateway : IRemoveItemGateway
        {
            private readonly List<string>? order;

            public RecordingGateway(List<string>? order) => this.order = order;

            public List<RemoveItemCommand> Commands { get; } = new List<RemoveItemCommand>();
            public RemoveItemGatewayResult Result { get; set; } = null!;
            public int CallCount => Commands.Count;

            public Task<RemoveItemGatewayResult> RemoveAsync(
                RemoveItemCommand command,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Commands.Add(command);
                order?.Add("gateway:remove");
                return Task.FromResult(Result);
            }
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingStore : IRemoveItemOperationStore
        {
            private readonly List<string>? order;
            private RemoveItemPendingIntent? existingIntent;

            public RecordingStore(List<string>? order) => this.order = order;

            public List<RemoveItemPendingIntent> Intents { get; } = new List<RemoveItemPendingIntent>();
            public List<string> StartedOperationIds { get; } = new List<string>();
            public List<RemoveItemOperationCompletion> Completions { get; } =
                new List<RemoveItemOperationCompletion>();
            public Exception? CreateException { get; set; }
            public Exception? CompleteException { get; set; }
            public bool CompleteResult { get; set; } = true;
            public PlayerActionOperation? CurrentOperation { get; private set; }

            public PlayerActionOperation CreatePending(RemoveItemPendingIntent intent)
            {
                if (CreateException != null) throw CreateException;
                if (existingIntent != null)
                {
                    if (!existingIntent.HasSameRequest(intent))
                        throw new RemoveItemIdempotencyConflictException(
                            intent.OperatorId,
                            intent.ClientRequestKey,
                            existingIntent.OperationId);
                    return CurrentOperation!;
                }

                existingIntent = intent;
                Intents.Add(intent);
                order?.Add("store:pending");
                CurrentOperation = Operation(intent, PlayerActionStatus.Pending, null);
                return CurrentOperation;
            }

            public bool TryStart(string operationId, DateTimeOffset startedAtUtc)
            {
                StartedOperationIds.Add(operationId);
                order?.Add("store:start");
                return true;
            }

            public bool TryComplete(RemoveItemOperationCompletion completion)
            {
                Completions.Add(completion);
                order?.Add($"store:{completion.Status}");
                if (CompleteException != null) throw CompleteException;
                if (!CompleteResult) return false;
                CurrentOperation = Operation(existingIntent!, completion.Status, completion);
                return true;
            }

            private static PlayerActionOperation Operation(
                RemoveItemPendingIntent intent,
                PlayerActionStatus status,
                RemoveItemOperationCompletion? completion) =>
                new PlayerActionOperation(
                    intent.OperationId,
                    PlayerActionOperationTypes.RemoveItem,
                    intent.OperatorId,
                    intent.Target,
                    status,
                    intent.CreatedAtUtc,
                    intent.CreatedAtUtc.AddMilliseconds(1),
                    completion?.CompletedAtUtc,
                    completion?.FailureCode,
                    completion?.BeforeInventorySnapshotId,
                    completion?.AfterInventorySnapshotId,
                    null,
                    null,
                    intent.CorrelationId);
        }

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingEvidenceStore : IPlayerEvidenceStore
        {
            private readonly List<string>? order;

            public RecordingEvidenceStore(List<string>? order) => this.order = order;

            public List<PlayerInventorySnapshot> InventorySnapshots { get; } =
                new List<PlayerInventorySnapshot>();
            public Exception? AppendException { get; set; }

            public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
            {
                if (AppendException != null) throw AppendException;
                InventorySnapshots.Add(snapshot);
                order?.Add(InventorySnapshots.Count == 1 ? "evidence:before" : "evidence:after");
            }

            public void AppendSession(PlayerSession session) => throw new NotSupportedException();
            public void AppendActivity(PlayerActivityEvent activity) => throw new NotSupportedException();
            public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot) => throw new NotSupportedException();
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

        private static GameResourceCatalogReadResult AvailableCatalog() =>
            GameResourceCatalogReadResult.Available(new GameResourceCatalogSnapshot(
                "catalog-7",
                "3.0.1-b4",
                OnlineObservedAtUtc,
                new[]
                {
                    new GameResourceCatalogEntry(
                        "resource-iron",
                        1,
                        "resourceIron",
                        "铁",
                        "Iron",
                        GameResourceKind.Item,
                        GameResourceVisibility.Public,
                        100,
                        false,
                        GameResourceIconStatus.Available,
                        null)
                },
                Array.Empty<string>()));
    }
}
