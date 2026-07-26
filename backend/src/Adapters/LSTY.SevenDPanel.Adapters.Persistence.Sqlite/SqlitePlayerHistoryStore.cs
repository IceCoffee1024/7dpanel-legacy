using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqlitePlayerHistoryStore : IPlayerHistoryStore, IPlayerMapSpatialQueryStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqlitePlayerHistoryStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Append(PlayerSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var identity = snapshot.CrossplatformIdentity
                ?? throw new ArgumentException("A historical snapshot requires a cross-platform identity.", nameof(snapshot));
            if (string.IsNullOrWhiteSpace(identity.CombinedId))
                throw new ArgumentException("A historical snapshot requires a cross-platform combined identity.", nameof(snapshot));
            if (snapshot.GameStage < 0 || snapshot.ExpToNextLevel < 0 || snapshot.SkillPoints < 0)
                throw new ArgumentOutOfRangeException(nameof(snapshot));

            var observedUtc = ToUnixMilliseconds(snapshot.ObservedAtUtc);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO player_history_snapshots (
                      crossplatform_id, observed_utc, entity_id, name,
                      platform_combined_id, platform_name, crossplatform_combined_id, crossplatform_name,
                      device_type, ip, ping, compatibility_version, discord_user_id, permission_level,
                      position_x, position_y, position_z, is_dead, health, max_health, level,
                      play_group, last_login_utc, game_stage, exp_to_next_level, skill_points,
                      bedroll_x, bedroll_y, bedroll_z, score, zombie_kills, player_kills, deaths,
                      total_time_played_minutes, distance_walked_meters, total_items_crafted,
                      longest_life_minutes, current_life_minutes)
                  VALUES (
                      @CrossplatformId, @ObservedUtc, @EntityId, @Name,
                      @PlatformCombinedId, @PlatformName, @CrossplatformCombinedId, @CrossplatformName,
                      @DeviceType, @Ip, @Ping, @CompatibilityVersion, @DiscordUserId, @PermissionLevel,
                      @PositionX, @PositionY, @PositionZ, @IsDead, @Health, @MaxHealth, @Level,
                      @PlayGroup, @LastLoginUtc, @GameStage, @ExpToNextLevel, @SkillPoints,
                      @BedrollX, @BedrollY, @BedrollZ, @Score, @ZombieKills, @PlayerKills, @Deaths,
                      @TotalTimePlayedMinutes, @DistanceWalkedMeters, @TotalItemsCrafted,
                      @LongestLifeMinutes, @CurrentLifeMinutes);",
                SnapshotParameters(snapshot, identity.CombinedId, observedUtc), transaction);
            var snapshotId = connection.ExecuteScalar<long>("SELECT last_insert_rowid();", transaction: transaction);
            connection.Execute(
                @"INSERT INTO player_history_players (
                      crossplatform_id, latest_name, first_observed_utc, last_observed_utc,
                      latest_snapshot_id, total_observation_count, retained_snapshot_count,
                      compacted_snapshot_count)
                  VALUES (@CrossplatformId, @Name, @ObservedUtc, @ObservedUtc, @SnapshotId, 1, 1, 0)
                  ON CONFLICT(crossplatform_id) DO UPDATE SET
                      latest_name = excluded.latest_name,
                      last_observed_utc = excluded.last_observed_utc,
                      latest_snapshot_id = excluded.latest_snapshot_id,
                      total_observation_count = player_history_players.total_observation_count + 1,
                      retained_snapshot_count = player_history_players.retained_snapshot_count + 1;",
                new { CrossplatformId = identity.CombinedId, snapshot.Name, ObservedUtc = observedUtc, SnapshotId = snapshotId },
                transaction);
            transaction.Commit();
        }

        public void AppendGap(PlayerHistoryGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));

            using var connection = connectionFactory.Open();
            connection.Execute(
                @"INSERT OR IGNORE INTO player_history_gaps (
                      gap_id, crossplatform_id, started_utc, completed_utc, dropped_count, reason, recorded_utc)
                  VALUES (
                      @GapId, @CrossplatformId, @StartedUtc, @CompletedUtc, @DroppedCount, @Reason, @RecordedUtc);",
                new
                {
                    gap.GapId,
                    gap.CrossplatformId,
                    StartedUtc = ToUnixMilliseconds(gap.StartedAtUtc),
                    CompletedUtc = ToUnixMilliseconds(gap.CompletedAtUtc),
                    gap.DroppedCount,
                    Reason = ToStorageReason(gap.Reason),
                    RecordedUtc = ToUnixMilliseconds(gap.RecordedAtUtc)
                });
        }

        public HistoricalPlayersPage GetPlayers(HistoricalPlayersQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            var where = new List<string>();
            if (query.Query != null)
            {
                where.Add(@"(latest_name LIKE @Search ESCAPE '\' OR crossplatform_id LIKE @Search ESCAPE '\')");
                parameters.Add("Search", "%" + EscapeLike(query.Query) + "%");
            }
            if (query.Cursor != null)
            {
                where.Add("(first_observed_utc < @CursorFirstObservedUtc OR (first_observed_utc = @CursorFirstObservedUtc AND crossplatform_id > @CursorCrossplatformId))");
                parameters.Add("CursorFirstObservedUtc", ToUnixMilliseconds(query.Cursor.FirstObservedAtUtc));
                parameters.Add("CursorCrossplatformId", query.Cursor.CrossplatformId);
            }

            var rows = connection.Query<SummaryRow>(
                @"SELECT p.crossplatform_id AS CrossplatformId, p.latest_name AS LatestName,
                         p.first_observed_utc AS FirstObservedUtc, p.last_observed_utc AS LastObservedUtc,
                         p.total_observation_count AS TotalObservationCount,
                         p.retained_snapshot_count AS RetainedSnapshotCount,
                         p.compacted_snapshot_count AS CompactedSnapshotCount,
                         EXISTS(SELECT 1 FROM player_history_gaps g WHERE g.crossplatform_id = p.crossplatform_id) AS HasGaps
                  FROM player_history_players p" +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY p.first_observed_utc DESC, p.crossplatform_id ASC LIMIT @Take;",
                parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new HistoricalPlayersCursor(FromUnixMilliseconds(pageRows[pageRows.Length - 1].FirstObservedUtc), pageRows[pageRows.Length - 1].CrossplatformId)
                : null;
            return new HistoricalPlayersPage(pageRows.Select(ToSummary), next);
        }

        public HistoricalPlayerDetails? GetPlayer(string crossplatformId)
        {
            if (string.IsNullOrWhiteSpace(crossplatformId)) throw new ArgumentException("A cross-platform identity is required.", nameof(crossplatformId));
            using var connection = connectionFactory.Open();
            var row = connection.QuerySingleOrDefault<DetailRow>(
                @"SELECT p.crossplatform_id AS CrossplatformId, p.latest_name AS LatestName,
                         p.first_observed_utc AS FirstObservedUtc, p.last_observed_utc AS LastObservedUtc,
                         p.total_observation_count AS TotalObservationCount, p.retained_snapshot_count AS RetainedSnapshotCount,
                         p.compacted_snapshot_count AS CompactedSnapshotCount,
                         (SELECT COUNT(*) FROM player_history_gaps g WHERE g.crossplatform_id = p.crossplatform_id) AS GapCount,
                         COALESCE((SELECT SUM(g.dropped_count) FROM player_history_gaps g WHERE g.crossplatform_id = p.crossplatform_id), 0) AS DroppedObservationCount
                  FROM player_history_players p WHERE p.crossplatform_id = @CrossplatformId;",
                new { CrossplatformId = crossplatformId });
            return row == null ? null : new HistoricalPlayerDetails(
                ToSummary(row),
                new PlayerHistoryGapSummary(row.GapCount, row.DroppedObservationCount));
        }

        public PlayerHistorySnapshotsPage GetSnapshots(PlayerHistorySnapshotsQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            var rows = connection.Query<SnapshotRow>(
                @"SELECT snapshot_id AS SnapshotId, crossplatform_id AS CrossplatformId, observed_utc AS ObservedUtc,
                         entity_id AS EntityId, name AS Name, platform_combined_id AS PlatformCombinedId,
                         platform_name AS PlatformName, crossplatform_combined_id AS CrossplatformCombinedId,
                         crossplatform_name AS CrossplatformName, device_type AS DeviceType, ip AS Ip,
                         ping AS Ping, compatibility_version AS CompatibilityVersion, discord_user_id AS DiscordUserId,
                         permission_level AS PermissionLevel, position_x AS PositionX, position_y AS PositionY,
                         position_z AS PositionZ, is_dead AS IsDead, health AS Health, max_health AS MaxHealth,
                         level AS Level, play_group AS PlayGroup, last_login_utc AS LastLoginUtc,
                         game_stage AS GameStage, exp_to_next_level AS ExpToNextLevel, skill_points AS SkillPoints,
                         bedroll_x AS BedrollX, bedroll_y AS BedrollY, bedroll_z AS BedrollZ, score AS Score,
                         zombie_kills AS ZombieKills, player_kills AS PlayerKills, deaths AS Deaths,
                         total_time_played_minutes AS TotalTimePlayedMinutes, distance_walked_meters AS DistanceWalkedMeters,
                         total_items_crafted AS TotalItemsCrafted, longest_life_minutes AS LongestLifeMinutes,
                         current_life_minutes AS CurrentLifeMinutes
                  FROM player_history_snapshots
                  WHERE crossplatform_id = @CrossplatformId AND (@BeforeSnapshotId IS NULL OR snapshot_id < @BeforeSnapshotId)
                  ORDER BY snapshot_id DESC LIMIT @Take;",
                new { query.CrossplatformId, query.BeforeSnapshotId, Take = query.PageSize + 1 }).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var snapshots = pageRows.Select(ToSnapshot).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? pageRows[pageRows.Length - 1].SnapshotId
                : (long?)null;
            if (pageRows.Length == 0) return new PlayerHistorySnapshotsPage(snapshots, next, Array.Empty<PlayerHistoryGap>());

            var oldest = pageRows.Min(row => row.ObservedUtc);
            var newest = pageRows.Max(row => row.ObservedUtc);
            var gaps = connection.Query<GapRow>(
                @"SELECT gap_id AS GapId, crossplatform_id AS CrossplatformId, started_utc AS StartedUtc,
                         completed_utc AS CompletedUtc, dropped_count AS DroppedCount, reason AS Reason,
                         recorded_utc AS RecordedUtc
                  FROM player_history_gaps
                  WHERE crossplatform_id = @CrossplatformId
                    AND completed_utc >= @OldestUtc AND started_utc <= @NewestUtc
                  ORDER BY started_utc ASC, gap_id ASC;",
                new { query.CrossplatformId, OldestUtc = oldest, NewestUtc = newest })
                .Select(ToGap).ToArray();
            return new PlayerHistorySnapshotsPage(snapshots, next, gaps);
        }

        public PlayerTrackHistory? GetPlayerTrack(GetPlayerTrackQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            var exists = connection.ExecuteScalar<long>(
                @"SELECT EXISTS(
                      SELECT 1
                      FROM player_history_players
                      WHERE crossplatform_id = @CrossplatformId);",
                new { query.CrossplatformId });
            if (exists == 0) return null;

            var fromUtc = ToUnixMilliseconds(query.FromUtc);
            var toUtc = ToUnixMilliseconds(query.ToUtc);
            var rows = connection.Query<PlayerTrackRow>(
                @"SELECT snapshot_id AS SnapshotId, crossplatform_id AS CrossplatformId,
                         observed_utc AS ObservedUtc, name AS Name,
                         position_x AS PositionX, position_y AS PositionY, position_z AS PositionZ
                  FROM player_history_snapshots
                  WHERE crossplatform_id = @CrossplatformId
                    AND observed_utc >= @FromUtc
                    AND observed_utc <= @ToUtc
                  ORDER BY observed_utc ASC, snapshot_id ASC
                  LIMIT @Take;",
                new
                {
                    query.CrossplatformId,
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    Take = GetPlayerTrackQuery.MaximumObservations + 1
                }).ToArray();
            if (rows.Length > GetPlayerTrackQuery.MaximumObservations)
                throw new PlayerTrackLimitExceededException();

            var gaps = connection.Query<GapRow>(
                @"SELECT gap_id AS GapId, crossplatform_id AS CrossplatformId,
                         started_utc AS StartedUtc, completed_utc AS CompletedUtc,
                         dropped_count AS DroppedCount, reason AS Reason, recorded_utc AS RecordedUtc
                  FROM player_history_gaps
                  WHERE crossplatform_id = @CrossplatformId
                    AND completed_utc >= @FromUtc
                    AND started_utc <= @ToUtc
                  ORDER BY started_utc ASC, completed_utc ASC, gap_id ASC
                  LIMIT @Take;",
                new
                {
                    query.CrossplatformId,
                    FromUtc = fromUtc,
                    ToUtc = toUtc,
                    Take = GetPlayerTrackQuery.MaximumContinuityGaps + 1
                }).ToArray();
            if (gaps.Length > GetPlayerTrackQuery.MaximumContinuityGaps)
                throw new PlayerTrackLimitExceededException();

            return new PlayerTrackHistory(
                rows.Select(row => new PlayerTrackObservation(
                    row.SnapshotId,
                    row.CrossplatformId,
                    row.Name,
                    row.PositionX,
                    row.PositionY,
                    row.PositionZ,
                    FromUnixMilliseconds(row.ObservedUtc))),
                gaps.Select(ToGap));
        }

        public IReadOnlyList<HistoricalPlayerLastRetainedLocation> GetHistoricalPlayerLastRetainedLocations(
            HistoricalPlayerLastLocationsStoreQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            var rows = connection.Query<HistoricalPlayerLastRetainedLocationRow>(
                @"WITH ranked_snapshots AS (
                      SELECT snapshot_id AS SnapshotId,
                             crossplatform_id AS CrossplatformId,
                             name AS DisplayName,
                             position_x AS PositionX,
                             position_y AS PositionY,
                             position_z AS PositionZ,
                             observed_utc AS ObservedUtc,
                             ROW_NUMBER() OVER (
                                 PARTITION BY crossplatform_id
                                 ORDER BY observed_utc DESC, snapshot_id DESC) AS RetainedRank
                      FROM player_history_snapshots
                  )
                  SELECT SnapshotId, CrossplatformId, DisplayName,
                         PositionX, PositionY, PositionZ, ObservedUtc
                  FROM ranked_snapshots
                  WHERE RetainedRank = 1
                    AND PositionX >= @MinimumX
                    AND PositionX <= @MaximumX
                    AND PositionZ >= @MinimumZ
                    AND PositionZ <= @MaximumZ
                  ORDER BY ObservedUtc DESC, SnapshotId DESC, CrossplatformId ASC
                  LIMIT @Take;",
                new
                {
                    query.Extent.MinimumX,
                    query.Extent.MaximumX,
                    query.Extent.MinimumZ,
                    query.Extent.MaximumZ,
                    Take = query.CandidateLimit
                }).ToArray();

            return rows.Select(row => new HistoricalPlayerLastRetainedLocation(
                row.SnapshotId,
                row.CrossplatformId,
                row.DisplayName,
                new MapLayerPosition(row.PositionX, row.PositionY, row.PositionZ),
                FromUnixMilliseconds(row.ObservedUtc)))
                .ToArray();
        }

        public IReadOnlyList<PlayerAreaObservationCandidate> GetPlayerAreaCandidates(PlayerAreaCandidateQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            query.Validate();
            using var connection = connectionFactory.Open();
            return connection.Query<PlayerAreaCandidateRow>(
                @"SELECT snapshot_id AS SnapshotId, crossplatform_id AS CrossplatformId,
                         observed_utc AS ObservedUtc, name AS DisplayName,
                         position_x AS PositionX, position_y AS PositionY, position_z AS PositionZ
                  FROM player_history_snapshots
                  WHERE observed_utc >= @FromUtc
                    AND observed_utc <= @ToUtc
                    AND position_x >= @MinimumX
                    AND position_x <= @MaximumX
                    AND position_z >= @MinimumZ
                    AND position_z <= @MaximumZ
                  ORDER BY observed_utc DESC, snapshot_id DESC
                  LIMIT @Take;",
                new
                {
                    FromUtc = ToUnixMilliseconds(query.FromUtc),
                    ToUtc = ToUnixMilliseconds(query.ToUtc),
                    query.MinimumX,
                    query.MaximumX,
                    query.MinimumZ,
                    query.MaximumZ,
                    Take = query.CandidateObservationLimit
                })
                .Select(row => new PlayerAreaObservationCandidate(
                    row.SnapshotId,
                    row.CrossplatformId,
                    row.DisplayName,
                    FromUnixMilliseconds(row.ObservedUtc),
                    row.PositionX,
                    row.PositionY,
                    row.PositionZ))
                .ToArray();
        }

        public int Compact(DateTimeOffset utcNow, int maximumDeletes)
        {
            if (maximumDeletes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDeletes));
            var now = ToUnixMilliseconds(utcNow);
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var rows = connection.Query<RetentionRow>(
                @"SELECT s.snapshot_id AS SnapshotId, s.crossplatform_id AS CrossplatformId, s.observed_utc AS ObservedUtc,
                         p.latest_snapshot_id AS LatestSnapshotId
                  FROM player_history_snapshots s
                  INNER JOIN player_history_players p ON p.crossplatform_id = s.crossplatform_id
                  ORDER BY s.crossplatform_id ASC, s.snapshot_id ASC;", transaction: transaction).ToArray();
            var protectedIds = new HashSet<long>(rows.GroupBy(row => row.CrossplatformId).Select(group => group.First().SnapshotId));
            foreach (var row in rows) protectedIds.Add(row.LatestSnapshotId);
            var winners = new HashSet<string>();
            var victims = new List<RetentionRow>();
            foreach (var row in rows.OrderByDescending(value => value.SnapshotId))
            {
                if (!TryGetBucket(now - row.ObservedUtc, row.ObservedUtc, out var tier, out var bucket)) continue;
                var key = row.CrossplatformId + "\u001f" + tier.ToString(CultureInfo.InvariantCulture) + "\u001f" + bucket.ToString(CultureInfo.InvariantCulture);
                if (!winners.Add(key) && !protectedIds.Contains(row.SnapshotId)) victims.Add(row);
            }
            var selected = victims.Take(maximumDeletes > 1000 ? 1000 : maximumDeletes).ToArray();
            if (selected.Length == 0) { transaction.Commit(); return 0; }
            connection.Execute("DELETE FROM player_history_snapshots WHERE snapshot_id IN @Ids;", new { Ids = selected.Select(row => row.SnapshotId).ToArray() }, transaction);
            foreach (var group in selected.GroupBy(row => row.CrossplatformId))
            {
                connection.Execute(
                    @"UPDATE player_history_players
                      SET retained_snapshot_count = retained_snapshot_count - @Count,
                          compacted_snapshot_count = compacted_snapshot_count + @Count
                      WHERE crossplatform_id = @CrossplatformId;",
                    new { CrossplatformId = group.Key, Count = group.Count() }, transaction);
            }
            transaction.Commit();
            return selected.Length;
        }

        private static object SnapshotParameters(PlayerSnapshot player, string crossplatformId, long observedUtc) => new
        {
            CrossplatformId = crossplatformId,
            ObservedUtc = observedUtc,
            player.EntityId,
            player.Name,
            PlatformCombinedId = player.PlatformIdentity.CombinedId,
            PlatformName = player.PlatformIdentity.Platform,
            CrossplatformCombinedId = player.CrossplatformIdentity!.CombinedId,
            CrossplatformName = player.CrossplatformIdentity.Platform,
            DeviceType = player.DeviceType.ToString(),
            player.Ip,
            player.Ping,
            CompatibilityVersion = player.CompatibilityVersion,
            DiscordUserId = player.DiscordUserId,
            player.PermissionLevel,
            PositionX = player.Position.X,
            PositionY = player.Position.Y,
            PositionZ = player.Position.Z,
            IsDead = player.IsDead ? 1 : 0,
            player.Health,
            player.MaxHealth,
            player.Level,
            PlayGroup = player.PlayGroup,
            LastLoginUtc = player.LastLoginUtc.HasValue ? ToUnixMilliseconds(player.LastLoginUtc.Value) : (long?)null,
            player.GameStage,
            player.ExpToNextLevel,
            player.SkillPoints,
            BedrollX = player.Bedroll?.X,
            BedrollY = player.Bedroll?.Y,
            BedrollZ = player.Bedroll?.Z,
            player.Score,
            player.ZombieKills,
            player.PlayerKills,
            player.Deaths,
            player.TotalTimePlayedMinutes,
            player.DistanceWalkedMeters,
            player.TotalItemsCrafted,
            player.LongestLifeMinutes,
            player.CurrentLifeMinutes
        };

        private static HistoricalPlayerSummary ToSummary(SummaryRow row) => new HistoricalPlayerSummary(
            row.CrossplatformId, row.LatestName, FromUnixMilliseconds(row.FirstObservedUtc), FromUnixMilliseconds(row.LastObservedUtc),
            row.TotalObservationCount, row.RetainedSnapshotCount, row.CompactedSnapshotCount, row.HasGaps);

        private static HistoricalPlayerSnapshot ToSnapshot(SnapshotRow row) => new HistoricalPlayerSnapshot(row.SnapshotId,
            new PlayerSnapshot(row.EntityId, row.Name, new PlayerPlatformIdentity(row.PlatformCombinedId, row.PlatformName),
                new PlayerPlatformIdentity(row.CrossplatformCombinedId, row.CrossplatformName),
                (PlayerDeviceType)Enum.Parse(typeof(PlayerDeviceType), row.DeviceType, true), row.Ip, row.Ping,
                row.CompatibilityVersion, row.DiscordUserId, row.PermissionLevel,
                new PlayerPosition(row.PositionX, row.PositionY, row.PositionZ), row.IsDead != 0, row.Health, row.MaxHealth, row.Level,
                row.PlayGroup, row.LastLoginUtc.HasValue ? FromUnixMilliseconds(row.LastLoginUtc.Value) : (DateTimeOffset?)null,
                row.GameStage, row.ExpToNextLevel, row.SkillPoints,
                row.BedrollX.HasValue ? new PlayerPosition(row.BedrollX.Value, row.BedrollY!.Value, row.BedrollZ!.Value) : (PlayerPosition?)null,
                row.Score, row.ZombieKills, row.PlayerKills, row.Deaths, row.TotalTimePlayedMinutes, row.DistanceWalkedMeters,
                row.TotalItemsCrafted, row.LongestLifeMinutes, row.CurrentLifeMinutes, FromUnixMilliseconds(row.ObservedUtc)));

        private static PlayerHistoryGap ToGap(GapRow row) => new PlayerHistoryGap(row.GapId, row.CrossplatformId,
            FromUnixMilliseconds(row.StartedUtc), FromUnixMilliseconds(row.CompletedUtc), row.DroppedCount,
            FromStorageReason(row.Reason), FromUnixMilliseconds(row.RecordedUtc));

        private static long ToUnixMilliseconds(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();
        private static DateTimeOffset FromUnixMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);
        private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        private static string ToStorageReason(PlayerHistoryGapReason reason) => reason == PlayerHistoryGapReason.QueueFull ? "queue_full" : reason == PlayerHistoryGapReason.StoreFailure ? "store_failure" : "shutdown_timeout";
        private static PlayerHistoryGapReason FromStorageReason(string reason) => reason == "queue_full" ? PlayerHistoryGapReason.QueueFull : reason == "store_failure" ? PlayerHistoryGapReason.StoreFailure : PlayerHistoryGapReason.ShutdownTimeout;

        private static bool TryGetBucket(long ageMilliseconds, long observedUtc, out int tier, out long bucket)
        {
            const long minute = 60L * 1000;
            const long hour = 60L * minute;
            const long day = 24L * hour;
            var widths = new[]
            {
                (15L * minute, minute),
                (30L * minute, 5L * minute),
                (hour, 10L * minute),
                (6L * hour, 30L * minute),
                (12L * hour, hour),
                (day, 2L * hour),
                (3L * day, 6L * hour),
                (7L * day, 12L * hour),
                (30L * day, day),
                (long.MaxValue, 7L * day)
            };
            if (ageMilliseconds < 5L * minute) { tier = 0; bucket = 0; return false; }
            for (var index = 0; index < widths.Length; index++)
            {
                if (ageMilliseconds < widths[index].Item1)
                {
                    tier = index + 1;
                    bucket = FloorDivide(observedUtc, widths[index].Item2);
                    return true;
                }
            }
            tier = 0; bucket = 0; return false;
        }

        private static long FloorDivide(long value, long divisor)
        {
            var quotient = value / divisor;
            return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
        }

        private class SummaryRow { public string CrossplatformId { get; set; } = null!; public string LatestName { get; set; } = null!; public long FirstObservedUtc { get; set; } public long LastObservedUtc { get; set; } public long TotalObservationCount { get; set; } public long RetainedSnapshotCount { get; set; } public long CompactedSnapshotCount { get; set; } public bool HasGaps { get; set; } }
        private sealed class DetailRow : SummaryRow { public long GapCount { get; set; } public long DroppedObservationCount { get; set; } }
        private sealed class GapRow { public string GapId { get; set; } = null!; public string CrossplatformId { get; set; } = null!; public long StartedUtc { get; set; } public long CompletedUtc { get; set; } public long DroppedCount { get; set; } public string Reason { get; set; } = null!; public long RecordedUtc { get; set; } }
        private sealed class RetentionRow { public long SnapshotId { get; set; } public string CrossplatformId { get; set; } = null!; public long ObservedUtc { get; set; } public long LatestSnapshotId { get; set; } }
        private sealed class PlayerTrackRow { public long SnapshotId { get; set; } public string CrossplatformId { get; set; } = null!; public long ObservedUtc { get; set; } public string Name { get; set; } = null!; public float PositionX { get; set; } public float PositionY { get; set; } public float PositionZ { get; set; } }
        private sealed class HistoricalPlayerLastRetainedLocationRow { public long SnapshotId { get; set; } public string CrossplatformId { get; set; } = null!; public string DisplayName { get; set; } = null!; public double PositionX { get; set; } public double PositionY { get; set; } public double PositionZ { get; set; } public long ObservedUtc { get; set; } }
        private sealed class PlayerAreaCandidateRow { public long SnapshotId { get; set; } public string CrossplatformId { get; set; } = null!; public long ObservedUtc { get; set; } public string DisplayName { get; set; } = null!; public double PositionX { get; set; } public double PositionY { get; set; } public double PositionZ { get; set; } }
        private sealed class SnapshotRow { public long SnapshotId { get; set; } public string CrossplatformId { get; set; } = null!; public long ObservedUtc { get; set; } public int EntityId { get; set; } public string Name { get; set; } = null!; public string PlatformCombinedId { get; set; } = null!; public string PlatformName { get; set; } = null!; public string CrossplatformCombinedId { get; set; } = null!; public string CrossplatformName { get; set; } = null!; public string DeviceType { get; set; } = null!; public string? Ip { get; set; } public int Ping { get; set; } public string? CompatibilityVersion { get; set; } public string? DiscordUserId { get; set; } public int PermissionLevel { get; set; } public float PositionX { get; set; } public float PositionY { get; set; } public float PositionZ { get; set; } public long IsDead { get; set; } public int Health { get; set; } public int MaxHealth { get; set; } public int Level { get; set; } public string? PlayGroup { get; set; } public long? LastLoginUtc { get; set; } public int? GameStage { get; set; } public int? ExpToNextLevel { get; set; } public int? SkillPoints { get; set; } public float? BedrollX { get; set; } public float? BedrollY { get; set; } public float? BedrollZ { get; set; } public int Score { get; set; } public int ZombieKills { get; set; } public int PlayerKills { get; set; } public int Deaths { get; set; } public float TotalTimePlayedMinutes { get; set; } public float DistanceWalkedMeters { get; set; } public uint TotalItemsCrafted { get; set; } public float LongestLifeMinutes { get; set; } public float CurrentLifeMinutes { get; set; } }
    }
}
