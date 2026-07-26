using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Hosting;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysMapMetadataProjectionTests
    {
        private static readonly DateTimeOffset ObservedAt =
            new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset LaterObservation = ObservedAt.AddMinutes(1);

        [Fact]
        public void Projection_starts_unavailable_and_publishes_finite_world_metadata()
        {
            var projection = new SevenDaysMapMetadataProjection();
            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);

            projection.Publish(CreateSample(), ObservedAt);
            var snapshot = projection.Query();

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal(ObservedAt, snapshot.ObservedAtUtc);
            Assert.Equal("world-guid", snapshot.WorldId);
            Assert.Equal("Navezgane", snapshot.Metadata!.WorldName);
            Assert.Equal(-4096, snapshot.Metadata.Extent.MinimumX);
            Assert.Equal(4096, snapshot.Metadata.Extent.MaximumZ);
            // Legacy GPSMap maps every game point as [x, z] and flips only TMS tile-y,
            // so positive Z remains map-up/north while positive X remains east.
            Assert.Equal("east", snapshot.Metadata.Axes.XAxisDirection);
            Assert.Equal("north", snapshot.Metadata.Axes.ZAxisDirection);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, snapshot.Metadata.AvailableZoomLevels);
            Assert.Equal(128, snapshot.Metadata.TileSizePixels);
            Assert.Null(snapshot.Metadata.ResourceVersion);
        }

        [Fact]
        public async Task Runtime_captures_only_inside_dispatch_after_game_ready_and_stop_clears_projection()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            var inner = new RecordingRuntime();
            var insideDispatch = false;
            var captureCount = 0;
            using var runtime = new SevenDaysMapProjectionRuntime(
                projection,
                gameTimeProjection,
                inner,
                (_, action, _) =>
                {
                    insideDispatch = true;
                    try { return Task.FromResult(action()); }
                    finally { insideDispatch = false; }
                },
                () =>
                {
                    Assert.True(insideDispatch);
                    captureCount++;
                    return CreateSample();
                },
                () => ObservedAt,
                TimeSpan.FromHours(1));

            runtime.Start();
            Assert.Equal(0, captureCount);
            runtime.MarkGameReady();
            await runtime.RefreshCompletion;

            Assert.Equal(1, captureCount);
            Assert.Equal(AvailabilityState.Available, projection.Query().Availability);
            runtime.Stop();
            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);
            Assert.Equal(1, inner.StopCount);
        }

        [Fact]
        public async Task Stop_prevents_an_in_flight_refresh_from_republishing_the_projection()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            var dispatchStarted = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatchCompletion = new TaskCompletionSource<SevenDaysMapSample>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var publishWindowEntered = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var allowPublish = new System.Threading.ManualResetEventSlim();
            using var runtime = new SevenDaysMapProjectionRuntime(
                projection,
                gameTimeProjection,
                new RecordingRuntime(),
                (_, _, _) =>
                {
                    dispatchStarted.TrySetResult(null);
                    return dispatchCompletion.Task;
                },
                CreateSample,
                () =>
                {
                    publishWindowEntered.TrySetResult(null);
                    allowPublish.Wait(TestContext.Current.CancellationToken);
                    return ObservedAt;
                },
                TimeSpan.FromHours(1));

            runtime.MarkGameReady();
            await dispatchStarted.Task;
            dispatchCompletion.SetResult(CreateSample());
            await publishWindowEntered.Task;
            runtime.Stop();
            allowPublish.Set();
            await runtime.RefreshCompletion;

            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);
            Assert.Equal(
                AvailabilityState.Unavailable,
                gameTimeProjection.Query().Availability);
        }

        [Fact]
        public async Task A_new_game_ready_lifecycle_can_publish_after_stop()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            var captureCount = 0;
            using var runtime = new SevenDaysMapProjectionRuntime(
                projection,
                gameTimeProjection,
                new RecordingRuntime(),
                (_, action, _) => Task.FromResult(action()),
                () =>
                {
                    captureCount++;
                    return CreateSample();
                },
                () => ObservedAt.AddMinutes(captureCount),
                TimeSpan.FromHours(1));

            runtime.MarkGameReady();
            await runtime.RefreshCompletion;
            runtime.Stop();
            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);

            runtime.MarkGameReady();
            await runtime.RefreshCompletion;

            Assert.Equal(2, captureCount);
            Assert.Equal(AvailabilityState.Available, projection.Query().Availability);
            Assert.Equal(
                AvailabilityState.Available,
                gameTimeProjection.Query().Availability);
        }

        [Fact]
        public async Task Concurrent_ready_and_stop_are_completed_in_one_serial_lifecycle_order()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            var inner = new CoordinatedRuntime();
            using var runtime = new SevenDaysMapProjectionRuntime(
                projection,
                gameTimeProjection,
                inner,
                (_, action, _) => Task.FromResult(action()),
                CreateSample,
                () => ObservedAt,
                TimeSpan.FromHours(1));

            var ready = Task.Run(runtime.MarkGameReady);
            Assert.True(inner.ReadyEntered.Wait(TimeSpan.FromSeconds(5)));
            var stop = Task.Run(runtime.Stop);

            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.False(inner.StopEntered.IsSet);

            inner.AllowReady.Set();
            await Task.WhenAll(ready, stop);

            Assert.Equal(new[] { "ready-enter", "ready-exit", "stop" }, inner.Trace);
            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);
            Assert.Equal(AvailabilityState.Unavailable, gameTimeProjection.Query().Availability);
        }

        [Fact]
        public void Failed_refresh_retains_the_last_sample_as_stale()
        {
            var projection = new SevenDaysMapMetadataProjection();
            projection.Publish(CreateSample(), ObservedAt);

            projection.MarkCaptureFailed();

            var snapshot = projection.Query();
            Assert.Equal(AvailabilityState.Stale, snapshot.Availability);
            Assert.Equal("world-guid", snapshot.WorldId);
            Assert.NotNull(snapshot.Metadata);
        }

        [Fact]
        public void Game_time_capture_failure_keeps_only_game_time_stale_while_metadata_updates()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            projection.Publish(CreateSample(), ObservedAt);
            gameTimeProjection.Publish(CreateSample(), ObservedAt);
            var updatedMetadata = new SevenDaysMapMetadataSample(
                "Navezgane",
                "world-guid",
                -2048,
                -2048,
                2048,
                2048,
                128,
                5);

            var nextSample = new SevenDaysMapSample(
                updatedMetadata,
                null,
                gameTimeCaptureFailed: true);
            projection.Publish(nextSample, LaterObservation);
            gameTimeProjection.Publish(nextSample, LaterObservation);

            var metadata = projection.Query();
            var gameTime = gameTimeProjection.Query();
            Assert.Equal(AvailabilityState.Available, metadata.Availability);
            Assert.Equal(LaterObservation, metadata.ObservedAtUtc);
            Assert.Equal(-2048, metadata.Metadata!.Extent.MinimumX);
            Assert.Equal(AvailabilityState.Stale, gameTime.Availability);
            Assert.Equal(ObservedAt, gameTime.GameTime!.ObservedAtUtc);
            Assert.Equal(4, gameTime.GameTime.Day);
        }

        [Fact]
        public void Missing_world_clears_both_projection_fields_to_unavailable()
        {
            var projection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            projection.Publish(CreateSample(), ObservedAt);
            gameTimeProjection.Publish(CreateSample(), ObservedAt);

            var noWorld = new SevenDaysMapSample(null, null, worldAvailable: false);
            projection.Publish(noWorld, LaterObservation);
            gameTimeProjection.Publish(noWorld, LaterObservation);

            Assert.Equal(AvailabilityState.Unavailable, projection.Query().Availability);
            Assert.Equal(
                AvailabilityState.Unavailable,
                gameTimeProjection.Query().Availability);
        }

        private static SevenDaysMapSample CreateSample() => new SevenDaysMapSample(
            new SevenDaysMapMetadataSample(
                "Navezgane",
                "world-guid",
                -4096,
                -4096,
                4096,
                4096,
                128,
                5),
            new SevenDaysMapGameTimeSample(4, 13, 27));

        private sealed class RecordingRuntime : IModRuntime
        {
            public int StopCount { get; private set; }
            public void Start() { }
            public void MarkGameReady() { }
            public void Stop() { StopCount++; }
        }

        private sealed class CoordinatedRuntime : IModRuntime
        {
            public ConcurrentQueue<string> Trace { get; } = new ConcurrentQueue<string>();
            public ManualResetEventSlim ReadyEntered { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim AllowReady { get; } = new ManualResetEventSlim();
            public ManualResetEventSlim StopEntered { get; } = new ManualResetEventSlim();

            public void Start() { }

            public void MarkGameReady()
            {
                Trace.Enqueue("ready-enter");
                ReadyEntered.Set();
                AllowReady.Wait(TestContext.Current.CancellationToken);
                Trace.Enqueue("ready-exit");
            }

            public void Stop()
            {
                Trace.Enqueue("stop");
                StopEntered.Set();
            }
        }
    }
}
