using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysPlayerActionsTests
    {
        [Fact]
        public async Task Kick_capture_runs_only_inside_dispatcher_boundary()
        {
            var dispatched = false;
            var command = Command();
            using var cancellation = new CancellationTokenSource();
            var actions = new SevenDaysPlayerActions(
                dispatcher: (name, action, timeout, token) =>
                {
                    Assert.Equal("7DPanel.Players.Kick", name);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    Assert.Equal(cancellation.Token, token);
                    dispatched = true;
                    return Task.FromResult(action());
                },
                kick: actualCommand =>
                {
                    Assert.True(dispatched);
                    Assert.Same(command, actualCommand);
                    return KickPlayerActionResult.Succeeded(
                        actualCommand.EntityId,
                        "Alice",
                        actualCommand.ExpectedPlatformIdentity);
                });

            var result = await actions.KickAsync(command, cancellation.Token);

            Assert.Equal(KickPlayerActionStatus.Succeeded, result.Status);
            Assert.Equal("Alice", result.Target!.Name);
        }

        [Theory]
        [InlineData(KickPlayerActionStatus.PlayerNotOnline)]
        [InlineData(KickPlayerActionStatus.PlayerIdentityChanged)]
        public async Task Typed_action_results_are_returned_without_text_conversion(
            KickPlayerActionStatus status)
        {
            var command = Command();
            var expected = status == KickPlayerActionStatus.PlayerNotOnline
                ? KickPlayerActionResult.PlayerNotOnline()
                : KickPlayerActionResult.PlayerIdentityChanged(
                    command.EntityId,
                    "Replacement",
                    command.ExpectedPlatformIdentity);
            var actions = new SevenDaysPlayerActions(
                dispatcher: (_, action, _, _) => Task.FromResult(action()),
                kick: _ => expected);

            var result = await actions.KickAsync(command, CancellationToken.None);

            Assert.Same(expected, result);
        }

        [Fact]
        public void Missing_entity_returns_player_not_online()
        {
            var result = SevenDaysPlayerActions.ResolveTarget(
                Array.Empty<SevenDaysPlayerActions.PlayerConnectionSnapshot>(),
                Command());

            Assert.Equal(KickPlayerActionStatus.PlayerNotOnline, result.Status);
        }

        [Theory]
        [InlineData("Steam_456", "Steam")]
        [InlineData("Steam_123", "EOS")]
        public void Changed_platform_identity_returns_current_target_without_kicking(
            string combinedId,
            string platform)
        {
            var result = SevenDaysPlayerActions.ResolveTarget(
                new[]
                {
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(
                        7,
                        "Replacement",
                        combinedId,
                        platform)
                },
                Command());

            Assert.Equal(KickPlayerActionStatus.PlayerIdentityChanged, result.Status);
            Assert.Equal("Replacement", result.Target!.Name);
            Assert.Equal(combinedId, result.Target.PlatformIdentity.CombinedId);
            Assert.Equal(platform, result.Target.PlatformIdentity.Platform);
        }

        [Fact]
        public void Matching_target_returns_current_name_and_expected_identity()
        {
            var result = SevenDaysPlayerActions.ResolveTarget(
                new List<SevenDaysPlayerActions.PlayerConnectionSnapshot>
                {
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(8, "Other", "Steam_456", "Steam"),
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(7, "Current Name", "Steam_123", "Steam")
                },
                Command());

            Assert.Equal(KickPlayerActionStatus.Succeeded, result.Status);
            Assert.Equal(7, result.Target!.EntityId);
            Assert.Equal("Current Name", result.Target.Name);
            Assert.Equal("Steam_123", result.Target.PlatformIdentity.CombinedId);
        }

        [Fact]
        public void Native_kick_data_uses_manual_kick_and_approved_reason()
        {
            var data = SevenDaysPlayerActions.CreateKickDataSnapshot("违反服务器规则");

            Assert.Equal("ManualKick", data.Reason);
            Assert.Equal(0, data.ApiResponseEnum);
            Assert.Equal(default, data.BanUntil);
            Assert.Equal("违反服务器规则", data.CustomReason);
        }

        [Theory]
        [InlineData("Steam_456", "Steam")]
        [InlineData("Steam_123", "EOS")]
        public void Identity_mismatch_does_not_invoke_the_native_kick(
            string combinedId,
            string platform)
        {
            var nativeCalls = 0;
            var result = SevenDaysPlayerActions.ResolveAndKick(
                new[]
                {
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(
                        7,
                        "Replacement",
                        combinedId,
                        platform,
                        new object())
                },
                Command(),
                (_, _) => nativeCalls++);

            Assert.Equal(KickPlayerActionStatus.PlayerIdentityChanged, result.Status);
            Assert.Equal(0, nativeCalls);
        }

        [Theory]
        [InlineData("", "Steam")]
        [InlineData("Steam_123", "")]
        public void Missing_current_identity_field_returns_identity_changed_without_kicking(
            string combinedId,
            string platform)
        {
            var nativeCalls = 0;
            var result = SevenDaysPlayerActions.ResolveAndKick(
                new[]
                {
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(
                        7,
                        "Current Name",
                        combinedId,
                        platform,
                        new object())
                },
                Command(),
                (_, _) => nativeCalls++);

            Assert.Equal(KickPlayerActionStatus.PlayerIdentityChanged, result.Status);
            Assert.Equal(0, nativeCalls);
        }

        [Fact]
        public void Matching_identity_invokes_the_native_kick_once_with_the_matched_handle_and_data()
        {
            var handle = new object();
            object? actualHandle = null;
            SevenDaysPlayerActions.KickDataSnapshot? actualData = null;
            var result = SevenDaysPlayerActions.ResolveAndKick(
                new[]
                {
                    new SevenDaysPlayerActions.PlayerConnectionSnapshot(
                        7,
                        "Current Name",
                        "Steam_123",
                        "Steam",
                        handle)
                },
                Command(),
                (matchedHandle, data) =>
                {
                    actualHandle = matchedHandle;
                    actualData = data;
                });

            Assert.Equal(KickPlayerActionStatus.Succeeded, result.Status);
            Assert.Same(handle, actualHandle);
            Assert.NotNull(actualData);
            Assert.Equal("ManualKick", actualData.Reason);
            Assert.Equal(0, actualData.ApiResponseEnum);
            Assert.Equal(default, actualData.BanUntil);
            Assert.Equal("违反服务器规则", actualData.CustomReason);
        }

        private static KickPlayerCommand Command()
        {
            return new KickPlayerCommand(
                7,
                new PlayerPlatformIdentity("Steam_123", "Steam"),
                "违反服务器规则");
        }
    }
}