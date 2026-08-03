using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    [Trait("Capability", "Players")]
    [Trait("Boundary", "Application")]
    public sealed class PlayerMapUseCaseTests
    {
        [Fact]
        public void Game_time_keeps_game_clock_and_observation_time_independent()
        {
            var observedAtUtc = Utc(12);

            var gameTime = new MapGameTime(37, 18, 42, observedAtUtc);

            Assert.Equal(37, gameTime.Day);
            Assert.Equal(18, gameTime.Hour);
            Assert.Equal(42, gameTime.Minute);
            Assert.Equal(observedAtUtc, gameTime.ObservedAtUtc);
        }

        [Fact]
        public void Map_metadata_keeps_projection_inputs_and_requires_axis_convention()
        {
            var extent = new MapExtent(-4096, -4096, 4096, 4096);
            var axes = new MapAxisConvention("east", "north");

            var metadata = new MapMetadata(
                "Navezgane",
                extent,
                axes,
                new[] { 4, 2, 3, 3 },
                256,
                null);

            Assert.Equal("Navezgane", metadata.WorldName);
            Assert.Equal(extent, metadata.Extent);
            Assert.Same(axes, metadata.Axes);
            Assert.Equal(new[] { 2, 3, 4 }, metadata.AvailableZoomLevels);
            Assert.Equal(256, metadata.TileSizePixels);
            Assert.Null(metadata.ResourceVersion);
            Assert.Throws<ArgumentNullException>(() => new MapMetadata(
                "Navezgane",
                extent,
                null!,
                new[] { 2 },
                256,
                "map-v1"));
        }

        [Fact]
        public void Track_query_rejects_invalid_identity_time_kind_order_and_duration()
        {
            Assert.Throws<ArgumentException>(() => new GetPlayerTrackQuery(" ", Utc(1), Utc(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GetPlayerTrackQuery(
                CrossplatformId,
                Utc(1).ToOffset(TimeSpan.FromHours(8)),
                Utc(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GetPlayerTrackQuery(
                CrossplatformId,
                Utc(2),
                Utc(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GetPlayerTrackQuery(
                CrossplatformId,
                Utc(1),
                Utc(1).AddDays(30).AddMilliseconds(1)));

            var query = new GetPlayerTrackQuery(CrossplatformId, Utc(1), Utc(1).AddDays(30));
            Assert.Equal(Utc(1).AddDays(30), query.ToUtc);
        }

        [Fact]
        public void Track_use_case_stably_sorts_by_observation_time_then_snapshot_id_and_keeps_xyz()
        {
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(
                    new[]
                    {
                        Observation(3, Utc(3), 30, 31, 32),
                        Observation(2, Utc(2), 20, 21, 22),
                        Observation(1, Utc(2), 10, 11, 12)
                    },
                    Array.Empty<PlayerHistoryGap>())
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Equal(3, result!.ObservationCount);
            var points = Assert.Single(result.Segments).Points;
            Assert.Equal(new long[] { 1, 2, 3 }, points.Select(point => point.SnapshotId));
            Assert.Equal((10f, 11f, 12f), (points[0].X, points[0].Y, points[0].Z));
            Assert.Equal(new[] { "Alice", "Alice", "Alice" }, points.Select(point => point.Name));
            Assert.Equal(new[] { Utc(2), Utc(2), Utc(3) },
                points.Select(point => point.ObservedAtUtc));
        }

        [Fact]
        public void Duplicate_observation_time_and_snapshot_id_breaks_the_track_segment()
        {
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(
                    new[]
                    {
                        Observation(1, Utc(1), 1, 2, 3),
                        Observation(1, Utc(1), 4, 5, 6),
                        Observation(2, Utc(2), 7, 8, 9)
                    },
                    Array.Empty<PlayerHistoryGap>())
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Collection(result!.Segments,
                segment => Assert.Equal(new long[] { 1 },
                    segment.Points.Select(point => point.SnapshotId)),
                segment => Assert.Equal(new long[] { 1, 2 },
                    segment.Points.Select(point => point.SnapshotId)));
        }

        [Fact]
        public void Gap_completed_at_previous_observation_breaks_the_closed_interval()
        {
            AssertClosedBoundaryBreaks(Utc(0), Utc(1));
        }

        [Fact]
        public void Gap_started_at_next_observation_breaks_the_closed_interval()
        {
            AssertClosedBoundaryBreaks(Utc(2), Utc(3));
        }

        [Fact]
        public void Many_sorted_gaps_preserve_the_single_cursor_segmentation_result()
        {
            var observations = Enumerable.Range(1, 1000)
                .Select(index => Observation(index, Utc(1).AddMilliseconds(index), index, 0, 0))
                .ToArray();
            var gaps = Enumerable.Range(1, GetPlayerTrackQuery.MaximumContinuityGaps - 1)
                .Select(index => new PlayerHistoryGap(
                    "before-" + index,
                    CrossplatformId,
                    Utc(0).AddMilliseconds(index),
                    Utc(0).AddMilliseconds(index),
                    1,
                    PlayerHistoryGapReason.QueueFull,
                    Utc(5)))
                .Concat(new[]
                {
                    new PlayerHistoryGap(
                        "split",
                        CrossplatformId,
                        observations[499].ObservedAtUtc.AddTicks(1),
                        observations[500].ObservedAtUtc.AddTicks(-1),
                        1,
                        PlayerHistoryGapReason.QueueFull,
                        Utc(5))
                });
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(observations, gaps)
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Collection(result!.Segments,
                segment => Assert.Equal(500, segment.Points.Count),
                segment => Assert.Equal(500, segment.Points.Count));
        }

        [Fact]
        public void Track_collection_contracts_reject_null_elements()
        {
            var point = new PlayerTrackPoint(1, "Alice", 1, 2, 3, Utc(1));
            var segment = new PlayerTrackSegment(new[] { point });

            Assert.Throws<ArgumentException>(() => new PlayerTrackHistory(
                new PlayerTrackObservation[] { null! },
                Array.Empty<PlayerHistoryGap>()));
            Assert.Throws<ArgumentException>(() => new PlayerTrackHistory(
                Array.Empty<PlayerTrackObservation>(),
                new PlayerHistoryGap[] { null! }));
            Assert.Throws<ArgumentException>(() => new PlayerTrackSegment(
                new PlayerTrackPoint[] { null! }));
            Assert.Throws<ArgumentException>(() => new GetPlayerTrackResult(
                new PlayerTrackSegment[] { null! }));

            Assert.Single(segment.Points);
        }

        [Fact]
        public void Intersecting_internal_gap_breaks_track_without_exposing_gap_details()
        {
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(
                    new[]
                    {
                        Observation(1, Utc(1), 1, 2, 3),
                        Observation(2, Utc(2), 4, 5, 6),
                        Observation(3, Utc(3), 7, 8, 9)
                    },
                    new[]
                    {
                        new PlayerHistoryGap("gap-1", CrossplatformId, Utc(1).AddMinutes(30), Utc(1).AddMinutes(30), 7,
                            PlayerHistoryGapReason.QueueFull, Utc(4))
                    })
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Collection(result!.Segments,
                segment => Assert.Equal(new long[] { 1 }, segment.Points.Select(point => point.SnapshotId)),
                segment => Assert.Equal(new long[] { 2, 3 }, segment.Points.Select(point => point.SnapshotId)));
            Assert.DoesNotContain(
                typeof(GetPlayerTrackResult).GetProperties(),
                property => property.Name.IndexOf("gap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            property.Name.IndexOf("dropped", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Invalid_coordinate_and_identity_observations_break_segments_and_single_points_are_kept()
        {
            var otherIdentity = "EOS_other";
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(
                    new[]
                    {
                        Observation(1, Utc(1), 1, 2, 3),
                        Observation(2, Utc(2), float.NaN, 5, 6),
                        Observation(3, Utc(3), 7, 8, 9),
                        Observation(4, Utc(4), 10, 11, 12, otherIdentity),
                        Observation(5, Utc(5), 13, 14, 15)
                    },
                    Array.Empty<PlayerHistoryGap>())
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Equal(3, result!.ObservationCount);
            Assert.Collection(result.Segments,
                segment => Assert.Equal(1, Assert.Single(segment.Points).SnapshotId),
                segment => Assert.Equal(3, Assert.Single(segment.Points).SnapshotId),
                segment => Assert.Equal(5, Assert.Single(segment.Points).SnapshotId));
        }

        [Fact]
        public void Track_use_case_rejects_more_than_five_thousand_observations()
        {
            var observations = Enumerable.Range(1, GetPlayerTrackQuery.MaximumObservations + 1)
                .Select(index => Observation(index, Utc(1).AddMilliseconds(index), index, 0, 0));
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(observations, Array.Empty<PlayerHistoryGap>())
            };

            Assert.Throws<PlayerTrackLimitExceededException>(() =>
                new GetPlayerTrackUseCase(store).Execute(Query()));
        }

        private const string CrossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";

        private static GetPlayerTrackQuery Query() =>
            new GetPlayerTrackQuery(CrossplatformId, Utc(0), Utc(6));

        private static void AssertClosedBoundaryBreaks(
            DateTimeOffset gapStartedAtUtc,
            DateTimeOffset gapCompletedAtUtc)
        {
            var store = new RecordingStore
            {
                TrackHistory = new PlayerTrackHistory(
                    new[]
                    {
                        Observation(1, Utc(1), 1, 2, 3),
                        Observation(2, Utc(2), 4, 5, 6)
                    },
                    new[]
                    {
                        new PlayerHistoryGap(
                            "boundary",
                            CrossplatformId,
                            gapStartedAtUtc,
                            gapCompletedAtUtc,
                            1,
                            PlayerHistoryGapReason.QueueFull,
                            Utc(5))
                    })
            };

            var result = new GetPlayerTrackUseCase(store).Execute(Query());

            Assert.NotNull(result);
            Assert.Collection(result!.Segments,
                segment => Assert.Equal(1, Assert.Single(segment.Points).SnapshotId),
                segment => Assert.Equal(2, Assert.Single(segment.Points).SnapshotId));
        }

        private static PlayerTrackObservation Observation(
            long snapshotId,
            DateTimeOffset observedAtUtc,
            float x,
            float y,
            float z,
            string crossplatformId = CrossplatformId) =>
            new PlayerTrackObservation(snapshotId, crossplatformId, "Alice", x, y, z, observedAtUtc);

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 25, hour, 0, 0, TimeSpan.Zero);

        [Trait("Capability", "Players")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingStore : IPlayerHistoryStore
        {
            public PlayerTrackHistory? TrackHistory { get; set; }

            public void Append(PlayerSnapshot snapshot) => throw new NotSupportedException();

            public void AppendGap(PlayerHistoryGap gap) => throw new NotSupportedException();

            public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query) => throw new NotSupportedException();

            public HistoricalPlayerDetails? GetPlayer(string crossplatformId) => throw new NotSupportedException();

            public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query) => throw new NotSupportedException();

            public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query) => TrackHistory;

            public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
                HistoricalPlayerLastLocationsStoreQuery query) =>
                Array.Empty<HistoricalPlayerLastRetainedLocation>();

            public int Compact(DateTimeOffset utcNow, int maximumDeletes) => throw new NotSupportedException();
        }
    }
}
