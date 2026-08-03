using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Administration")]
    [Trait("Boundary", "Application")]
    public sealed class EntityMaintenanceUseCaseTests
    {
        private const string EntityTypeResourceId = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private static readonly DateTimeOffset RequestedAtUtc =
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Spawn_submission_requires_strong_confirmation_and_keeps_only_catalog_identity_and_bounds()
        {
            var bridge = new RecordingBridge();
            var catalog = new StubCatalog(EntityTypeResourceId);
            var useCase = new SpawnWorldEntityUseCase(bridge, catalog);

            Assert.Throws<WorldOperationStrongConfirmationRequiredException>(() =>
                useCase.Execute(new SpawnWorldEntityRequest(
                    "operator-1", "world-1", "world-v1", "map-v1", catalog.Version,
                    EntityTypeResourceId, 3, new WorldCoordinate(10, 20, 30), 12,
                    "spawn-1", true, false, RequestedAtUtc)));
            Assert.Null(bridge.Intent);

            useCase.Execute(new SpawnWorldEntityRequest(
                "operator-1", "world-1", "world-v1", "map-v1", catalog.Version,
                EntityTypeResourceId, 3, new WorldCoordinate(10, 20, 30), 12,
                "spawn-1", true, true, RequestedAtUtc));

            Assert.Equal(WorldOperationKind.SpawnEntity, bridge.Intent!.Kind);
            var target = Assert.IsType<WorldEntityOperationTarget>(bridge.Intent.Target);
            Assert.Equal(EntityTypeResourceId, target.EntityTypeResourceId);
            Assert.Equal(3, target.Quantity);
            Assert.Equal(12, target.Radius);
            Assert.Equal(10, target.DestinationX);
            Assert.Null(target.EntityId);
            Assert.Null(target.EntityCategory);
        }

        [Fact]
        public async Task Entity_live_state_is_captured_revalidated_and_mutated_only_inside_dispatch()
        {
            var trace = new List<string>();
            var handler = Handler(
                _ =>
                {
                    trace.Add("context");
                    return SevenDaysEntityMaintenanceContext.ForSpawn(
                        "world-1", "world-v1", "map-v1", EntityTypeResourceId,
                        entityTypeIsPlayer: false,
                        entityTypeIsProtected: false,
                        apply: () =>
                        {
                            trace.Add("apply");
                            return true;
                        });
                },
                trace);

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.SpawnEntity,
                    new WorldEntityOperationTarget(
                        "spawn-correlation-1", null, null, EntityTypeResourceId, null,
                        null, null, null, 1, 2, 3, 2, 10, null)),
                CancellationToken.None);

            Assert.Equal(SevenDaysEntityMaintenanceOutcome.Succeeded, result.Outcome);
            Assert.Null(result.ErrorCode);
            Assert.Equal(new[] { "dispatch", "context", "apply" }, trace);
        }

        [Fact]
        public async Task Delete_rejects_reused_entity_id_when_the_type_identity_changed()
        {
            var sideEffects = 0;
            var handler = Handler(
                _ => SevenDaysEntityMaintenanceContext.ForDelete(
                    "world-1", "world-v1", "map-v1",
                    targetExists: true,
                    targetId: "entity-42",
                    entityId: 42,
                    entityTypeResourceId: "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
                    ownerStableIdentity: null,
                    observedX: 1,
                    observedY: 2,
                    observedZ: 3,
                    isPlayer: false,
                    isProtected: false,
                    apply: () =>
                    {
                        sideEffects++;
                        return true;
                    }),
                new List<string>());

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.DeleteEntity,
                    new WorldEntityOperationTarget(
                        "entity-42", 42, "entity-42", EntityTypeResourceId, null,
                        1, 2, 3, null, null, null)),
                CancellationToken.None);

            Assert.Equal(SevenDaysEntityMaintenanceOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysEntityMaintenanceResult.TargetTypeChanged, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Theory]
        [InlineData(true, false, SevenDaysEntityMaintenanceResult.PlayerEntityForbidden)]
        [InlineData(false, true, SevenDaysEntityMaintenanceResult.ProtectedEntityForbidden)]
        public async Task Cleanup_rejects_player_or_protected_candidates_before_mutation(
            bool containsPlayer,
            bool containsProtected,
            string expectedCode)
        {
            var sideEffects = 0;
            var handler = Handler(
                _ => SevenDaysEntityMaintenanceContext.ForCleanup(
                    "world-1", "world-v1", "map-v1", WorldEntityCategory.Hostile,
                    candidateCount: 1,
                    containsPlayer,
                    containsProtected,
                    apply: () =>
                    {
                        sideEffects++;
                        return true;
                    }),
                new List<string>());

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.CleanupEntities,
                    new WorldEntityOperationTarget(
                        "cleanup-Hostile", null, null, null, null,
                        null, null, null, 0, 0, 0, 10, 50, "Hostile")),
                CancellationToken.None);

            Assert.Equal(SevenDaysEntityMaintenanceOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Fact]
        public async Task Exception_after_entity_side_effect_start_is_sanitized_result_unknown()
        {
            var handler = Handler(
                _ => SevenDaysEntityMaintenanceContext.ForSpawn(
                    "world-1", "world-v1", "map-v1", EntityTypeResourceId,
                    entityTypeIsPlayer: false,
                    entityTypeIsProtected: false,
                    apply: () => throw new InvalidOperationException(
                        "C:\\private\\entity.xml secret")),
                new List<string>());

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.SpawnEntity,
                    new WorldEntityOperationTarget(
                        "spawn-correlation-1", null, null, EntityTypeResourceId, null,
                        null, null, null, 1, 2, 3, 2, 10, null)),
                CancellationToken.None);

            Assert.Equal(SevenDaysEntityMaintenanceOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysEntityMaintenanceResult.ResultUnknown, result.ErrorCode);
            Assert.DoesNotContain("private", result.ErrorCode!, StringComparison.OrdinalIgnoreCase);
        }

        private static SevenDaysEntityMaintenanceHandler Handler(
            Func<WorldOperationIntent, SevenDaysEntityMaintenanceContext?> captureContext,
            ICollection<string> trace) =>
            new SevenDaysEntityMaintenanceHandler(
                (name, action, timeout, _) =>
                {
                    Assert.Equal("7DPanel.World.EntityMaintenance", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    trace.Add("dispatch");
                    return Task.FromResult(action());
                },
                captureContext);

        private static WorldOperationIntent Intent(
            WorldOperationKind kind,
            WorldOperationTarget target) =>
            new WorldOperationIntent(
                "operator-1", kind, "world-1", "world-v1", "map-v1",
                "correlation-1", "Approved entity maintenance", false, target,
                RequestedAtUtc);

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class StubCatalog : IWorldToolCatalog
        {
            private readonly string entityTypeResourceId;

            public StubCatalog(string entityTypeResourceId)
            {
                this.entityTypeResourceId = entityTypeResourceId;
                Version = "catalog-v1";
            }

            public string Version { get; }

            public WorldToolCatalogSnapshot Read() => WorldToolCatalogSnapshot.Available(
                Version,
                RequestedAtUtc,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { entityTypeResourceId });
        }

        [Trait("Capability", "Administration")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingBridge : IWorldOperationJobBridge
        {
            public WorldOperationIntent? Intent { get; private set; }

            public WorldOperationReceipt Enqueue(WorldOperationIntent intent)
            {
                Intent = intent;
                return new WorldOperationReceipt(
                    "operation-1",
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    WorldOperationStatus.Queued,
                    intent.CorrelationId,
                    intent.CreatedAtUtc);
            }

            public WorldOperationRecord Get(string operationId) => throw new NotSupportedException();
            public WorldOperationPage Query(WorldOperationQuery query) => throw new NotSupportedException();
            public bool RequestCancellation(string operationId, string actorSubject) => false;
        }
    }
}
