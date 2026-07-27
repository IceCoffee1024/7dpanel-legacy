using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application.GameEvents;
using Microsoft.Data.Sqlite;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteGameEventStore : IGameEventStore
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteGameEventStore(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void Append(GameEventRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            using var connection = connectionFactory.Open();
            connection.Execute(@"INSERT INTO game_events (
                event_id, event_type, occurred_utc, observed_utc,
                actor_crossplatform_id, actor_platform_id, actor_entity_id, actor_name,
                target_crossplatform_id, target_platform_id, target_entity_id, target_name,
                game_shutting_down)
                VALUES (@EventId, @EventType, @OccurredUtc, @ObservedUtc,
                @ActorCrossplatformId, @ActorPlatformId, @ActorEntityId, @ActorName,
                @TargetCrossplatformId, @TargetPlatformId, @TargetEntityId, @TargetName,
                @GameShuttingDown);", new
            {
                record.EventId,
                EventType = record.EventType.ToString(),
                OccurredUtc = record.OccurredAtUtc.ToUnixTimeMilliseconds(),
                ObservedUtc = record.ObservedAtUtc.ToUnixTimeMilliseconds(),
                ActorCrossplatformId = record.Actor?.CrossplatformId,
                ActorPlatformId = record.Actor?.PlatformId,
                ActorEntityId = record.Actor?.EntityId,
                ActorName = record.Actor?.DisplayName,
                TargetCrossplatformId = record.Target?.CrossplatformId,
                TargetPlatformId = record.Target?.PlatformId,
                TargetEntityId = record.Target?.EntityId,
                TargetName = record.Target?.DisplayName,
                GameShuttingDown = record.GameShuttingDown.HasValue ? (record.GameShuttingDown.Value ? 1 : 0) : (int?)null
            });
        }

        public void AppendGap(GameEventGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));
            using var connection = connectionFactory.Open();
            connection.Execute(@"INSERT INTO game_event_gaps (
                gap_id, reason, started_utc, ended_utc, affected_count)
                VALUES (@GapId, @Reason, @StartedUtc, @EndedUtc, @AffectedCount);", new
            {
                gap.GapId,
                Reason = gap.Reason.ToString(),
                StartedUtc = gap.StartedAtUtc.ToUnixTimeMilliseconds(),
                EndedUtc = gap.EndedAtUtc?.ToUnixTimeMilliseconds(),
                gap.AffectedCount
            });
        }

        public GameEventPage Query(GameEventQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", query.PageSize + 1);
            if (query.FromUtc.HasValue) { where.Add("occurred_utc >= @FromUtc"); parameters.Add("FromUtc", query.FromUtc.Value.ToUnixTimeMilliseconds()); }
            if (query.ToUtc.HasValue) { where.Add("occurred_utc <= @ToUtc"); parameters.Add("ToUtc", query.ToUtc.Value.ToUnixTimeMilliseconds()); }
            if (query.EventType.HasValue) { where.Add("event_type = @EventType"); parameters.Add("EventType", query.EventType.Value.ToString()); }
            if (query.CrossplatformId != null)
            {
                where.Add("(actor_crossplatform_id = @CrossplatformId OR target_crossplatform_id = @CrossplatformId)");
                parameters.Add("CrossplatformId", query.CrossplatformId);
            }
            if (query.Cursor != null)
            {
                where.Add("(occurred_utc < @CursorUtc OR (occurred_utc = @CursorUtc AND event_id < @CursorEventId))");
                parameters.Add("CursorUtc", query.Cursor.OccurredAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorEventId", query.Cursor.EventId);
            }

            using var connection = connectionFactory.Open();
            var rows = connection.Query<EventRow>(
                "SELECT event_id AS EventId, event_type AS EventType, occurred_utc AS OccurredUtc, observed_utc AS ObservedUtc, " +
                "actor_crossplatform_id AS ActorCrossplatformId, actor_platform_id AS ActorPlatformId, actor_entity_id AS ActorEntityId, actor_name AS ActorName, " +
                "target_crossplatform_id AS TargetCrossplatformId, target_platform_id AS TargetPlatformId, target_entity_id AS TargetEntityId, target_name AS TargetName, " +
                "game_shutting_down AS GameShuttingDown FROM game_events" +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY occurred_utc DESC, event_id DESC LIMIT @Take;", parameters).ToArray();
            var pageRows = rows.Take(query.PageSize).ToArray();
            var next = rows.Length > query.PageSize && pageRows.Length > 0
                ? new GameEventCursor(FromUnix(pageRows[pageRows.Length - 1].OccurredUtc), pageRows[pageRows.Length - 1].EventId)
                : null;
            var gaps = QueryGaps(connection, pageRows, query);
            return new GameEventPage(pageRows.Select(ToRecord), next, gaps);
        }

        private static IReadOnlyList<GameEventGap> QueryGaps(
            SqliteConnection connection, EventRow[] rows, GameEventQuery query)
        {
            var from = rows.Length == 0 ? query.FromUtc?.ToUnixTimeMilliseconds() : rows.Min(row => row.OccurredUtc);
            var to = rows.Length == 0 ? query.ToUtc?.ToUnixTimeMilliseconds() : rows.Max(row => row.OccurredUtc);
            if (!from.HasValue || !to.HasValue) return Array.Empty<GameEventGap>();
            return connection.Query<GapRow>(@"SELECT gap_id AS GapId, reason AS Reason, started_utc AS StartedUtc,
                ended_utc AS EndedUtc, affected_count AS AffectedCount FROM game_event_gaps
                WHERE COALESCE(ended_utc, started_utc) >= @FromUtc AND started_utc <= @ToUtc
                ORDER BY started_utc DESC, gap_id DESC;", new { FromUtc = from.Value, ToUtc = to.Value })
                .Select(row => new GameEventGap(row.GapId, (GameEventGapReason)Enum.Parse(typeof(GameEventGapReason), row.Reason), FromUnix(row.StartedUtc), row.EndedUtc.HasValue ? FromUnix(row.EndedUtc.Value) : null, row.AffectedCount))
                .ToArray();
        }

        private static GameEventRecord ToRecord(EventRow row) => new GameEventRecord(
            row.EventId, (GameEventType)Enum.Parse(typeof(GameEventType), row.EventType), FromUnix(row.OccurredUtc), FromUnix(row.ObservedUtc),
            Subject(row.ActorCrossplatformId, row.ActorPlatformId, row.ActorEntityId, row.ActorName),
            Subject(row.TargetCrossplatformId, row.TargetPlatformId, row.TargetEntityId, row.TargetName),
            row.GameShuttingDown.HasValue ? row.GameShuttingDown.Value != 0 : (bool?)null);
        private static GameEventSubject? Subject(string? crossplatformId, string? platformId, int? entityId, string? name) =>
            crossplatformId == null && platformId == null && !entityId.HasValue && name == null ? null : new GameEventSubject(crossplatformId, platformId, entityId, name);
        private static DateTimeOffset FromUnix(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

        private sealed class EventRow
        {
            public string EventId { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public long OccurredUtc { get; set; }
            public long ObservedUtc { get; set; }
            public string? ActorCrossplatformId { get; set; }
            public string? ActorPlatformId { get; set; }
            public int? ActorEntityId { get; set; }
            public string? ActorName { get; set; }
            public string? TargetCrossplatformId { get; set; }
            public string? TargetPlatformId { get; set; }
            public int? TargetEntityId { get; set; }
            public string? TargetName { get; set; }
            public int? GameShuttingDown { get; set; }
        }
        private sealed class GapRow { public string GapId { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; public long StartedUtc { get; set; } public long? EndedUtc { get; set; } public long AffectedCount { get; set; } }
    }
}
