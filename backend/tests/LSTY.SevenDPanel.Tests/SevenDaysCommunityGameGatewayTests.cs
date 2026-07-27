using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Community;
using LSTY.SevenDPanel.Application.Community;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysCommunityGameGatewayTests
    {
        [Fact]
        public async Task Fixed_target_revalidation_and_typed_send_run_inside_game_thread_dispatcher()
        {
            var insideDispatcher = false;
            var sends = 0;
            var origin = Position(1, 65, 2);
            var gateway = Gateway(
                _ => Context(
                    origin: origin,
                    send: destination =>
                    {
                        Assert.True(insideDispatcher);
                        Assert.Equal(Position(100, 70, 200), destination);
                        sends++;
                    }),
                dispatcher: (name, action, timeout, token) =>
                {
                    Assert.Equal("7DPanel.Community.Teleport", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    insideDispatcher = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatcher = false; }
                });

            var result = await gateway.TeleportAsync(Command(), CancellationToken.None);

            Assert.Equal(TeleportActionStatus.Succeeded, result.Status);
            Assert.Equal(origin, result.Origin);
            Assert.Equal(1, sends);
        }

        [Theory]
        [InlineData("offline", TeleportFailureCodes.PlayerNotOnline)]
        [InlineData("crossplatform", TeleportFailureCodes.TargetChanged)]
        [InlineData("entity", TeleportFailureCodes.TargetChanged)]
        [InlineData("world", TeleportFailureCodes.TargetChanged)]
        [InlineData("dead", TeleportFailureCodes.PlayerDead)]
        [InlineData("unspawned", TeleportFailureCodes.PlayerNotSpawned)]
        [InlineData("bounds", TeleportFailureCodes.DestinationOutOfBounds)]
        [InlineData("blood-moon", TeleportFailureCodes.BloodMoonDenied)]
        public async Task Changed_or_unsafe_runtime_target_is_rejected_before_typed_send(
            string change,
            string expectedCode)
        {
            var sends = 0;
            var gateway = Gateway(command => change == "offline"
                ? null
                : Context(
                    crossplatformId: change == "crossplatform" ? "EOS-B" : "EOS-A",
                    entityId: change == "entity" ? 8 : 7,
                    worldId: change == "world" ? "world-2" : "world-1",
                    alive: change != "dead",
                    spawned: change != "unspawned",
                    destinationInBounds: change != "bounds",
                    bloodMoon: change == "blood-moon",
                    send: _ => sends++));

            var result = await gateway.TeleportAsync(Command(), CancellationToken.None);

            Assert.Equal(TeleportActionStatus.Rejected, result.Status);
            Assert.Equal(expectedCode, result.FailureCode);
            Assert.Equal(0, sends);
        }

        [Fact]
        public async Task Send_interruption_is_result_unknown_and_is_never_retried()
        {
            var sends = 0;
            var gateway = Gateway(_ => Context(send: _ =>
            {
                sends++;
                throw new InvalidOperationException("connection interrupted");
            }));

            var result = await gateway.TeleportAsync(Command(), CancellationToken.None);

            Assert.Equal(TeleportActionStatus.ResultUnknown, result.Status);
            Assert.Equal(TeleportFailureCodes.ResultUnknown, result.FailureCode);
            Assert.Equal(1, sends);
        }

        [Fact]
        public async Task Queue_failure_and_queued_cancellation_have_no_side_effect()
        {
            var sends = 0;
            var queueFailure = Gateway(
                _ => Context(send: _ => sends++),
                dispatcher: (_, _, _, _) => Task.FromException<TeleportActionResult>(
                    new InvalidOperationException("queue unavailable")));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var queuedCancellation = Gateway(
                _ => Context(send: _ => sends++),
                dispatcher: (_, _, _, token) => Task.FromCanceled<TeleportActionResult>(token));

            var failed = await queueFailure.TeleportAsync(Command(), CancellationToken.None);
            var cancelled = await queuedCancellation.TeleportAsync(Command(), cancellation.Token);

            Assert.Equal(TeleportActionStatus.Failed, failed.Status);
            Assert.Equal(TeleportActionStatus.Cancelled, cancelled.Status);
            Assert.Equal(0, sends);
        }

        private static SevenDaysCommunityGameGateway Gateway(
            Func<TeleportActionCommand, CommunityTeleportRuntimeContext?> capture,
            Func<
                string,
                Func<TeleportActionResult>,
                TimeSpan,
                CancellationToken,
                Task<TeleportActionResult>>? dispatcher = null) =>
            new SevenDaysCommunityGameGateway(
                dispatcher ?? ((_, action, _, _) => Task.FromResult(action())),
                capture);

        private static CommunityTeleportRuntimeContext Context(
            string crossplatformId = "EOS-A",
            int entityId = 7,
            string worldId = "world-1",
            bool alive = true,
            bool spawned = true,
            bool destinationInBounds = true,
            bool bloodMoon = false,
            WorldPosition? origin = null,
            Action<WorldPosition>? send = null) => new CommunityTeleportRuntimeContext(
                crossplatformId,
                entityId,
                worldId,
                origin ?? Position(1, 65, 2),
                alive,
                spawned,
                destinationInBounds,
                bloodMoon,
                send ?? (_ => { }));

        private static TeleportActionCommand Command() => new TeleportActionCommand(
            "operation-1",
            "EOS-A",
            7,
            "world-1",
            Position(100, 70, 200),
            true);

        private static WorldPosition Position(double x, double y, double z) =>
            new WorldPosition("world-1", x, y, z, 90);
    }
}
