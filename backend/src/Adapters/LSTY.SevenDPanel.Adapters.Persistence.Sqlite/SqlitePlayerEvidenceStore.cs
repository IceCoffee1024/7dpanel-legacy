using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqlitePlayerEvidenceStore : IPlayerEvidenceStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqlitePlayerEvidenceStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ??
                throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void AppendSession(PlayerSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO player_sessions (
                      id, crossplatform_id, server_id, world_id, started_at_utc,
                      ended_at_utc, end_reason, last_x, last_y, last_z, completeness)
                  VALUES (
                      @Id, @CrossplatformId, @ServerId, @WorldId, @StartedAtUtc,
                      @EndedAtUtc, @EndReason, @LastX, @LastY, @LastZ, @Completeness);",
                new
                {
                    Id = session.SessionId,
                    session.CrossplatformId,
                    session.ServerId,
                    session.WorldId,
                    StartedAtUtc = ToUnixMilliseconds(session.StartedAtUtc),
                    EndedAtUtc = ToNullableUnixMilliseconds(session.EndedAtUtc),
                    session.EndReason,
                    LastX = session.LastPosition?.X,
                    LastY = session.LastPosition?.Y,
                    LastZ = session.LastPosition?.Z,
                    Completeness = session.Completeness.ToString()
                },
                transaction);
            transaction.Commit();
        }

        public void AppendActivity(PlayerActivityEvent activity)
        {
            if (activity == null) throw new ArgumentNullException(nameof(activity));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO player_activity_events (
                      id, crossplatform_id, server_id, world_id, kind,
                      observed_at_utc, correlation_id, completeness)
                  VALUES (
                      @Id, @CrossplatformId, @ServerId, @WorldId, @Kind,
                      @ObservedAtUtc, @CorrelationId, @Completeness);",
                new
                {
                    Id = activity.ActivityId,
                    activity.CrossplatformId,
                    activity.ServerId,
                    activity.WorldId,
                    activity.Kind,
                    ObservedAtUtc = ToUnixMilliseconds(activity.ObservedAtUtc),
                    activity.CorrelationId,
                    Completeness = activity.Completeness.ToString()
                },
                transaction);
            transaction.Commit();
        }

        public void AppendInventorySnapshot(PlayerInventorySnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO player_inventory_snapshots (
                      id, crossplatform_id, server_id, world_id, observed_at_utc,
                      game_version, catalog_version, catalog_resolution, fingerprint,
                      admin_boundary)
                  VALUES (
                      @Id, @CrossplatformId, @ServerId, @WorldId, @ObservedAtUtc,
                      @GameVersion, @CatalogVersion, @CatalogResolution, @Fingerprint,
                      @AdminBoundary);",
                new
                {
                    Id = snapshot.SnapshotId,
                    snapshot.CrossplatformId,
                    snapshot.ServerId,
                    snapshot.WorldId,
                    ObservedAtUtc = ToUnixMilliseconds(snapshot.ObservedAtUtc),
                    snapshot.GameVersion,
                    snapshot.CatalogVersion,
                    CatalogResolution = snapshot.CatalogResolution.ToString(),
                    snapshot.Fingerprint,
                    AdminBoundary = snapshot.AdminBoundary ? 1 : 0
                },
                transaction);
            foreach (var item in snapshot.Items)
            {
                connection.Execute(
                    @"INSERT INTO player_inventory_items (
                          snapshot_id, container_kind, slot_index, internal_name,
                          item_kind, count, quality, use_amount)
                      VALUES (
                          @SnapshotId, @ContainerKind, @SlotIndex, @InternalName,
                          'Unknown', @Count, @Quality, @UseAmount);",
                    new
                    {
                        SnapshotId = snapshot.SnapshotId,
                        ContainerKind = item.Container,
                        SlotIndex = item.Slot,
                        item.InternalName,
                        item.Count,
                        item.Quality,
                        UseAmount = item.UseAmount?.ToString(CultureInfo.InvariantCulture)
                    },
                    transaction);
                for (var ordinal = 0; ordinal < item.ModInternalNames.Count; ordinal++)
                {
                    connection.Execute(
                        @"INSERT INTO player_inventory_item_mods (
                              snapshot_id, container_kind, slot_index, ordinal, internal_name)
                          VALUES (
                              @SnapshotId, @ContainerKind, @SlotIndex, @Ordinal, @InternalName);",
                        new
                        {
                            SnapshotId = snapshot.SnapshotId,
                            ContainerKind = item.Container,
                            SlotIndex = item.Slot,
                            Ordinal = ordinal,
                            InternalName = item.ModInternalNames[ordinal]
                        },
                        transaction);
                }
            }
            transaction.Commit();
        }

        public void AppendSkillSnapshot(PlayerSkillSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            connection.Execute(
                @"INSERT INTO player_skill_snapshots (
                      id, crossplatform_id, server_id, world_id, observed_at_utc,
                      game_version, level, skill_points)
                  VALUES (
                      @Id, @CrossplatformId, @ServerId, @WorldId, @ObservedAtUtc,
                      @GameVersion, @Level, @SkillPoints);",
                new
                {
                    Id = snapshot.SnapshotId,
                    snapshot.CrossplatformId,
                    snapshot.ServerId,
                    snapshot.WorldId,
                    ObservedAtUtc = ToUnixMilliseconds(snapshot.ObservedAtUtc),
                    snapshot.GameVersion,
                    snapshot.Level,
                    snapshot.SkillPoints
                },
                transaction);
            foreach (var value in snapshot.Values)
            {
                connection.Execute(
                    @"INSERT INTO player_skill_values (
                          snapshot_id, skill_key, state, value, minimum, maximum,
                          next_level_cost, parent_key)
                      VALUES (
                          @SnapshotId, @SkillKey, @State, @Value, @Minimum, @Maximum,
                          @NextLevelCost, @ParentKey);",
                    new
                    {
                        SnapshotId = snapshot.SnapshotId,
                        value.SkillKey,
                        State = value.State.ToString(),
                        value.Value,
                        value.Minimum,
                        value.Maximum,
                        value.NextLevelCost,
                        value.ParentKey
                    },
                    transaction);
            }
            transaction.Commit();
        }

        public void AppendInventoryGap(PlayerEvidenceGap gap) => AppendGap("inventory_gaps", gap);

        public void AppendSkillGap(PlayerEvidenceGap gap) => AppendGap("skill_gaps", gap);

        public IReadOnlyList<PlayerSession> GetSessions(PlayerEvidenceRangeQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            return connection.Query<SessionRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         server_id AS ServerId, world_id AS WorldId,
                         started_at_utc AS StartedAtUtc, ended_at_utc AS EndedAtUtc,
                         end_reason AS EndReason, last_x AS LastX, last_y AS LastY,
                         last_z AS LastZ, completeness AS Completeness
                  FROM player_sessions
                  WHERE crossplatform_id = @CrossplatformId
                    AND started_at_utc >= @FromUtc AND started_at_utc <= @ToUtc
                  ORDER BY started_at_utc DESC, id DESC
                  LIMIT @Take;",
                RangeParameters(query))
                .Select(ToSession)
                .ToArray();
        }

        public IReadOnlyList<PlayerActivityEvent> GetActivity(PlayerEvidenceRangeQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            return connection.Query<ActivityRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         server_id AS ServerId, world_id AS WorldId, kind AS Kind,
                         observed_at_utc AS ObservedAtUtc, correlation_id AS CorrelationId,
                         completeness AS Completeness
                  FROM player_activity_events
                  WHERE crossplatform_id = @CrossplatformId
                    AND observed_at_utc >= @FromUtc AND observed_at_utc <= @ToUtc
                  ORDER BY observed_at_utc DESC, id DESC
                  LIMIT @Take;",
                RangeParameters(query))
                .Select(ToActivity)
                .ToArray();
        }

        public PlayerInventorySnapshotsPage GetInventorySnapshots(PlayerInventorySnapshotsQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var rows = connection.Query<InventorySnapshotRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         server_id AS ServerId, world_id AS WorldId,
                         observed_at_utc AS ObservedAtUtc, game_version AS GameVersion,
                         catalog_version AS CatalogVersion,
                         catalog_resolution AS CatalogResolution,
                         fingerprint AS Fingerprint, admin_boundary AS AdminBoundary
                  FROM player_inventory_snapshots
                  WHERE crossplatform_id = @CrossplatformId
                    AND (@CursorUtc IS NULL OR observed_at_utc < @CursorUtc
                        OR (observed_at_utc = @CursorUtc AND id < @CursorId))
                  ORDER BY observed_at_utc DESC, id DESC
                  LIMIT @Take;",
                new
                {
                    query.CrossplatformId,
                    CursorUtc = query.Cursor == null
                        ? (long?)null
                        : ToUnixMilliseconds(query.Cursor.ObservedAtUtc),
                    CursorId = query.Cursor?.Id,
                    Take = query.PageSize + 1
                },
                transaction).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var snapshots = ReadInventoryAggregates(connection, transaction, pageRows);
            var gaps = ReadPageGaps(connection, transaction, "inventory_gaps", query.CrossplatformId, pageRows);
            transaction.Commit();
            return new PlayerInventorySnapshotsPage(
                snapshots,
                NextCursor(rows.Length, query.PageSize, pageRows),
                gaps);
        }

        public PlayerSkillSnapshotsPage GetSkillSnapshots(PlayerSkillSnapshotsQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction();
            var rows = connection.Query<SkillSnapshotRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         server_id AS ServerId, world_id AS WorldId,
                         observed_at_utc AS ObservedAtUtc, game_version AS GameVersion,
                         level AS Level, skill_points AS SkillPoints
                  FROM player_skill_snapshots
                  WHERE crossplatform_id = @CrossplatformId
                    AND (@CursorUtc IS NULL OR observed_at_utc < @CursorUtc
                        OR (observed_at_utc = @CursorUtc AND id < @CursorId))
                  ORDER BY observed_at_utc DESC, id DESC
                  LIMIT @Take;",
                new
                {
                    query.CrossplatformId,
                    CursorUtc = query.Cursor == null
                        ? (long?)null
                        : ToUnixMilliseconds(query.Cursor.ObservedAtUtc),
                    CursorId = query.Cursor?.Id,
                    Take = query.PageSize + 1
                },
                transaction).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var snapshots = ReadSkillAggregates(connection, transaction, pageRows);
            var gaps = ReadPageGaps(connection, transaction, "skill_gaps", query.CrossplatformId, pageRows);
            transaction.Commit();
            return new PlayerSkillSnapshotsPage(
                snapshots,
                NextCursor(rows.Length, query.PageSize, pageRows),
                gaps);
        }

        public IReadOnlyList<PlayerEvidenceGap> GetInventoryGaps(PlayerEvidenceRangeQuery query) =>
            GetGaps("inventory_gaps", query);

        public IReadOnlyList<PlayerEvidenceGap> GetSkillGaps(PlayerEvidenceRangeQuery query) =>
            GetGaps("skill_gaps", query);

        public void Compact(PlayerEvidenceCompactionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            CompactInventory(connection, transaction, request);
            CompactSkills(connection, transaction, request);
            transaction.Commit();
        }

        private void AppendGap(string table, PlayerEvidenceGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));
            using var connection = connectionFactory.Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var existing = connection.QuerySingleOrDefault<GapRow>(
                "SELECT id AS Id, crossplatform_id AS CrossplatformId, " +
                "started_at_utc AS StartedAtUtc, ended_at_utc AS EndedAtUtc, " +
                "reason AS Reason, estimated_lost_count AS EstimatedLostCount " +
                "FROM " + table + " WHERE id = @Id;",
                new { Id = gap.GapId },
                transaction);
            if (existing != null)
            {
                if (GapEquals(existing, gap))
                {
                    transaction.Commit();
                    return;
                }
                throw new InvalidOperationException("A player evidence gap ID already has different content.");
            }

            var startedAtUtc = ToUnixMilliseconds(gap.StartedAtUtc);
            var endedAtUtc = ToUnixMilliseconds(gap.EndedAtUtc);
            var overlaps = connection.Query<GapRow>(
                "SELECT id AS Id, crossplatform_id AS CrossplatformId, " +
                "started_at_utc AS StartedAtUtc, ended_at_utc AS EndedAtUtc, " +
                "reason AS Reason, estimated_lost_count AS EstimatedLostCount " +
                "FROM " + table + " WHERE crossplatform_id = @CrossplatformId " +
                "AND reason = @Reason AND ended_at_utc >= @StartedAtUtc " +
                "AND started_at_utc <= @EndedAtUtc ORDER BY id ASC;",
                new
                {
                    gap.CrossplatformId,
                    gap.Reason,
                    StartedAtUtc = startedAtUtc,
                    EndedAtUtc = endedAtUtc
                },
                transaction).ToArray();
            var canonicalId = overlaps.Length == 0
                ? gap.GapId
                : Math.Min(gap.GapId, overlaps.Min(row => row.Id));
            var mergedStart = overlaps.Length == 0
                ? startedAtUtc
                : Math.Min(startedAtUtc, overlaps.Min(row => row.StartedAtUtc));
            var mergedEnd = overlaps.Length == 0
                ? endedAtUtc
                : Math.Max(endedAtUtc, overlaps.Max(row => row.EndedAtUtc));
            var mergedCount = gap.EstimatedLostCount + overlaps.Sum(row => row.EstimatedLostCount);
            if (overlaps.Length != 0)
                connection.Execute(
                    "DELETE FROM " + table + " WHERE id IN @Ids;",
                    new { Ids = overlaps.Select(row => row.Id).ToArray() },
                    transaction);
            connection.Execute(
                "INSERT INTO " + table + " (id, crossplatform_id, started_at_utc, " +
                "ended_at_utc, reason, estimated_lost_count) VALUES " +
                "(@Id, @CrossplatformId, @StartedAtUtc, @EndedAtUtc, @Reason, @EstimatedLostCount);",
                new
                {
                    Id = canonicalId,
                    gap.CrossplatformId,
                    StartedAtUtc = mergedStart,
                    EndedAtUtc = mergedEnd,
                    gap.Reason,
                    EstimatedLostCount = mergedCount
                },
                transaction);
            transaction.Commit();
        }

        private IReadOnlyList<PlayerEvidenceGap> GetGaps(
            string table,
            PlayerEvidenceRangeQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            using var connection = connectionFactory.Open();
            return connection.Query<GapRow>(
                "SELECT id AS Id, crossplatform_id AS CrossplatformId, " +
                "started_at_utc AS StartedAtUtc, ended_at_utc AS EndedAtUtc, " +
                "reason AS Reason, estimated_lost_count AS EstimatedLostCount " +
                "FROM " + table + " WHERE crossplatform_id = @CrossplatformId " +
                "AND ended_at_utc >= @FromUtc AND started_at_utc <= @ToUtc " +
                "ORDER BY started_at_utc DESC, id DESC LIMIT @Take;",
                RangeParameters(query))
                .Select(ToGap)
                .ToArray();
        }

        private static IReadOnlyList<PlayerInventorySnapshot> ReadInventoryAggregates(
            SqliteConnection connection,
            SqliteTransaction transaction,
            InventorySnapshotRow[] rows)
        {
            if (rows.Length == 0) return Array.Empty<PlayerInventorySnapshot>();
            var ids = rows.Select(row => row.Id).ToArray();
            var itemRows = connection.Query<InventoryItemRow>(
                @"SELECT snapshot_id AS SnapshotId, container_kind AS ContainerKind,
                         slot_index AS SlotIndex, internal_name AS InternalName,
                         count AS Count, quality AS Quality, use_amount AS UseAmount
                  FROM player_inventory_items
                  WHERE snapshot_id IN @Ids
                  ORDER BY snapshot_id, container_kind, slot_index;",
                new { Ids = ids }, transaction).ToArray();
            var modRows = connection.Query<InventoryModRow>(
                @"SELECT snapshot_id AS SnapshotId, container_kind AS ContainerKind,
                         slot_index AS SlotIndex, ordinal AS Ordinal, internal_name AS InternalName
                  FROM player_inventory_item_mods
                  WHERE snapshot_id IN @Ids
                  ORDER BY snapshot_id, container_kind, slot_index, ordinal;",
                new { Ids = ids }, transaction).ToArray();
            var mods = modRows
                .GroupBy(row => ItemKey(row.SnapshotId, row.ContainerKind, row.SlotIndex))
                .ToDictionary(group => group.Key, group => group.Select(row => row.InternalName).ToArray());
            var items = itemRows
                .GroupBy(row => row.SnapshotId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => new InventoryItemScalar(
                        row.ContainerKind,
                        row.SlotIndex,
                        row.InternalName,
                        row.Count,
                        row.Quality,
                        ParseNullableDecimal(row.UseAmount),
                        mods.TryGetValue(
                            ItemKey(row.SnapshotId, row.ContainerKind, row.SlotIndex),
                            out var values)
                            ? values
                            : Array.Empty<string>())).ToArray());
            return rows.Select(row => new PlayerInventorySnapshot(
                row.Id,
                row.CrossplatformId,
                row.ServerId,
                row.WorldId,
                FromUnixMilliseconds(row.ObservedAtUtc),
                row.GameVersion,
                row.CatalogVersion,
                ParseEnum<CatalogResolutionState>(row.CatalogResolution),
                row.Fingerprint,
                row.AdminBoundary != 0,
                items.TryGetValue(row.Id, out var values)
                    ? values
                    : Array.Empty<InventoryItemScalar>())).ToArray();
        }

        private static IReadOnlyList<PlayerSkillSnapshot> ReadSkillAggregates(
            SqliteConnection connection,
            SqliteTransaction transaction,
            SkillSnapshotRow[] rows)
        {
            if (rows.Length == 0) return Array.Empty<PlayerSkillSnapshot>();
            var values = connection.Query<SkillValueRow>(
                @"SELECT snapshot_id AS SnapshotId, skill_key AS SkillKey, state AS State,
                         value AS Value, minimum AS Minimum, maximum AS Maximum,
                         next_level_cost AS NextLevelCost, parent_key AS ParentKey
                  FROM player_skill_values
                  WHERE snapshot_id IN @Ids
                  ORDER BY snapshot_id, skill_key;",
                new { Ids = rows.Select(row => row.Id).ToArray() },
                transaction)
                .GroupBy(row => row.SnapshotId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(value => new PlayerSkillValue(
                        value.SkillKey,
                        ParseEnum<SkillValueState>(value.State),
                        value.Value,
                        value.Minimum,
                        value.Maximum,
                        value.NextLevelCost,
                        value.ParentKey)).ToArray());
            return rows.Select(row => new PlayerSkillSnapshot(
                row.Id,
                row.CrossplatformId,
                row.ServerId,
                row.WorldId,
                FromUnixMilliseconds(row.ObservedAtUtc),
                row.GameVersion,
                row.Level,
                row.SkillPoints,
                values.TryGetValue(row.Id, out var snapshotValues)
                    ? snapshotValues
                    : Array.Empty<PlayerSkillValue>())).ToArray();
        }

        private static IReadOnlyList<PlayerEvidenceGap> ReadPageGaps<TRow>(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string table,
            string crossplatformId,
            TRow[] rows)
            where TRow : SnapshotRow
        {
            if (rows.Length == 0) return Array.Empty<PlayerEvidenceGap>();
            return connection.Query<GapRow>(
                "SELECT id AS Id, crossplatform_id AS CrossplatformId, " +
                "started_at_utc AS StartedAtUtc, ended_at_utc AS EndedAtUtc, " +
                "reason AS Reason, estimated_lost_count AS EstimatedLostCount " +
                "FROM " + table + " WHERE crossplatform_id = @CrossplatformId " +
                "AND ended_at_utc >= @OldestUtc AND started_at_utc <= @NewestUtc " +
                "ORDER BY started_at_utc ASC, id ASC;",
                new
                {
                    CrossplatformId = crossplatformId,
                    OldestUtc = rows.Min(row => row.ObservedAtUtc),
                    NewestUtc = rows.Max(row => row.ObservedAtUtc)
                },
                transaction)
                .Select(ToGap)
                .ToArray();
        }

        private static PlayerEvidenceCursor? NextCursor<TRow>(
            int rowCount,
            int pageSize,
            TRow[] pageRows)
            where TRow : SnapshotRow =>
            rowCount > pageSize && pageRows.Length != 0
                ? new PlayerEvidenceCursor(
                    FromUnixMilliseconds(pageRows[pageRows.Length - 1].ObservedAtUtc),
                    pageRows[pageRows.Length - 1].Id)
                : null;

        private static void CompactInventory(
            SqliteConnection connection,
            SqliteTransaction transaction,
            PlayerEvidenceCompactionRequest request)
        {
            var rows = connection.Query<InventoryRetentionRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         observed_at_utc AS ObservedAtUtc, fingerprint AS Fingerprint,
                         admin_boundary AS AdminBoundary
                  FROM player_inventory_snapshots
                  ORDER BY crossplatform_id, observed_at_utc, id;",
                transaction: transaction).ToArray();
            var victims = new List<long>();
            foreach (var group in rows.GroupBy(row => row.CrossplatformId, StringComparer.Ordinal))
            {
                var ordered = group.ToArray();
                var protectedIds = new HashSet<long> { ordered[0].Id, ordered[ordered.Length - 1].Id };
                for (var index = 0; index < ordered.Length; index++)
                {
                    if (ordered[index].AdminBoundary != 0)
                        protectedIds.Add(ordered[index].Id);
                    if (index != 0 && !string.Equals(
                            ordered[index - 1].Fingerprint,
                            ordered[index].Fingerprint,
                            StringComparison.Ordinal))
                        protectedIds.Add(ordered[index].Id);
                }
                AddBucketVictims(
                    ordered,
                    protectedIds,
                    request,
                    row => row.Id,
                    row => row.ObservedAtUtc,
                    victims);
            }
            DeleteIds(connection, transaction, "player_inventory_snapshots", victims);
        }

        private static void CompactSkills(
            SqliteConnection connection,
            SqliteTransaction transaction,
            PlayerEvidenceCompactionRequest request)
        {
            var rows = connection.Query<SkillRetentionRow>(
                @"SELECT id AS Id, crossplatform_id AS CrossplatformId,
                         observed_at_utc AS ObservedAtUtc
                  FROM player_skill_snapshots
                  ORDER BY crossplatform_id, observed_at_utc, id;",
                transaction: transaction).ToArray();
            var victims = new List<long>();
            foreach (var group in rows.GroupBy(row => row.CrossplatformId, StringComparer.Ordinal))
            {
                var ordered = group.ToArray();
                var protectedIds = new HashSet<long> { ordered[0].Id, ordered[ordered.Length - 1].Id };
                AddBucketVictims(
                    ordered,
                    protectedIds,
                    request,
                    row => row.Id,
                    row => row.ObservedAtUtc,
                    victims);
            }
            DeleteIds(connection, transaction, "player_skill_snapshots", victims);
        }

        private static void AddBucketVictims<TRow>(
            TRow[] ordered,
            HashSet<long> protectedIds,
            PlayerEvidenceCompactionRequest request,
            Func<TRow, long> id,
            Func<TRow, long> observedAtUtc,
            ICollection<long> victims)
        {
            var retainAfterUtc = ToUnixMilliseconds(request.RetainAfterUtc);
            var bucketMilliseconds = checked((long)request.BucketSize.TotalMilliseconds);
            var winners = new HashSet<long>();
            foreach (var row in ordered.Reverse())
            {
                var rowId = id(row);
                var observed = observedAtUtc(row);
                if (observed >= retainAfterUtc || protectedIds.Contains(rowId)) continue;
                var bucket = FloorDivide(observed, bucketMilliseconds);
                if (!winners.Add(bucket)) victims.Add(rowId);
            }
        }

        private static void DeleteIds(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string table,
            IReadOnlyCollection<long> ids)
        {
            if (ids.Count == 0) return;
            connection.Execute(
                "DELETE FROM " + table + " WHERE id IN @Ids;",
                new { Ids = ids.ToArray() },
                transaction);
        }

        private static object RangeParameters(PlayerEvidenceRangeQuery query) => new
        {
            query.CrossplatformId,
            FromUtc = ToUnixMilliseconds(query.FromUtc),
            ToUtc = ToUnixMilliseconds(query.ToUtc),
            Take = query.MaximumResults
        };

        private static PlayerSession ToSession(SessionRow row) => new PlayerSession(
            row.Id,
            row.CrossplatformId,
            row.ServerId,
            row.WorldId,
            FromUnixMilliseconds(row.StartedAtUtc),
            row.EndedAtUtc.HasValue ? FromUnixMilliseconds(row.EndedAtUtc.Value) : (DateTimeOffset?)null,
            row.EndReason,
            row.LastX.HasValue
                ? new PlayerPosition(row.LastX.Value, row.LastY!.Value, row.LastZ!.Value)
                : (PlayerPosition?)null,
            ParseEnum<PlayerProfileSectionState>(row.Completeness));

        private static PlayerActivityEvent ToActivity(ActivityRow row) => new PlayerActivityEvent(
            row.Id,
            row.CrossplatformId,
            row.ServerId,
            row.WorldId,
            row.Kind,
            FromUnixMilliseconds(row.ObservedAtUtc),
            row.CorrelationId,
            ParseEnum<PlayerProfileSectionState>(row.Completeness));

        private static PlayerEvidenceGap ToGap(GapRow row) => new PlayerEvidenceGap(
            row.Id,
            row.CrossplatformId,
            FromUnixMilliseconds(row.StartedAtUtc),
            FromUnixMilliseconds(row.EndedAtUtc),
            row.Reason,
            row.EstimatedLostCount);

        private static bool GapEquals(GapRow row, PlayerEvidenceGap gap) =>
            string.Equals(row.CrossplatformId, gap.CrossplatformId, StringComparison.Ordinal)
            && row.StartedAtUtc == ToUnixMilliseconds(gap.StartedAtUtc)
            && row.EndedAtUtc == ToUnixMilliseconds(gap.EndedAtUtc)
            && string.Equals(row.Reason, gap.Reason, StringComparison.Ordinal)
            && row.EstimatedLostCount == gap.EstimatedLostCount;

        private static string ItemKey(long snapshotId, string container, int slot) =>
            snapshotId.ToString(CultureInfo.InvariantCulture) + "\u001f" + container + "\u001f" +
            slot.ToString(CultureInfo.InvariantCulture);

        private static decimal? ParseNullableDecimal(string? value) =>
            value == null
                ? (decimal?)null
                : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

        private static T ParseEnum<T>(string value) where T : struct =>
            (T)Enum.Parse(typeof(T), value, ignoreCase: false);

        private static long ToUnixMilliseconds(DateTimeOffset value) =>
            value.ToUniversalTime().ToUnixTimeMilliseconds();

        private static long? ToNullableUnixMilliseconds(DateTimeOffset? value) =>
            value.HasValue ? ToUnixMilliseconds(value.Value) : (long?)null;

        private static DateTimeOffset FromUnixMilliseconds(long value) =>
            DateTimeOffset.FromUnixTimeMilliseconds(value);

        private static long FloorDivide(long value, long divisor)
        {
            var quotient = value / divisor;
            return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
        }

        private sealed class SessionRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public string ServerId { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public long StartedAtUtc { get; set; }
            public long? EndedAtUtc { get; set; }
            public string? EndReason { get; set; }
            public float? LastX { get; set; }
            public float? LastY { get; set; }
            public float? LastZ { get; set; }
            public string Completeness { get; set; } = string.Empty;
        }

        private sealed class ActivityRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public string ServerId { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public long ObservedAtUtc { get; set; }
            public string? CorrelationId { get; set; }
            public string Completeness { get; set; } = string.Empty;
        }

        private abstract class SnapshotRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public string ServerId { get; set; } = string.Empty;
            public string WorldId { get; set; } = string.Empty;
            public long ObservedAtUtc { get; set; }
            public string GameVersion { get; set; } = string.Empty;
        }

        private sealed class InventorySnapshotRow : SnapshotRow
        {
            public string? CatalogVersion { get; set; }
            public string CatalogResolution { get; set; } = string.Empty;
            public string Fingerprint { get; set; } = string.Empty;
            public int AdminBoundary { get; set; }
        }

        private sealed class InventoryItemRow
        {
            public long SnapshotId { get; set; }
            public string ContainerKind { get; set; } = string.Empty;
            public int SlotIndex { get; set; }
            public string InternalName { get; set; } = string.Empty;
            public int Count { get; set; }
            public int? Quality { get; set; }
            public string? UseAmount { get; set; }
        }

        private sealed class InventoryModRow
        {
            public long SnapshotId { get; set; }
            public string ContainerKind { get; set; } = string.Empty;
            public int SlotIndex { get; set; }
            public int Ordinal { get; set; }
            public string InternalName { get; set; } = string.Empty;
        }

        private sealed class SkillSnapshotRow : SnapshotRow
        {
            public int? Level { get; set; }
            public int? SkillPoints { get; set; }
        }

        private sealed class SkillValueRow
        {
            public long SnapshotId { get; set; }
            public string SkillKey { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
            public int? Value { get; set; }
            public int? Minimum { get; set; }
            public int? Maximum { get; set; }
            public int? NextLevelCost { get; set; }
            public string? ParentKey { get; set; }
        }

        private sealed class GapRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public long StartedAtUtc { get; set; }
            public long EndedAtUtc { get; set; }
            public string Reason { get; set; } = string.Empty;
            public long EstimatedLostCount { get; set; }
        }

        private sealed class InventoryRetentionRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public long ObservedAtUtc { get; set; }
            public string Fingerprint { get; set; } = string.Empty;
            public int AdminBoundary { get; set; }
        }

        private sealed class SkillRetentionRow
        {
            public long Id { get; set; }
            public string CrossplatformId { get; set; } = string.Empty;
            public long ObservedAtUtc { get; set; }
        }
    }
}
