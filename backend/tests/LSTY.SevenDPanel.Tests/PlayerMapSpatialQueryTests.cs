using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class PlayerMapSpatialQueryTests
    {
        [Fact]
        public void Request_requires_one_valid_shape_a_utc_range_and_bounded_limits()
        {
            var rectangle = new PlayerMapRectangle(-10, -20, 10, 20);
            var circle = new PlayerMapCircle(0, 0, 10);

            Assert.Throws<ArgumentException>(() => new SearchPlayersInAreaRequest(Utc(1), Utc(2), null, null));
            Assert.Throws<ArgumentException>(() => new SearchPlayersInAreaRequest(Utc(1), Utc(2), rectangle, circle));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1).ToOffset(TimeSpan.FromHours(8)), Utc(2), rectangle, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(Utc(2), Utc(1), rectangle, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1), Utc(1).AddDays(30).AddMilliseconds(1), rectangle, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapRectangle(double.NaN, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapRectangle(1, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapRectangle(0, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapRectangle(0, 0, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapCircle(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerMapCircle(double.MaxValue, 0, double.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1), Utc(2), rectangle, null, SearchPlayersInAreaRequest.MaximumCandidateObservationLimit + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1), Utc(2), rectangle, null, playerResultLimit: SearchPlayersInAreaRequest.MaximumPlayerResultLimit + 1));

            var request = new SearchPlayersInAreaRequest(Utc(1), Utc(1).AddDays(30), rectangle, null);
            Assert.Equal(SearchPlayersInAreaRequest.DefaultCandidateObservationLimit, request.CandidateObservationLimit);
            Assert.Equal(SearchPlayersInAreaRequest.DefaultPlayerResultLimit, request.PlayerResultLimit);
        }

        [Fact]
        public void Request_rejects_a_default_circle_that_bypasses_the_value_type_constructor()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1),
                Utc(2),
                null,
                default(PlayerMapCircle)));
        }

        [Fact]
        public void Request_rejects_a_default_rectangle_that_has_no_positive_area()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SearchPlayersInAreaRequest(
                Utc(1),
                Utc(2),
                default(PlayerMapRectangle),
                null));
        }

        [Fact]
        public void Candidate_query_enforces_utc_range_strict_finite_bounds_and_a_bounded_positive_limit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(
                fromUtc: Utc(1).ToOffset(TimeSpan.FromHours(8))));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(fromUtc: Utc(3), toUtc: Utc(2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(toUtc: Utc(1).AddDays(30).AddMilliseconds(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(minimumX: double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(maximumX: -10));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(maximumZ: -10));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(candidateObservationLimit: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CandidateQuery(
                candidateObservationLimit: PlayerAreaCandidateQuery.MaximumCandidateObservationLimit + 1));

            var maximum = CandidateQuery(
                candidateObservationLimit: PlayerAreaCandidateQuery.MaximumCandidateObservationLimit);
            Assert.Equal(PlayerAreaCandidateQuery.MaximumCandidateObservationLimit, maximum.CandidateObservationLimit);
        }

        [Fact]
        public void Sqlite_store_revalidates_candidate_limit_before_executing_the_query()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var query = CandidateQuery();
            var limit = typeof(PlayerAreaCandidateQuery).GetField(
                "<CandidateObservationLimit>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(limit);
            limit!.SetValue(query, -1);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SqlitePlayerHistoryStore(database.ConnectionFactory).GetPlayerAreaCandidates(query));
        }

        [Fact]
        public void Sqlite_store_revalidates_the_complete_candidate_query_before_executing_sql()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var corruptions = new[]
            {
                ("<FromUtc>k__BackingField", (object)Utc(1).ToOffset(TimeSpan.FromHours(8))),
                ("<FromUtc>k__BackingField", (object)Utc(3)),
                ("<ToUtc>k__BackingField", (object)Utc(1).AddDays(31)),
                ("<MinimumX>k__BackingField", (object)double.NaN),
                ("<MaximumZ>k__BackingField", (object)double.PositiveInfinity),
                ("<MaximumX>k__BackingField", (object)(-10d)),
                ("<MaximumZ>k__BackingField", (object)(-11d))
            };

            foreach (var corruption in corruptions)
            {
                var query = CandidateQuery();
                var field = typeof(PlayerAreaCandidateQuery).GetField(
                    corruption.Item1,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field);
                field!.SetValue(query, corruption.Item2);

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    store.GetPlayerAreaCandidates(query));
            }
        }

        [Fact]
        public void Rectangle_query_includes_closed_boundaries_and_excludes_time_and_coordinate_outliers()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot("minimum", "EOS_min", Utc(2), -10, 1, -20));
            store.Append(Snapshot("maximum", "EOS_max", Utc(3), 10, 2, 20));
            store.Append(Snapshot("outside-x", "EOS_x", Utc(3), 10.01f, 2, 0));
            store.Append(Snapshot("outside-z", "EOS_z", Utc(3), 0, 2, 20.01f));
            store.Append(Snapshot("before", "EOS_before", Utc(1), 0, 2, 0));
            store.Append(Snapshot("after", "EOS_after", Utc(4), 0, 2, 0));

            var result = new SearchPlayersInAreaUseCase(store).Execute(new SearchPlayersInAreaRequest(
                Utc(2), Utc(3), new PlayerMapRectangle(-10, -20, 10, 20), null));

            Assert.Equal(new[] { "EOS_max", "EOS_min" }, result.Hits.Select(hit => hit.CrossplatformId));
            Assert.All(result.Hits, hit => Assert.Equal(1, hit.HitObservationCount));
            Assert.False(result.CandidateObservationLimitReached);
        }

        [Fact]
        public void Circle_query_includes_the_circle_boundary_and_rejects_bounding_box_corners()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot("center", "EOS_center", Utc(2), 5, 7, 5));
            store.Append(Snapshot("boundary", "EOS_boundary", Utc(3), 8, 8, 9));
            store.Append(Snapshot("corner", "EOS_corner", Utc(3), 10, 9, 10));

            var result = new SearchPlayersInAreaUseCase(store).Execute(new SearchPlayersInAreaRequest(
                Utc(1), Utc(4), null, new PlayerMapCircle(5, 5, 5)));

            Assert.Equal(new[] { "EOS_boundary", "EOS_center" }, result.Hits.Select(hit => hit.CrossplatformId));
            Assert.DoesNotContain(result.Hits, hit => hit.CrossplatformId == "EOS_corner");
        }

        [Fact]
        public void Circle_query_uses_one_coordinate_tolerance_for_float_boundary_candidates_only()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot("float-boundary", "EOS_boundary", Utc(2), 0.3f, 0, 0.2f));
            store.Append(Snapshot("clear-outlier", "EOS_outlier", Utc(2), 0.3001f, 0, 0.2f));

            var result = new SearchPlayersInAreaUseCase(store).Execute(new SearchPlayersInAreaRequest(
                Utc(1), Utc(3), null, new PlayerMapCircle(0.2, 0.2, 0.1)));

            Assert.Equal("EOS_boundary", Assert.Single(result.Hits).CrossplatformId);
        }

        [Fact]
        public void Hits_group_by_crossplatform_identity_and_stably_choose_last_snapshot_at_equal_time()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot("Alice old", CrossplatformId, Utc(1), 1, 2, 3));
            store.Append(Snapshot("Alice tie old", CrossplatformId, Utc(2), 4, 5, 6));
            store.Append(Snapshot("Alice latest", CrossplatformId, Utc(2), 7, 8, 9));

            var hit = Assert.Single(new SearchPlayersInAreaUseCase(store).Execute(new SearchPlayersInAreaRequest(
                Utc(1), Utc(2), new PlayerMapRectangle(-100, -100, 100, 100), null)).Hits);

            Assert.Equal(CrossplatformId, hit.CrossplatformId);
            Assert.Equal("Alice latest", hit.DisplayName);
            Assert.Equal(Utc(1), hit.FirstHitUtc);
            Assert.Equal(Utc(2), hit.LastHitUtc);
            Assert.Equal(3, hit.HitObservationCount);
            Assert.Equal((7d, 8d, 9d), (hit.LastPosition.X, hit.LastPosition.Y, hit.LastPosition.Z));
        }

        [Fact]
        public void Candidate_and_player_limits_are_stable_and_explicit_in_the_result()
        {
            var store = new RecordingSpatialStore(
                Candidate(5, "EOS_c", "C", Utc(3), 3, 0, 0),
                Candidate(4, "EOS_b", "B", Utc(3), 2, 0, 0),
                Candidate(3, "EOS_a", "A", Utc(2), 1, 0, 0));
            var request = new SearchPlayersInAreaRequest(
                Utc(1), Utc(4), new PlayerMapRectangle(-10, -10, 10, 10), null,
                candidateObservationLimit: 4,
                playerResultLimit: 2);

            var result = new SearchPlayersInAreaUseCase(store).Execute(request);

            Assert.Equal(5, store.LastQuery!.CandidateObservationLimit);
            Assert.Equal((-10d, -10d, 10d, 10d),
                (store.LastQuery.MinimumX, store.LastQuery.MinimumZ, store.LastQuery.MaximumX, store.LastQuery.MaximumZ));
            Assert.Equal(new[] { "EOS_c", "EOS_b" }, result.Hits.Select(hit => hit.CrossplatformId));
            Assert.False(result.CandidateObservationLimitReached);
            Assert.True(result.PlayerResultLimitReached);
            Assert.Equal(3, result.CandidateObservationCount);
            Assert.Equal(3, result.MatchingObservationCount);
        }

        [Fact]
        public void Candidate_limit_is_reached_only_when_the_store_returns_the_extra_probe_row()
        {
            var exactStore = new RecordingSpatialStore(
                Candidate(3, "EOS_c", "C", Utc(3), 3, 0, 0),
                Candidate(2, "EOS_b", "B", Utc(2), 2, 0, 0));
            var overflowStore = new RecordingSpatialStore(
                Candidate(3, "EOS_c", "C", Utc(3), 3, 0, 0),
                Candidate(2, "EOS_b", "B", Utc(2), 2, 0, 0),
                Candidate(1, "EOS_a", "A", Utc(1), 1, 0, 0));
            var request = new SearchPlayersInAreaRequest(
                Utc(1), Utc(4), new PlayerMapRectangle(-10, -10, 10, 10), null,
                candidateObservationLimit: 2,
                playerResultLimit: 10);

            var exact = new SearchPlayersInAreaUseCase(exactStore).Execute(request);
            var overflow = new SearchPlayersInAreaUseCase(overflowStore).Execute(request);

            Assert.Equal(3, exactStore.LastQuery!.CandidateObservationLimit);
            Assert.False(exact.CandidateObservationLimitReached);
            Assert.Equal(2, exact.CandidateObservationCount);
            Assert.True(overflow.CandidateObservationLimitReached);
            Assert.Equal(2, overflow.CandidateObservationCount);
            Assert.Equal(new[] { "EOS_c", "EOS_b" }, overflow.Hits.Select(hit => hit.CrossplatformId));
        }

        [Fact]
        public void Maximum_request_limit_probes_one_extra_candidate_without_overflowing_the_store_contract()
        {
            var store = new RecordingSpatialStore();
            var request = new SearchPlayersInAreaRequest(
                Utc(1), Utc(2), new PlayerMapRectangle(-1, -1, 1, 1), null,
                candidateObservationLimit: SearchPlayersInAreaRequest.MaximumCandidateObservationLimit);

            new SearchPlayersInAreaUseCase(store).Execute(request);

            Assert.Equal(PlayerAreaCandidateQuery.MaximumCandidateObservationLimit,
                store.LastQuery!.CandidateObservationLimit);
        }

        [Fact]
        public void Migration_is_repeatable_records_006_and_exposes_the_spatial_covering_index()
        {
            using var database = new TemporarySpatialDatabase();
            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM SchemaVersions WHERE ScriptName LIKE '%Migrations.006_PlayerMapSpatialQueries.sql';"));
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pragma_index_list('player_history_snapshots') WHERE name = 'ix_player_history_snapshots_spatial_x_z_time';"));
            Assert.Equal(0, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM pragma_index_list('player_history_snapshots') WHERE name = 'ix_player_history_snapshots_spatial_time';"));
            var plan = connection.Query(
                @"EXPLAIN QUERY PLAN
                  SELECT snapshot_id, crossplatform_id, observed_utc, name, position_x, position_y, position_z
                  FROM player_history_snapshots
                  WHERE observed_utc >= 0 AND observed_utc <= 1
                    AND position_x >= 0 AND position_x <= 1
                    AND position_z >= 0 AND position_z <= 1
                  ORDER BY observed_utc DESC, snapshot_id DESC
                  LIMIT 10;")
                .Select(row => (string)row.detail)
                .ToArray();
            Assert.Contains(plan, detail =>
                detail.IndexOf("ix_player_history_snapshots_spatial_x_z_time", StringComparison.Ordinal) >= 0 &&
                detail.IndexOf("position_x", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private const string CrossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 25, hour, 0, 0, TimeSpan.Zero);

        private static PlayerAreaObservationCandidate Candidate(
            long snapshotId,
            string crossplatformId,
            string displayName,
            DateTimeOffset observedAtUtc,
            double x,
            double y,
            double z) =>
            new PlayerAreaObservationCandidate(snapshotId, crossplatformId, displayName, observedAtUtc, x, y, z);

        private static PlayerAreaCandidateQuery CandidateQuery(
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            double minimumX = -10,
            double minimumZ = -10,
            double maximumX = 10,
            double maximumZ = 10,
            int candidateObservationLimit = 10) =>
            new PlayerAreaCandidateQuery(
                fromUtc ?? Utc(1),
                toUtc ?? Utc(2),
                minimumX,
                minimumZ,
                maximumX,
                maximumZ,
                candidateObservationLimit);

        private static PlayerSnapshot Snapshot(
            string name,
            string crossplatformId,
            DateTimeOffset observedAtUtc,
            float x,
            float y,
            float z) =>
            new PlayerSnapshot(
                7,
                name,
                new PlayerPlatformIdentity("Steam_00000000000000000", "Steam"),
                new PlayerPlatformIdentity(crossplatformId, "EOS"),
                PlayerDeviceType.Windows,
                "127.0.0.1",
                0,
                "3.0",
                null,
                0,
                new PlayerPosition(x, y, z),
                false,
                100,
                100,
                level: 1,
                score: 0,
                zombieKills: 0,
                playerKills: 0,
                deaths: 0,
                totalTimePlayedMinutes: 0,
                distanceWalkedMeters: 0,
                totalItemsCrafted: 0,
                longestLifeMinutes: 0,
                currentLifeMinutes: 0,
                observedAtUtc: observedAtUtc);

        private sealed class RecordingSpatialStore : IPlayerMapSpatialQueryStore
        {
            private readonly IReadOnlyList<PlayerAreaObservationCandidate> candidates;

            public RecordingSpatialStore(params PlayerAreaObservationCandidate[] candidates)
            {
                this.candidates = candidates;
            }

            public PlayerAreaCandidateQuery? LastQuery { get; private set; }

            public IReadOnlyList<PlayerAreaObservationCandidate> GetPlayerAreaCandidates(PlayerAreaCandidateQuery query)
            {
                LastQuery = query;
                return candidates.Take(query.CandidateObservationLimit).ToArray();
            }
        }

        private sealed class TemporarySpatialDatabase : IDisposable
        {
            private readonly string directory;

            public TemporarySpatialDatabase()
            {
                directory = Path.Combine(Path.GetTempPath(), "7dpanel-player-map-spatial-tests", Guid.NewGuid().ToString("N"));
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Upgrade() => new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
