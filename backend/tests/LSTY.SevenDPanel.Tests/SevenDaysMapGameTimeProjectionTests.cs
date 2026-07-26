using System;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysMapGameTimeProjectionTests
    {
        private static readonly DateTimeOffset ObservedAt =
            new DateTimeOffset(2026, 7, 26, 9, 30, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset LaterObservation = ObservedAt.AddMinutes(1);

        [Fact]
        public void Projection_publishes_game_time_with_capture_observation_time()
        {
            var projection = new SevenDaysMapGameTimeProjection();

            projection.Publish(
                new SevenDaysMapSample(
                    null,
                    new SevenDaysMapGameTimeSample(7, 22, 14)),
                ObservedAt);
            var snapshot = projection.Query();

            Assert.Equal(AvailabilityState.Available, snapshot.Availability);
            Assert.Equal(7, snapshot.GameTime!.Day);
            Assert.Equal(22, snapshot.GameTime.Hour);
            Assert.Equal(14, snapshot.GameTime.Minute);
            Assert.Equal(ObservedAt, snapshot.GameTime.ObservedAtUtc);
        }

        [Fact]
        public void Metadata_failure_does_not_hide_available_game_time()
        {
            var projection = new SevenDaysMapGameTimeProjection();

            projection.Publish(
                new SevenDaysMapSample(
                    null,
                    new SevenDaysMapGameTimeSample(1, 6, 0)),
                ObservedAt);

            Assert.Equal(
                AvailabilityState.Available,
                projection.Query().Availability);
        }

        [Fact]
        public void Failed_refresh_without_previous_sample_is_unavailable()
        {
            var projection = new SevenDaysMapGameTimeProjection();

            projection.MarkCaptureFailed();

            Assert.Equal(
                AvailabilityState.Unavailable,
                projection.Query().Availability);
        }

        [Fact]
        public void Missing_metadata_from_a_ready_world_keeps_only_metadata_stale_while_game_time_updates()
        {
            var metadataProjection = new SevenDaysMapMetadataProjection();
            var gameTimeProjection = new SevenDaysMapGameTimeProjection();
            metadataProjection.Publish(CreateCompleteSample(), ObservedAt);
            gameTimeProjection.Publish(CreateCompleteSample(), ObservedAt);

            var nextSample = new SevenDaysMapSample(
                null,
                new SevenDaysMapGameTimeSample(8, 1, 2));
            metadataProjection.Publish(nextSample, LaterObservation);
            gameTimeProjection.Publish(nextSample, LaterObservation);

            var metadata = metadataProjection.Query();
            var gameTime = gameTimeProjection.Query();
            Assert.Equal(AvailabilityState.Stale, metadata.Availability);
            Assert.Equal(ObservedAt, metadata.ObservedAtUtc);
            Assert.Equal("world-guid", metadata.WorldId);
            Assert.Equal(AvailabilityState.Available, gameTime.Availability);
            Assert.Equal(LaterObservation, gameTime.GameTime!.ObservedAtUtc);
            Assert.Equal(8, gameTime.GameTime.Day);
        }

        private static SevenDaysMapSample CreateCompleteSample() => new SevenDaysMapSample(
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
    }
}
