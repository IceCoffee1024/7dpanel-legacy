using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World;
using LSTY.SevenDPanel.Application.WorldOperations;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysMapWorldOperationHandlerTests
    {
        private static readonly DateTimeOffset CreatedAtUtc =
            new DateTimeOffset(2026, 7, 27, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Player_identity_drift_is_rejected_without_side_effect()
        {
            var sideEffects = 0;
            var handler = Handler(Context(
                targetId: "EOS_replacement",
                entityId: 7,
                stableIdentity: "EOS_replacement",
                apply: () => sideEffects++));

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.MoveOnlinePlayer,
                    new WorldEntityOperationTarget(
                        "EOS_123",
                        7,
                        "EOS_123",
                        null,
                        null,
                        null,
                        null,
                        null,
                        20,
                        10,
                        30)),
                CancellationToken.None);

            Assert.Equal(SevenDaysMapWorldOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysMapWorldOperationResult.TargetIdentityChanged, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Theory]
        [InlineData("type", SevenDaysMapWorldOperationResult.TargetTypeChanged)]
        [InlineData("position", SevenDaysMapWorldOperationResult.TargetPositionChanged)]
        public async Task Entity_type_or_observed_position_drift_is_rejected_without_side_effect(
            string drift,
            string expectedCode)
        {
            var sideEffects = 0;
            var handler = Handler(Context(
                targetId: "vehicle:type-jeep:owner-1:17",
                entityId: 17,
                stableIdentity: "vehicle:type-jeep:owner-1:17",
                entityTypeResourceId: drift == "type" ? "type-truck" : "type-jeep",
                ownerIdentity: "owner-1",
                x: drift == "position" ? 10.25 : 10,
                y: 5,
                z: 20,
                apply: () => sideEffects++));

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.MoveEntity,
                    new WorldEntityOperationTarget(
                        "vehicle:type-jeep:owner-1:17",
                        17,
                        "vehicle:type-jeep:owner-1:17",
                        "type-jeep",
                        "owner-1",
                        10,
                        5,
                        20,
                        30,
                        6,
                        40)),
                CancellationToken.None);

            Assert.Equal(SevenDaysMapWorldOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Theory]
        [InlineData("world-id", SevenDaysMapWorldOperationResult.WorldIdChanged)]
        [InlineData("world-version", SevenDaysMapWorldOperationResult.WorldVersionChanged)]
        [InlineData("map-version", SevenDaysMapWorldOperationResult.MapResourceVersionChanged)]
        public async Task World_versions_are_revalidated_before_side_effect(
            string drift,
            string expectedCode)
        {
            var sideEffects = 0;
            var context = Context(
                worldId: drift == "world-id" ? "world-2" : "world-1",
                worldVersion: drift == "world-version" ? "world-v2" : "world-v1",
                mapResourceVersion: drift == "map-version" ? "map-v2" : "map-v1",
                targetId: "EOS_123",
                entityId: 7,
                stableIdentity: "EOS_123",
                apply: () => sideEffects++);

            var result = await Handler(context).HandleAsync(
                Intent(
                    WorldOperationKind.MoveOnlinePlayer,
                    new WorldEntityOperationTarget(
                        "EOS_123",
                        7,
                        "EOS_123",
                        null,
                        null,
                        null,
                        null,
                        null,
                        20,
                        10,
                        30)),
                CancellationToken.None);

            Assert.Equal(SevenDaysMapWorldOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Fact]
        public async Task Invalid_map_bounds_are_rejected_without_side_effect()
        {
            var sideEffects = 0;
            var result = await Handler(Context(apply: () => sideEffects++))
                .HandleAsync(
                    Intent(
                        WorldOperationKind.RenderExploredMap,
                        new WorldMapOperationTarget(-101, -10, 10, 10)),
                    CancellationToken.None);

            Assert.Equal(SevenDaysMapWorldOperationOutcome.Rejected, result.Outcome);
            Assert.Equal(SevenDaysMapWorldOperationResult.MapBoundsInvalid, result.ErrorCode);
            Assert.Equal(0, sideEffects);
        }

        [Fact]
        public async Task Exception_after_side_effect_start_is_result_unknown()
        {
            var sideEffects = 0;
            var handler = Handler(Context(
                targetId: "EOS_123",
                entityId: 7,
                stableIdentity: "EOS_123",
                apply: () =>
                {
                    sideEffects++;
                    throw new InvalidOperationException("connection interrupted");
                }));

            var result = await handler.HandleAsync(
                Intent(
                    WorldOperationKind.MoveOnlinePlayer,
                    new WorldEntityOperationTarget(
                        "EOS_123",
                        7,
                        "EOS_123",
                        null,
                        null,
                        null,
                        null,
                        null,
                        20,
                        10,
                        30)),
                CancellationToken.None);

            Assert.Equal(SevenDaysMapWorldOperationOutcome.ResultUnknown, result.Outcome);
            Assert.Equal(SevenDaysMapWorldOperationResult.ResultUnknown, result.ErrorCode);
            Assert.Equal(1, sideEffects);
        }

        private static SevenDaysMapWorldOperationHandler Handler(
            SevenDaysMapWorldOperationContext context) =>
            new SevenDaysMapWorldOperationHandler(
                (name, action, timeout, _) =>
                {
                    Assert.Equal("7DPanel.World.MapOperation", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    return Task.FromResult(action());
                },
                _ => context);

        private static SevenDaysMapWorldOperationContext Context(
            string worldId = "world-1",
            string worldVersion = "world-v1",
            string? mapResourceVersion = "map-v1",
            string? targetId = null,
            long? entityId = null,
            string? stableIdentity = null,
            string? entityTypeResourceId = null,
            string? ownerIdentity = null,
            double? x = null,
            double? y = null,
            double? z = null,
            Action? apply = null) =>
            new SevenDaysMapWorldOperationContext(
                worldAvailable: true,
                worldId,
                worldVersion,
                mapResourceVersion,
                minimumX: -100,
                minimumZ: -100,
                maximumX: 100,
                maximumZ: 100,
                targetExists: targetId != null,
                targetId,
                entityId,
                stableIdentity,
                entityTypeResourceId,
                ownerIdentity,
                x,
                y,
                z,
                apply: () =>
                {
                    apply?.Invoke();
                    return null;
                });

        private static WorldOperationIntent Intent(
            WorldOperationKind kind,
            WorldOperationTarget target) =>
            new WorldOperationIntent(
                "operator-1",
                kind,
                "world-1",
                "world-v1",
                "map-v1",
                "correlation-1",
                "Approved map world operation",
                false,
                target,
                CreatedAtUtc);
    }
}
