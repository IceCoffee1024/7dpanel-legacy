using System;
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
    public sealed class SqlitePlayerHistoryStoreTests
    {
        [Fact]
        public void Upgrade_creates_the_required_history_schema_and_can_be_repeated()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            var tables = connection.Query<string>(
                @"SELECT name
                  FROM sqlite_master
                  WHERE type = 'table'
                    AND name IN ('player_history_players', 'player_history_snapshots', 'player_history_gaps')
                  ORDER BY name;").AsList();
            var snapshotColumns = connection.Query<string>(
                "SELECT name FROM pragma_table_info('player_history_snapshots') ORDER BY cid;").AsList();
            var snapshotIndexes = connection.Query<string>(
                "SELECT name FROM pragma_index_list('player_history_snapshots') ORDER BY name;").AsList();
            var foreignKeyTables = connection.Query<string>(
                "SELECT [table] FROM pragma_foreign_key_list('player_history_snapshots');").AsList();

            Assert.Equal(
                new[] { "player_history_gaps", "player_history_players", "player_history_snapshots" },
                tables);
            Assert.Contains("crossplatform_name", snapshotColumns);
            Assert.Contains("ix_player_history_snapshots_player_id", snapshotIndexes);
            Assert.Contains("ix_player_history_snapshots_player_time", snapshotIndexes);
            Assert.Contains("player_history_players", foreignKeyTables);
        }

        [Fact]
        public void Upgrade_rejects_summary_with_no_retained_snapshot()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();

            using var connection = database.ConnectionFactory.Open();
            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO player_history_players (
                      crossplatform_id, latest_name, first_observed_utc, last_observed_utc,
                      latest_snapshot_id, total_observation_count, retained_snapshot_count,
                      compacted_snapshot_count)
                  VALUES ('EOS_empty', 'No Snapshot', 1, 1, 1, 0, 0, 0);"));
        }

        [Fact]
        public void Migration_rejects_negative_duration_and_distance_values()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot());

            using var connection = database.ConnectionFactory.Open();
            foreach (var column in new[]
            {
                "total_time_played_minutes",
                "distance_walked_meters",
                "longest_life_minutes",
                "current_life_minutes"
            })
            {
                Assert.Throws<SqliteException>(() => connection.Execute(
                    "UPDATE player_history_snapshots SET " + column + " = -1;"));
            }
        }

        [Fact]
        public void Compact_uses_the_five_to_fifteen_minute_epoch_bucket_and_protects_only_the_latest_winner()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var now = new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero);

            store.Append(Snapshot(observedAtUtc: now.AddHours(-3)));
            store.Append(Snapshot(observedAtUtc: now.AddMinutes(-6)));
            store.Append(Snapshot(observedAtUtc: now.AddMinutes(-6).AddSeconds(30)));
            store.Append(Snapshot(observedAtUtc: now.AddMinutes(-6).AddSeconds(45)));

            Assert.Equal(2, store.Compact(now, 5_000));

            var snapshots = store.GetSnapshots(new PlayerHistorySnapshotsQuery(
                "EOS_0002d12af0fe4add9c7de0fbc238d431", 10, null));
            Assert.Equal(2, snapshots.Snapshots.Count);
            Assert.Equal(now.AddMinutes(-6).AddSeconds(45), snapshots.Snapshots[0].Player.ObservedAtUtc);
            Assert.Equal(now.AddHours(-3), snapshots.Snapshots[1].Player.ObservedAtUtc);
        }

        [Fact]
        public void Compact_never_deletes_more_than_one_thousand_snapshots_per_batch()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var now = new DateTimeOffset(2026, 7, 25, 11, 0, 0, TimeSpan.Zero);

            store.Append(Snapshot(observedAtUtc: now.AddHours(-3)));
            for (var index = 0; index < 1_002; index++)
                store.Append(Snapshot(observedAtUtc: now.AddMinutes(-6).AddSeconds(index % 60)));
            store.Append(Snapshot(observedAtUtc: now.AddMinutes(-6).AddSeconds(59)));

            Assert.Equal(1_000, store.Compact(now, 50_000));
        }

        [Fact]
        public void Append_rejects_a_blank_crossplatform_combined_id()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var identity = new PlayerPlatformIdentity("EOS_valid", "EOS");
            var combinedId = typeof(PlayerPlatformIdentity).GetField(
                "<CombinedId>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(combinedId);
            combinedId!.SetValue(identity, " ");
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);

            Assert.Throws<ArgumentException>(() => store.Append(
                Snapshot(crossplatformIdentity: identity)));
        }

        [Fact]
        public void GetPlayers_matches_percent_underscore_and_backslash_as_literal_search_characters()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            const string name = "Name%_\\Literal";
            const string crossplatformId = "EOS_%_\\Literal";

            store.Append(Snapshot(name: name, crossplatformId: crossplatformId));
            store.Append(Snapshot(name: "NameXYLiteral", crossplatformId: "EOS_XYLiteral"));

            var byName = store.GetPlayers(new HistoricalPlayersQuery(name, 10, null));
            var byCrossplatformId = store.GetPlayers(new HistoricalPlayersQuery(crossplatformId, 10, null));

            Assert.Collection(byName.Players,
                player => Assert.Equal(crossplatformId, player.CrossplatformId));
            Assert.Collection(byCrossplatformId.Players,
                player => Assert.Equal(crossplatformId, player.CrossplatformId));
        }

        [Fact]
        public void GetPlayerTrack_reads_closed_range_in_stable_order_with_xyz_and_intersecting_gaps()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var fromUtc = Utc(2);
            var toUtc = Utc(4);

            store.Append(Snapshot(observedAtUtc: Utc(1), position: new PlayerPosition(1, 2, 3)));
            store.Append(Snapshot(name: "From", observedAtUtc: fromUtc, position: new PlayerPosition(10, 11, 12)));
            store.Append(Snapshot(name: "Middle A", observedAtUtc: Utc(3), position: new PlayerPosition(20, 21, 22)));
            store.Append(Snapshot(name: "Middle B", observedAtUtc: Utc(3), position: new PlayerPosition(30, 31, 32)));
            store.Append(Snapshot(name: "To", observedAtUtc: toUtc, position: new PlayerPosition(40, 41, 42)));
            store.Append(Snapshot(observedAtUtc: Utc(5), position: new PlayerPosition(50, 51, 52)));
            store.AppendGap(Gap("before", Utc(1), Utc(1).AddMinutes(30)));
            store.AppendGap(Gap("touch-from", Utc(1), fromUtc));
            store.AppendGap(Gap("inside", Utc(3), Utc(3).AddMinutes(1)));
            store.AppendGap(Gap("touch-to", toUtc, Utc(5)));
            store.AppendGap(Gap("after", Utc(5), Utc(6)));

            var result = store.GetPlayerTrack(new GetPlayerTrackQuery(CrossplatformId, fromUtc, toUtc));

            Assert.NotNull(result);
            Assert.Equal(new long[] { 2, 3, 4, 5 },
                result!.Observations.Select(observation => observation.SnapshotId));
            Assert.Equal(
                new[] { (10f, 11f, 12f), (20f, 21f, 22f), (30f, 31f, 32f), (40f, 41f, 42f) },
                result.Observations.Select(observation => (observation.X, observation.Y, observation.Z)));
            Assert.Equal(new[] { "From", "Middle A", "Middle B", "To" },
                result.Observations.Select(observation => observation.Name));
            Assert.Equal(new[] { fromUtc, Utc(3), Utc(3), toUtc },
                result.Observations.Select(observation => observation.ObservedAtUtc));
            Assert.Equal(new[] { "touch-from", "inside", "touch-to" },
                result.Gaps.Select(gap => gap.GapId));
        }

        [Fact]
        public void GetPlayerTrack_distinguishes_known_player_with_empty_range_from_unknown_player()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot(observedAtUtc: Utc(1)));

            var known = store.GetPlayerTrack(new GetPlayerTrackQuery(CrossplatformId, Utc(5), Utc(6)));
            var unknown = store.GetPlayerTrack(new GetPlayerTrackQuery("EOS_unknown", Utc(5), Utc(6)));

            Assert.NotNull(known);
            Assert.Empty(known!.Observations);
            Assert.Empty(known.Gaps);
            Assert.Null(unknown);
        }

        [Fact]
        public void GetPlayerTrack_rejects_the_five_thousand_and_first_observation()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var fromUtc = Utc(1);
            store.Append(Snapshot(observedAtUtc: fromUtc));
            InsertSnapshotCopies(database.ConnectionFactory, GetPlayerTrackQuery.MaximumObservations, fromUtc);

            Assert.Throws<PlayerTrackLimitExceededException>(() => store.GetPlayerTrack(
                new GetPlayerTrackQuery(CrossplatformId, fromUtc, fromUtc.AddDays(1))));
        }

        [Fact]
        public void GetPlayerTrack_allows_exactly_five_thousand_observations()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var fromUtc = Utc(1);
            store.Append(Snapshot(observedAtUtc: fromUtc));
            InsertSnapshotCopies(
                database.ConnectionFactory,
                GetPlayerTrackQuery.MaximumObservations - 1,
                fromUtc);

            var result = store.GetPlayerTrack(
                new GetPlayerTrackQuery(CrossplatformId, fromUtc, fromUtc.AddDays(1)));

            Assert.NotNull(result);
            Assert.Equal(GetPlayerTrackQuery.MaximumObservations, result!.Observations.Count);
        }

        [Fact]
        public void GetPlayerTrack_allows_five_thousand_gaps_and_rejects_the_next_one()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            var fromUtc = Utc(1);
            store.Append(Snapshot(observedAtUtc: fromUtc));
            InsertGaps(
                database.ConnectionFactory,
                GetPlayerTrackQuery.MaximumContinuityGaps,
                fromUtc);

            var allowed = store.GetPlayerTrack(
                new GetPlayerTrackQuery(CrossplatformId, fromUtc, fromUtc.AddDays(1)));

            Assert.NotNull(allowed);
            Assert.Equal(GetPlayerTrackQuery.MaximumContinuityGaps, allowed!.Gaps.Count);

            store.AppendGap(new PlayerHistoryGap(
                "overflow-gap",
                CrossplatformId,
                fromUtc,
                fromUtc,
                1,
                PlayerHistoryGapReason.QueueFull,
                fromUtc));

            Assert.Throws<PlayerTrackLimitExceededException>(() => store.GetPlayerTrack(
                new GetPlayerTrackQuery(CrossplatformId, fromUtc, fromUtc.AddDays(1))));
        }

        [Fact]
        public void GetHistoricalPlayerLastRetainedLocations_uses_each_players_latest_retained_snapshot_before_extent_filtering()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);

            store.Append(Snapshot(
                name: "Old inside",
                observedAtUtc: Utc(1),
                position: new PlayerPosition(5, 6, 7),
                crossplatformId: "EOS_moved"));
            store.Append(Snapshot(
                name: "Latest outside",
                observedAtUtc: Utc(2),
                position: new PlayerPosition(500, 6, 500),
                crossplatformId: "EOS_moved"));
            store.Append(Snapshot(
                name: "Latest inside",
                observedAtUtc: Utc(3),
                position: new PlayerPosition(8, 9, 10),
                crossplatformId: "EOS_inside"));

            var result = store.GetHistoricalPlayerLastRetainedLocations(
                new HistoricalPlayerLastLocationsStoreQuery(
                    new MapExtent(0, 0, 100, 100),
                    candidateLimit: 10));

            var location = Assert.Single(result);
            Assert.Equal("EOS_inside", location.CrossplatformId);
            Assert.Equal("Latest inside", location.DisplayName);
            Assert.Equal(Utc(3), location.ObservedAtUtc);
            Assert.Equal((8d, 9d, 10d),
                (location.Position.X, location.Position.Y, location.Position.Z));
        }

        [Fact]
        public void GetHistoricalPlayerLastRetainedLocations_has_closed_extent_stable_order_and_candidate_limit()
        {
            using var database = new TemporaryHistoryDatabase();
            database.Upgrade();
            var store = new SqlitePlayerHistoryStore(database.ConnectionFactory);
            store.Append(Snapshot(
                name: "Minimum",
                observedAtUtc: Utc(1),
                position: new PlayerPosition(-10, 1, -20),
                crossplatformId: "EOS_min"));
            store.Append(Snapshot(
                name: "Maximum",
                observedAtUtc: Utc(1),
                position: new PlayerPosition(30, 2, 40),
                crossplatformId: "EOS_max"));

            var result = store.GetHistoricalPlayerLastRetainedLocations(
                new HistoricalPlayerLastLocationsStoreQuery(
                    new MapExtent(-10, -20, 30, 40),
                    candidateLimit: 1));

            var location = Assert.Single(result);
            Assert.Equal("EOS_max", location.CrossplatformId);
        }

        private const string CrossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431";

        private static DateTimeOffset Utc(int hour) =>
            new DateTimeOffset(2026, 7, 25, hour, 0, 0, TimeSpan.Zero);

        private static PlayerHistoryGap Gap(string gapId, DateTimeOffset startedAtUtc, DateTimeOffset completedAtUtc) =>
            new PlayerHistoryGap(
                gapId,
                CrossplatformId,
                startedAtUtc,
                completedAtUtc,
                1,
                PlayerHistoryGapReason.QueueFull,
                Utc(7));

        private static void InsertSnapshotCopies(
            SqliteConnectionFactory connectionFactory,
            int count,
            DateTimeOffset fromUtc)
        {
            using var connection = connectionFactory.Open();
            var columns = connection.Query<string>(
                "SELECT name FROM pragma_table_info('player_history_snapshots') WHERE name <> 'snapshot_id' ORDER BY cid;")
                .ToArray();
            var projection = columns
                .Select(column => column == "observed_utc" ? "@FromUtc + sequence.value" : "source." + column)
                .ToArray();
            connection.Execute(
                @"WITH RECURSIVE sequence(value) AS (
                      SELECT 1
                      UNION ALL
                      SELECT value + 1 FROM sequence WHERE value < @Count
                  )
                  INSERT INTO player_history_snapshots (" + string.Join(", ", columns) + @")
                  SELECT " + string.Join(", ", projection) + @"
                  FROM sequence
                  CROSS JOIN player_history_snapshots source
                  WHERE source.snapshot_id = 1;",
                new { Count = count, FromUtc = fromUtc.ToUnixTimeMilliseconds() });
        }

        private static void InsertGaps(
            SqliteConnectionFactory connectionFactory,
            int count,
            DateTimeOffset fromUtc)
        {
            using var connection = connectionFactory.Open();
            connection.Execute(
                @"WITH RECURSIVE sequence(value) AS (
                      SELECT 1
                      UNION ALL
                      SELECT value + 1 FROM sequence WHERE value < @Count
                  )
                  INSERT INTO player_history_gaps (
                      gap_id, crossplatform_id, started_utc, completed_utc,
                      dropped_count, reason, recorded_utc)
                  SELECT 'gap-' || value, @CrossplatformId, @FromUtc, @FromUtc,
                         1, 'queue_full', @FromUtc
                  FROM sequence;",
                new
                {
                    Count = count,
                    CrossplatformId,
                    FromUtc = fromUtc.ToUnixTimeMilliseconds()
                });
        }

        private static PlayerSnapshot Snapshot(
            string name = "Player",
            int entityId = 7,
            DateTimeOffset? observedAtUtc = null,
            string? crossplatformId = "EOS_0002d12af0fe4add9c7de0fbc238d431",
            string? playGroup = "Survivors",
            DateTimeOffset? lastLoginUtc = null,
            int? gameStage = 0,
            int? expToNextLevel = 0,
            int? skillPoints = 0,
            PlayerPosition? bedroll = null,
            PlayerPosition? position = null,
            PlayerPlatformIdentity? crossplatformIdentity = null)
        {
            return new PlayerSnapshot(
                entityId,
                name,
                new PlayerPlatformIdentity("Steam_00000000000000000", "Steam"),
                crossplatformIdentity ?? (crossplatformId == null ? null : new PlayerPlatformIdentity(crossplatformId, "EOS")),
                PlayerDeviceType.Windows,
                "127.0.0.1",
                0,
                "3.0",
                "discord-user",
                0,
                position ?? new PlayerPosition(0, 0, 0),
                false,
                0,
                0,
                0,
                playGroup,
                lastLoginUtc ?? new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero),
                gameStage,
                expToNextLevel,
                skillPoints,
                bedroll ?? new PlayerPosition(0, 0, 0),
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                observedAtUtc ?? new DateTimeOffset(2026, 7, 25, 1, 0, 0, TimeSpan.Zero));
        }

        private sealed class TemporaryHistoryDatabase : IDisposable
        {
            private readonly string directory;

            public TemporaryHistoryDatabase()
            {
                directory = Path.Combine(
                    Path.GetTempPath(),
                    "7dpanel-player-history-tests",
                    Guid.NewGuid().ToString("N"));
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
