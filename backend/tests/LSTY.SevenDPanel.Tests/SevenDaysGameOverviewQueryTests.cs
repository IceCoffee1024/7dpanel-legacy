using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Overview;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysGameOverviewQueryTests
    {
        private static readonly DateTimeOffset SampledAt =
            new DateTimeOffset(2026, 7, 25, 8, 0, 0, TimeSpan.Zero);

        [Fact]
        public async Task Query_maps_game_preferences_title_and_world_session_uptime()
        {
            var query = CreateQuery(CreateAvailableSample());

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal("7 Days to Die", snapshot.GameTitle);
            Assert.Equal("Navezgane Save", snapshot.SaveGameName);
            Assert.Equal("Navezgane", snapshot.WorldName);
            Assert.Equal(321L, snapshot.WorldSessionUptimeSeconds);
            Assert.Equal(SampledAt, snapshot.SampledAtUtc);
        }

        [Fact]
        public async Task Query_maps_fps_player_counts_and_game_time()
        {
            var query = CreateQuery(CreateAvailableSample());

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(58.5d, snapshot.FramesPerSecond);
            Assert.Equal(3, snapshot.OnlinePlayerCount);
            Assert.Equal(8, snapshot.MaximumPlayerCount);
            Assert.Equal(17, snapshot.HistoricalPlayerCount);
            Assert.Equal("Day 4 13:27", snapshot.GameTime);
            Assert.Equal("2.1.0", snapshot.Version);
            Assert.Equal("SurvivalMP", snapshot.GameMode);
            Assert.Equal("Warrior", snapshot.Difficulty);
            Assert.Equal("Europe", snapshot.Region);
            Assert.Equal("en", snapshot.Language);
            Assert.Equal("203.0.113.10", snapshot.ConnectionAddress);
            Assert.Equal(26900, snapshot.ConnectionPort);
        }

        [Fact]
        public void Query_and_capture_contract_do_not_expose_live_game_handles_or_legacy_fields()
        {
            var permitted = new[]
            {
                typeof(string), typeof(int), typeof(int?), typeof(long), typeof(long?),
                typeof(double), typeof(double?), typeof(bool), typeof(bool?), typeof(DateTimeOffset)
            };
            var captureProperties = typeof(SevenDaysGameOverviewSample)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.All(captureProperties, property =>
                Assert.Contains(property.PropertyType, permitted));
            Assert.DoesNotContain("GameName", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("MapName", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("UnityHeapBytes", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("ServerUptimeSeconds", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain(
                typeof(SevenDaysGameOverviewQuery)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                field => field.FieldType == typeof(SevenDaysGameOverviewSample));
        }

        [Fact]
        public async Task Readiness_and_scalar_capture_run_inside_the_same_dispatch_callback()
        {
            var insideDispatchCallback = false;
            var readinessReadCount = 0;
            var captureCount = 0;
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    dispatchCount++;
                    insideDispatchCallback = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatchCallback = false; }
                },
                () =>
                {
                    Assert.True(insideDispatchCallback);
                    readinessReadCount++;
                    var isReady = true;
                    if (!isReady) return SevenDaysGameOverviewSample.NotReady();
                    captureCount++;
                    return CreateAvailableSample();
                },
                () => SampledAt,
                TimeSpan.FromSeconds(4));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal(1, dispatchCount);
            Assert.Equal(1, readinessReadCount);
            Assert.Equal(1, captureCount);
        }

        [Fact]
        public async Task Not_ready_maps_to_unavailable_after_one_dispatched_readiness_check()
        {
            var insideDispatchCallback = false;
            var readinessReadCount = 0;
            var captureCount = 0;
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    dispatchCount++;
                    insideDispatchCallback = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatchCallback = false; }
                },
                () =>
                {
                    Assert.True(insideDispatchCallback);
                    readinessReadCount++;
                    var isReady = false;
                    if (!isReady) return SevenDaysGameOverviewSample.NotReady();
                    captureCount++;
                    return CreateAvailableSample();
                },
                () => SampledAt,
                TimeSpan.FromSeconds(4));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Unavailable, snapshot.Availability);
            Assert.Null(snapshot.SampledAtUtc);
            Assert.Equal(1, dispatchCount);
            Assert.Equal(1, readinessReadCount);
            Assert.Equal(0, captureCount);
        }

        [Fact]
        public async Task Dispatch_timeout_maps_to_stale_without_exception_details()
        {
            var query = new SevenDaysGameOverviewQuery(
                (_, _, _) => Task.FromException<SevenDaysGameOverviewSample>(new TimeoutException("private timeout")),
                CreateAvailableSample,
                () => SampledAt,
                TimeSpan.FromSeconds(4));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Stale, snapshot.Availability);
            Assert.Null(snapshot.SampledAtUtc);
            Assert.Null(snapshot.GameTitle);
        }

        [Fact]
        public async Task Unavailable_fields_remain_null_in_an_available_snapshot()
        {
            var query = CreateQuery(new SevenDaysGameOverviewSample(
                true, "Save", "World", 10L, null, null, null, null, null, null, null, null, null, null, null, null));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Null(snapshot.Version);
            Assert.Null(snapshot.OnlinePlayerCount);
            Assert.Null(snapshot.FramesPerSecond);
            Assert.Null(snapshot.GameTime);
        }

        [Fact]
        public async Task Concurrent_callers_share_one_in_flight_capture_and_cached_sample()
        {
            var captureStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCapture = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                async (_, action, _) =>
                {
                    Interlocked.Increment(ref dispatchCount);
                    captureStarted.TrySetResult(true);
                    await releaseCapture.Task;
                    return action();
                },
                CreateAvailableSample,
                () => SampledAt,
                TimeSpan.FromSeconds(4));

            var requests = Enumerable.Range(0, 8)
                .Select(_ => query.GetGameOverviewAsync(TestContext.Current.CancellationToken))
                .ToArray();
            await captureStarted.Task;
            releaseCapture.TrySetResult(true);
            var snapshots = await Task.WhenAll(requests);
            var cached = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, dispatchCount);
            Assert.All(snapshots, snapshot => Assert.Equal(SampledAt, snapshot.SampledAtUtc));
            Assert.Equal(SampledAt, cached.SampledAtUtc);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Simultaneous_callers_share_one_gated_capture_until_any_result_is_published(
            bool gameReady)
        {
            const int callerCount = 8;
            using var startBarrier = new Barrier(callerCount + 1);
            using var callersReturnedFromQuery = new CountdownEvent(callerCount);
            using var captureStarted = new ManualResetEventSlim(false);
            using var releaseCapture = new ManualResetEventSlim(false);
            var dispatchCount = 0;
            var captureCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    Interlocked.Increment(ref dispatchCount);
                    return Task.Run(action, TestContext.Current.CancellationToken);
                },
                () =>
                {
                    Interlocked.Increment(ref captureCount);
                    captureStarted.Set();
                    releaseCapture.Wait(TestContext.Current.CancellationToken);
                    return gameReady
                        ? CreateAvailableSample()
                        : SevenDaysGameOverviewSample.NotReady();
                },
                () => SampledAt,
                TimeSpan.FromSeconds(4));
            var callers = Enumerable.Range(0, callerCount)
                .Select(_ => Task.Run(async () =>
                {
                    startBarrier.SignalAndWait(TestContext.Current.CancellationToken);
                    var request = query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
                    callersReturnedFromQuery.Signal();
                    return await request;
                }, TestContext.Current.CancellationToken))
                .ToArray();

            startBarrier.SignalAndWait(TestContext.Current.CancellationToken);
            Assert.True(captureStarted.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
            var allCallersJoinedInFlight = callersReturnedFromQuery.Wait(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
            releaseCapture.Set();
            Assert.True(allCallersJoinedInFlight);
            var snapshots = await Task.WhenAll(callers);

            Assert.Equal(1, dispatchCount);
            Assert.Equal(1, captureCount);
            Assert.All(snapshots, snapshot => Assert.Equal(
                gameReady ? AvailabilityState.Available : AvailabilityState.Unavailable,
                snapshot.Availability));
        }

        [Fact]
        public async Task Expired_cache_captures_a_fresh_sample()
        {
            var now = SampledAt;
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    dispatchCount++;
                    return Task.FromResult(action());
                },
                () => new SevenDaysGameOverviewSample(
                    true, "Save " + dispatchCount, "World", 10L, null, null, null, null, null, null, null, null, null, null, null, null),
                () => now,
                TimeSpan.FromSeconds(4));

            var first = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
            now = now.AddSeconds(3);
            var cached = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
            now = now.AddSeconds(2);
            var refreshed = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Save 1", first.SaveGameName);
            Assert.Equal("Save 1", cached.SaveGameName);
            Assert.Equal("Save 2", refreshed.SaveGameName);
            Assert.Equal(2, dispatchCount);
            Assert.Equal(now, refreshed.SampledAtUtc);
        }

        [Fact]
        public async Task Clock_rollback_does_not_reuse_a_future_dated_cached_sample()
        {
            var now = SampledAt;
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    dispatchCount++;
                    return Task.FromResult(action());
                },
                () => new SevenDaysGameOverviewSample(
                    true, "Save " + dispatchCount, "World", 10L, null, null, null, null, null, null, null, null, null, null, null, null),
                () => now,
                TimeSpan.FromSeconds(4));

            var first = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
            now = now.AddMinutes(-1);
            var refreshed = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal("Save 1", first.SaveGameName);
            Assert.Equal("Save 2", refreshed.SaveGameName);
            Assert.Equal(2, dispatchCount);
            Assert.Equal(now, refreshed.SampledAtUtc);
        }

        [Fact]
        public async Task Cancelling_one_waiter_does_not_cancel_the_shared_capture()
        {
            var captureStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCapture = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var query = new SevenDaysGameOverviewQuery(
                async (_, action, _) =>
                {
                    captureStarted.TrySetResult(true);
                    await releaseCapture.Task;
                    return action();
                },
                CreateAvailableSample,
                () => SampledAt,
                TimeSpan.FromSeconds(4));
            using var cancelledWaiter = new CancellationTokenSource();

            var cancelled = query.GetGameOverviewAsync(cancelledWaiter.Token);
            await captureStarted.Task;
            var surviving = query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
            cancelledWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            releaseCapture.TrySetResult(true);
            var snapshot = await surviving;

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
        }

        private static SevenDaysGameOverviewQuery CreateQuery(SevenDaysGameOverviewSample sample) =>
            new SevenDaysGameOverviewQuery(
                (_, action, _) => Task.FromResult(action()),
                () => sample,
                () => SampledAt,
                TimeSpan.FromSeconds(4));

        private static SevenDaysGameOverviewSample CreateAvailableSample() =>
            new SevenDaysGameOverviewSample(
                true, "Navezgane Save", "Navezgane", 321L, "2.1.0", "SurvivalMP", "Warrior", "Europe", "en",
                "203.0.113.10", 26900, 3, 8, 17, 58.5d, "Day 4 13:27");
    }
}
