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
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "SevenDays")]
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
        public async Task Query_maps_fixed_runtime_metrics_with_shared_observation_time()
        {
            var query = CreateQuery(CreateAvailableSample());

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            var metrics = Assert.IsType<GameRuntimeMetrics>(snapshot.RuntimeMetrics);
            Assert.Equal("Day 4 13:27", metrics.GameDayTime.Value);
            Assert.False(metrics.IsBloodMoon.Value);
            Assert.Equal(58.5d, metrics.FramesPerSecond.Value);
            Assert.Equal(3, metrics.OnlinePlayerCount.Value);
            Assert.Equal(17, metrics.HistoricalPlayerCount.Value);
            Assert.Equal(4, metrics.AnimalCount.Value);
            Assert.Equal(9, metrics.HostileEntityCount.Value);
            Assert.Equal(25, metrics.ActiveEntityCount.Value);
            Assert.Equal(144, metrics.ChunkCount.Value);
            Assert.Equal(6, metrics.DroppedItemCount.Value);
            Assert.Equal(123456L, metrics.GameMemoryBytes.Value);
            Assert.Equal("World.worldTime", metrics.GameDayTime.Source);
            Assert.Equal("game-clock", metrics.GameDayTime.Unit);
            Assert.Equal("World.aiDirector.BloodMoonComponent.BloodMoonActive", metrics.IsBloodMoon.Source);
            Assert.Equal("boolean", metrics.IsBloodMoon.Unit);
            Assert.Equal("GameManager.frameTime", metrics.FramesPerSecond.Source);
            Assert.Equal("frames/second", metrics.FramesPerSecond.Unit);
            Assert.Equal("World.Players.Count", metrics.OnlinePlayerCount.Source);
            Assert.Equal("GameManager.persistentPlayerCount", metrics.HistoricalPlayerCount.Source);
            Assert.Equal("World.Entities", metrics.AnimalCount.Source);
            Assert.Equal("World.Entities", metrics.HostileEntityCount.Source);
            Assert.Equal("World.Entities", metrics.ActiveEntityCount.Source);
            Assert.Equal("Chunk.InstanceCount", metrics.ChunkCount.Source);
            Assert.Equal("World.Entities", metrics.DroppedItemCount.Source);
            Assert.Equal("GC.GetTotalMemory(false)", metrics.GameMemoryBytes.Source);
            Assert.All(new[]
            {
                metrics.OnlinePlayerCount.Unit,
                metrics.HistoricalPlayerCount.Unit,
                metrics.AnimalCount.Unit,
                metrics.HostileEntityCount.Unit,
                metrics.ActiveEntityCount.Unit,
                metrics.ChunkCount.Unit,
                metrics.DroppedItemCount.Unit
            }, unit => Assert.Equal("count", unit));
            Assert.Equal("bytes", metrics.GameMemoryBytes.Unit);
            Assert.All(new[]
            {
                metrics.GameDayTime.ObservedAtUtc,
                metrics.IsBloodMoon.ObservedAtUtc,
                metrics.FramesPerSecond.ObservedAtUtc,
                metrics.OnlinePlayerCount.ObservedAtUtc,
                metrics.HistoricalPlayerCount.ObservedAtUtc,
                metrics.AnimalCount.ObservedAtUtc,
                metrics.HostileEntityCount.ObservedAtUtc,
                metrics.ActiveEntityCount.ObservedAtUtc,
                metrics.ChunkCount.ObservedAtUtc,
                metrics.DroppedItemCount.ObservedAtUtc,
                metrics.GameMemoryBytes.ObservedAtUtc
            }, observedAtUtc => Assert.Equal(SampledAt, observedAtUtc));
            Assert.Equal(8, snapshot.MaximumPlayerCount);
            Assert.Equal("2.1.0", snapshot.Version);
            Assert.Equal("SurvivalMP", snapshot.GameMode);
            Assert.Equal("Warrior", snapshot.Difficulty);
            Assert.Equal("Europe", snapshot.Region);
            Assert.Equal("en", snapshot.Language);
            Assert.Equal("203.0.113.10", snapshot.ConnectionAddress);
            Assert.Equal(26900, snapshot.ConnectionPort);
        }

        [Fact]
        public void Query_and_capture_contract_do_not_expose_live_game_handles_or_legacy_metric_aliases()
        {
            var permitted = new[]
            {
                typeof(string), typeof(int), typeof(int?), typeof(long), typeof(long?),
                typeof(double), typeof(double?), typeof(bool), typeof(bool?), typeof(DateTimeOffset)
            };
            var captureProperties = typeof(SevenDaysGameOverviewSample)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);

            Assert.All(captureProperties.Where(property => property.Name != "RuntimeMetrics"), property =>
                Assert.Contains(property.PropertyType, permitted));
            var metricSampleTypes = typeof(SevenDaysGameRuntimeMetricsSample)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.PropertyType)
                .ToArray();
            Assert.All(metricSampleTypes, type =>
            {
                Assert.True(type.IsGenericType);
                Assert.Equal(typeof(SevenDaysMetricSample<>), type.GetGenericTypeDefinition());
            });
            Assert.DoesNotContain("GameName", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("MapName", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("UnityHeapBytes", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain("ServerUptimeSeconds", captureProperties.Select(property => property.Name));
            Assert.DoesNotContain(
                typeof(SevenDaysGameOverviewQuery)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                field => field.FieldType == typeof(SevenDaysGameOverviewSample));

            var snapshotProperties = typeof(GameOverviewSnapshot)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();
            Assert.DoesNotContain("GameTime", snapshotProperties);
            Assert.DoesNotContain("FramesPerSecond", snapshotProperties);
            Assert.DoesNotContain("OnlinePlayerCount", snapshotProperties);
            Assert.DoesNotContain("HistoricalPlayerCount", snapshotProperties);
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
        public async Task Dispatch_timeout_without_history_maps_to_unavailable_without_exception_details()
        {
            var query = new SevenDaysGameOverviewQuery(
                (_, _, _) => Task.FromException<SevenDaysGameOverviewSample>(new TimeoutException("private timeout")),
                CreateAvailableSample,
                () => SampledAt,
                TimeSpan.FromSeconds(4));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Unavailable, snapshot.Availability);
            Assert.Null(snapshot.SampledAtUtc);
            Assert.Null(snapshot.GameTitle);
        }

        [Fact]
        public async Task Dispatch_timeout_preserves_the_last_successful_snapshot_as_stale()
        {
            var now = SampledAt;
            var dispatchCount = 0;
            var query = new SevenDaysGameOverviewQuery(
                (_, action, _) =>
                {
                    dispatchCount++;
                    return dispatchCount == 1
                        ? Task.FromResult(action())
                        : Task.FromException<SevenDaysGameOverviewSample>(new TimeoutException("private timeout"));
                },
                CreateAvailableSample,
                () => now,
                TimeSpan.FromSeconds(4));

            var available = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);
            now = now.AddSeconds(5);
            var stale = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, available.Availability);
            Assert.Equal(AvailabilityState.Stale, stale.Availability);
            Assert.Equal(available.SampledAtUtc, stale.SampledAtUtc);
            Assert.Equal(available.SaveGameName, stale.SaveGameName);
            Assert.Equal(
                available.RuntimeMetrics!.FramesPerSecond.Value,
                stale.RuntimeMetrics!.FramesPerSecond.Value);
        }

        [Fact]
        public async Task Unavailable_fields_remain_null_in_an_available_snapshot()
        {
            var query = CreateQuery(new SevenDaysGameOverviewSample(
                true,
                "Save",
                "World",
                10L,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                CreateUnavailableMetricSample()));

            var snapshot = await query.GetGameOverviewAsync(TestContext.Current.CancellationToken);

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Null(snapshot.Version);
            var metrics = Assert.IsType<GameRuntimeMetrics>(snapshot.RuntimeMetrics);
            Assert.Null(metrics.OnlinePlayerCount.Value);
            Assert.Equal(RuntimeMetricWarningCode.ReadFailed, metrics.OnlinePlayerCount.Warning);
            Assert.Null(metrics.FramesPerSecond.Value);
            Assert.Equal(RuntimeMetricWarningCode.ReadFailed, metrics.FramesPerSecond.Warning);
            Assert.Null(metrics.GameDayTime.Value);
            Assert.Equal(RuntimeMetricWarningCode.Unsupported, metrics.GameDayTime.Warning);
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
                    true, "Save " + dispatchCount, "World", 10L, null, null, null, null, null, null, null, null,
                    CreateUnavailableMetricSample()),
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
                    true, "Save " + dispatchCount, "World", 10L, null, null, null, null, null, null, null, null,
                    CreateUnavailableMetricSample()),
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
                "203.0.113.10", 26900, 8,
                new SevenDaysGameRuntimeMetricsSample(
                    new SevenDaysMetricSample<string>("Day 4 13:27", null),
                    new SevenDaysMetricSample<bool?>(false, null),
                    new SevenDaysMetricSample<double?>(58.5d, null),
                    new SevenDaysMetricSample<int?>(3, null),
                    new SevenDaysMetricSample<int?>(17, null),
                    new SevenDaysMetricSample<int?>(4, null),
                    new SevenDaysMetricSample<int?>(9, null),
                    new SevenDaysMetricSample<int?>(25, null),
                    new SevenDaysMetricSample<int?>(144, null),
                    new SevenDaysMetricSample<int?>(6, null),
                    new SevenDaysMetricSample<long?>(123456L, null)));

        private static SevenDaysGameRuntimeMetricsSample CreateUnavailableMetricSample() =>
            new SevenDaysGameRuntimeMetricsSample(
                new SevenDaysMetricSample<string>(null!, RuntimeMetricWarningCode.Unsupported),
                new SevenDaysMetricSample<bool?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<double?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<int?>(null, RuntimeMetricWarningCode.ReadFailed),
                new SevenDaysMetricSample<long?>(null, RuntimeMetricWarningCode.ReadFailed));
    }
}
