using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class SevenDaysTransientEntityProjectionTests
    {
        [Fact]
        public void Capture_publishes_immutable_animal_and_hostile_snapshots_with_one_observation_time()
        {
            var now = Utc(0);
            var animals = new List<SevenDaysTransientEntitySampleItem>
            {
                new SevenDaysTransientEntitySampleItem(1, "animalStag", 10, 11, 12)
            };
            var hostiles = new List<SevenDaysTransientEntitySampleItem>
            {
                new SevenDaysTransientEntitySampleItem(2, "zombieArlene", 20, 21, 22)
            };
            var sample = new SevenDaysTransientEntitySample(animals, hostiles);
            var projection = Projection(() => now);

            animals.Clear();
            hostiles[0] = new SevenDaysTransientEntitySampleItem(3, "replacement", 30, 31, 32);
            projection.Capture(sample, now);

            var animal = Assert.Single(Query(projection, SevenDaysTransientEntityKind.Animal).Entities);
            var hostile = Assert.Single(Query(projection, SevenDaysTransientEntityKind.Hostile).Entities);
            Assert.Equal(1, animal.EntityId);
            Assert.Equal("animalStag", animal.EntityType);
            Assert.Equal(10, animal.Position.X);
            Assert.Equal(11, animal.Position.Y);
            Assert.Equal(12, animal.Position.Z);
            Assert.Equal(2, hostile.EntityId);
            Assert.Equal(now, Query(projection, SevenDaysTransientEntityKind.Animal).ObservedAtUtc);
            Assert.Equal(now, Query(projection, SevenDaysTransientEntityKind.Hostile).ObservedAtUtc);
        }

        [Fact]
        public void Query_is_extent_bounded_and_requires_the_entity_zoom_threshold()
        {
            var now = Utc(0);
            var projection = Projection(() => now);
            projection.Capture(new SevenDaysTransientEntitySample(
                new[]
                {
                    new SevenDaysTransientEntitySampleItem(1, "minimum", -10, 0, -20),
                    new SevenDaysTransientEntitySampleItem(2, "maximum", 30, 0, 40),
                    new SevenDaysTransientEntitySampleItem(3, "outside", 31, 0, 41)
                },
                Array.Empty<SevenDaysTransientEntitySampleItem>()), now);

            var zoomedOut = Query(
                projection,
                SevenDaysTransientEntityKind.Animal,
                new MapExtent(-10, -20, 30, 40),
                zoom: SevenDaysTransientEntityQuery.MinimumZoom - 1);
            var bounded = Query(
                projection,
                SevenDaysTransientEntityKind.Animal,
                new MapExtent(-10, -20, 30, 40));

            Assert.Equal(AvailabilityState.Available, zoomedOut.Availability);
            Assert.False(zoomedOut.IsZoomSufficient);
            Assert.Empty(zoomedOut.Entities);
            Assert.True(bounded.IsZoomSufficient);
            Assert.Collection(
                bounded.Entities,
                entity => Assert.Equal(1, entity.EntityId),
                entity => Assert.Equal(2, entity.EntityId));
        }

        [Fact]
        public void Query_throws_an_explicit_error_instead_of_truncating_over_limit_results()
        {
            var now = Utc(0);
            var projection = Projection(() => now);
            projection.Capture(new SevenDaysTransientEntitySample(
                Array.Empty<SevenDaysTransientEntitySampleItem>(),
                new[]
                {
                    new SevenDaysTransientEntitySampleItem(1, "zombieArlene", 1, 2, 3),
                    new SevenDaysTransientEntitySampleItem(2, "zombieBoe", 4, 5, 6)
                }), now);

            var error = Assert.Throws<SevenDaysTransientEntityLimitExceededException>(() =>
                Query(projection, SevenDaysTransientEntityKind.Hostile, limit: 1));

            Assert.Equal(1, error.Limit);
            Assert.Equal(2, error.MatchedCount);
            Assert.Throws<SevenDaysTransientEntityLimitExceededException>(() =>
                new SevenDaysTransientEntityQuery(
                    SevenDaysTransientEntityKind.Hostile,
                    new MapExtent(-100, -100, 100, 100),
                    SevenDaysTransientEntityQuery.MinimumZoom,
                    SevenDaysTransientEntityQuery.MaximumResultLimit + 1));
        }

        [Fact]
        public void Snapshot_becomes_stale_then_expires_and_stop_clears_published_state()
        {
            var now = Utc(0);
            var projection = Projection(() => now);
            projection.Capture(new SevenDaysTransientEntitySample(
                new[] { new SevenDaysTransientEntitySampleItem(1, "animalStag", 1, 2, 3) },
                Array.Empty<SevenDaysTransientEntitySampleItem>()), now);

            now = now.AddSeconds(11);
            Assert.Equal(
                AvailabilityState.Stale,
                Query(projection, SevenDaysTransientEntityKind.Animal).Availability);

            now = now.AddSeconds(20);
            var expired = Query(projection, SevenDaysTransientEntityKind.Animal);
            Assert.Equal(AvailabilityState.Unavailable, expired.Availability);
            Assert.Null(expired.ObservedAtUtc);
            Assert.Empty(expired.Entities);

            projection.Capture(SevenDaysTransientEntitySample.Empty, now);
            projection.Stop();
            Assert.Equal(
                AvailabilityState.Unavailable,
                Query(projection, SevenDaysTransientEntityKind.Animal).Availability);
        }

        [Fact]
        public void Capture_rejects_non_finite_coordinates_and_non_utc_observation_times()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SevenDaysTransientEntitySampleItem(1, "animalStag", float.NaN, 0, 0));

            var projection = Projection(() => Utc(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => projection.Capture(
                SevenDaysTransientEntitySample.Empty,
                new DateTimeOffset(2026, 7, 26, 1, 0, 0, TimeSpan.FromHours(8))));
        }

        private static SevenDaysTransientEntityProjection Projection(Func<DateTimeOffset> utcNow) =>
            new SevenDaysTransientEntityProjection(
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                utcNow);

        private static SevenDaysTransientEntitySnapshot Query(
            SevenDaysTransientEntityProjection projection,
            SevenDaysTransientEntityKind kind,
            MapExtent? extent = null,
            int zoom = SevenDaysTransientEntityQuery.MinimumZoom,
            int limit = 10) =>
            projection.Query(new SevenDaysTransientEntityQuery(
                kind,
                extent ?? new MapExtent(-100, -100, 100, 100),
                zoom,
                limit));

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 1, minute, 0, TimeSpan.Zero);
    }
}
