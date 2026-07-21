using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Players;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Runtime;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysOnlinePlayerQueryTests
    {
        [Fact]
        public async Task Empty_snapshot_returns_empty_players()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, _, _, _) => Task.FromResult(new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>())),
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            var snapshot = await query.GetOnlineAsync(CancellationToken.None);

            Assert.Equal(capturedAtUtc, snapshot.CapturedAtUtc);
            Assert.Empty(snapshot.Players);
        }

        [Fact]
        public async Task Snapshot_is_sorted_by_entity_id_and_includes_nullable_crossplatform_identity()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var expectedPlatform = new PlayerPlatformIdentity("steam:2", "Steam");
            var expectedCrossplatform = new PlayerPlatformIdentity("eos:2", "EOS");
            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, _, _, _) => Task.FromResult(new OnlinePlayersSnapshot(capturedAtUtc, new[]
                {
                    new PlayerSnapshot(42, "Zed", expectedPlatform, null, 12, 20, 100),
                    new PlayerSnapshot(7, "Amy", expectedPlatform, expectedCrossplatform, 8, 16, 90),
                    new PlayerSnapshot(11, "Bob", expectedPlatform, expectedCrossplatform, 10, 18, 95)
                })),
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            var snapshot = await query.GetOnlineAsync(CancellationToken.None);

            Assert.Collection(snapshot.Players,
                player =>
                {
                    Assert.Equal(7, player.EntityId);
                    Assert.Equal("Amy", player.Name);
                    Assert.NotNull(player.CrossplatformIdentity);
                    Assert.Equal("eos:2", player.CrossplatformIdentity!.CombinedId);
                },
                player =>
                {
                    Assert.Equal(11, player.EntityId);
                    Assert.Equal("Bob", player.Name);
                },
                player =>
                {
                    Assert.Equal(42, player.EntityId);
                    Assert.Equal("Zed", player.Name);
                    Assert.Null(player.CrossplatformIdentity);
                });
        }

        [Fact]
        public async Task Concurrent_second_query_fails_immediately_and_gate_releases_after_success()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var firstStarted = new ManualResetEventSlim(false);
            var releaseFirst = new ManualResetEventSlim(false);
            var dispatcherCalls = 0;

            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, _, _, _) =>
                {
                    dispatcherCalls++;
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(5));
                    return Task.FromResult(new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()));
                },
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            var first = Task.Run(
                () => query.GetOnlineAsync(TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
            Assert.True(firstStarted.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            var secondTask = query.GetOnlineAsync(CancellationToken.None);
            var second = await Assert.ThrowsAsync<OnlinePlayerQueryBusyException>(async () => await secondTask);
            Assert.NotNull(second);

            releaseFirst.Set();
            await first;
            Assert.Equal(1, dispatcherCalls);

            var third = await query.GetOnlineAsync(CancellationToken.None);
            Assert.Empty(third.Players);
            Assert.Equal(2, dispatcherCalls);
        }

        [Fact]
        public async Task Separate_query_instances_do_not_share_the_single_flight_gate()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var firstStarted = new ManualResetEventSlim(false);
            var releaseFirst = new ManualResetEventSlim(false);
            var firstQuery = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, _, _, _) =>
                {
                    firstStarted.Set();
                    releaseFirst.Wait(TimeSpan.FromSeconds(5));
                    return Task.FromResult(new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()));
                },
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);
            var secondQuery = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, action, _, _) => Task.FromResult(action()),
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            var first = Task.Run(
                () => firstQuery.GetOnlineAsync(TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);
            Assert.True(firstStarted.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));

            var second = await secondQuery.GetOnlineAsync(CancellationToken.None);

            Assert.Empty(second.Players);
            releaseFirst.Set();
            await first;
        }

        [Fact]
        public async Task Gate_releases_after_exception()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var dispatchCount = 0;
            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, action, _, _) =>
                {
                    dispatchCount++;
                    if (dispatchCount == 1)
                    {
                        return Task.FromException<OnlinePlayersSnapshot>(new InvalidOperationException("boom"));
                    }

                    return Task.FromResult(action());
                },
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            await Assert.ThrowsAsync<InvalidOperationException>(() => query.GetOnlineAsync(CancellationToken.None));
            var second = await query.GetOnlineAsync(CancellationToken.None);
            Assert.Empty(second.Players);
            Assert.Equal(2, dispatchCount);
        }

        [Fact]
        public void Missing_platform_identity_is_a_field_read_failure()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SevenDaysOnlinePlayerQuery.CreatePlatformIdentityFromStrings(string.Empty, "Steam"));
        }

        [Fact]
        public void Missing_player_level_is_a_field_read_failure()
        {
            Assert.Throws<InvalidOperationException>(() =>
                SevenDaysOnlinePlayerQuery.RequireLevel(null));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Gate_releases_after_cancellation_or_timeout(bool cancel)
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var dispatchCount = 0;
            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (_, action, _, _) =>
                {
                    dispatchCount++;
                    if (dispatchCount == 1)
                    {
                        return cancel
                            ? Task.FromCanceled<OnlinePlayersSnapshot>(new CancellationToken(true))
                            : Task.FromException<OnlinePlayersSnapshot>(new TimeoutException());
                    }

                    return Task.FromResult(action());
                },
                capture: () => new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>()),
                utcClock: () => capturedAtUtc);

            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    query.GetOnlineAsync(TestContext.Current.CancellationToken));
            }
            else
            {
                await Assert.ThrowsAsync<TimeoutException>(() =>
                    query.GetOnlineAsync(TestContext.Current.CancellationToken));
            }

            var second = await query.GetOnlineAsync(TestContext.Current.CancellationToken);

            Assert.Empty(second.Players);
            Assert.Equal(2, dispatchCount);
        }

        [Fact]
        public async Task Capture_delegate_runs_only_inside_dispatcher_boundary()
        {
            var capturedAtUtc = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var captureInvocations = 0;
            var dispatchInvocations = 0;
            var query = new SevenDaysOnlinePlayerQuery(
                dispatcher: (operationName, action, timeout, cancellationToken) =>
                {
                    dispatchInvocations++;
                    Assert.Equal("7DPanel.Players.Online", operationName);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    return Task.FromResult(action());
                },
                capture: () =>
                {
                    captureInvocations++;
                    return new OnlinePlayersSnapshot(capturedAtUtc, Array.Empty<PlayerSnapshot>());
                },
                utcClock: () => capturedAtUtc);

            await query.GetOnlineAsync(CancellationToken.None);

            Assert.Equal(1, dispatchInvocations);
            Assert.Equal(1, captureInvocations);
        }
    }
}
