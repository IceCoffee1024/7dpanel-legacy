using System;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class MapWorldOperationUseCaseTests
    {
        [Fact]
        public void Delete_claim_requires_confirmation_and_enqueues_only_the_fixed_claim_target()
        {
            var bridge = new RecordingBridge();
            var useCase = new DeleteLandClaimUseCase(bridge);
            var request = new DeleteLandClaimRequest(
                "owner", "claim-1", "EOS-owner", new WorldCoordinate(1, 2, 3), 20,
                "world", "world-v1", "map-v1", "delete-claim", false, Utc());

            Assert.Throws<WorldOperationConfirmationRequiredException>(() => useCase.Execute(request));
            Assert.Null(bridge.Intent);

            request = new DeleteLandClaimRequest(
                "owner", "claim-1", "EOS-owner", new WorldCoordinate(1, 2, 3), 20,
                "world", "world-v1", "map-v1", "delete-claim", true, Utc());
            useCase.Execute(request);

            Assert.Equal(WorldOperationKind.DeleteLandClaim, bridge.Intent!.Kind);
            var target = Assert.IsType<WorldEntityOperationTarget>(bridge.Intent.Target);
            Assert.Equal("claim-1", target.TargetId);
            Assert.Equal("EOS-owner", target.OwnerIdentity);
            Assert.Null(target.DestinationX);
        }

        [Fact]
        public void Player_and_entity_moves_keep_their_distinct_fixed_identity_fields()
        {
            var bridge = new RecordingBridge();
            new MoveOnlinePlayerUseCase(bridge).Execute(new MoveOnlinePlayerRequest(
                "owner", "EOS-player", 7, Utc(), new WorldCoordinate(10, 20, 30),
                "world", "world-v1", null, "move-player", true, Utc()));
            var player = Assert.IsType<WorldEntityOperationTarget>(bridge.Intent!.Target);
            Assert.Equal(WorldOperationKind.MoveOnlinePlayer, bridge.Intent.Kind);
            Assert.Equal("EOS-player", player.StableIdentity);
            Assert.Null(player.EntityTypeResourceId);

            new MoveWorldEntityUseCase(bridge).Execute(new MoveWorldEntityRequest(
                "owner", "vehicle-1", 8, "vehicle-4x4", "EOS-owner",
                new WorldCoordinate(1, 2, 3), new WorldCoordinate(4, 5, 6),
                "world", "world-v1", null, "move-entity", true, Utc()));
            var entity = Assert.IsType<WorldEntityOperationTarget>(bridge.Intent!.Target);
            Assert.Equal(WorldOperationKind.MoveEntity, bridge.Intent.Kind);
            Assert.Equal("vehicle-4x4", entity.EntityTypeResourceId);
            Assert.Equal(1d, entity.ObservedX);
            Assert.Equal(4d, entity.DestinationX);
        }

        [Fact]
        public void Full_map_render_requires_strong_confirmation()
        {
            var bridge = new RecordingBridge();
            var useCase = new SubmitMapJobUseCase(bridge);
            var request = new SubmitMapJobRequest(
                "owner", MapJobKind.RenderFull, "world", "world-v1", "map-v1",
                new WorldMapBounds(-10, -20, 10, 20), "render-full", true, false, Utc());

            Assert.Throws<WorldOperationStrongConfirmationRequiredException>(() => useCase.Execute(request));
            Assert.Null(bridge.Intent);

            request = new SubmitMapJobRequest(
                "owner", MapJobKind.RenderFull, "world", "world-v1", "map-v1",
                new WorldMapBounds(-10, -20, 10, 20), "render-full", true, true, Utc());
            useCase.Execute(request);
            Assert.Equal(WorldOperationKind.RenderFullMap, bridge.Intent!.Kind);
            Assert.IsType<WorldMapOperationTarget>(bridge.Intent.Target);
        }

        private static DateTimeOffset Utc() =>
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero);

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
