using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite
{
    public sealed class SqliteUnifiedAuditQuery : IUnifiedAuditQuery
    {
        private readonly SqliteConnectionFactory connectionFactory;

        public SqliteUnifiedAuditQuery(SqliteConnectionFactory connectionFactory)
        {
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public UnifiedAuditPage Query(UnifiedAuditFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var where = new List<string>();
            var parameters = new DynamicParameters();
            parameters.Add("Take", filter.PageSize + 1);
            AddFilters(filter, where, parameters);

            using var connection = connectionFactory.Open();
            var rows = connection.Query<AuditRow>(
                @"SELECT source_kind AS SourceKind, source_id AS SourceId,
                         actor_subject AS ActorSubject, target_ref AS TargetRef,
                         action AS Action, occurred_utc AS OccurredUtc,
                         status AS Status, correlation_id AS CorrelationId,
                         has_details AS HasDetails
                  FROM unified_audit_projection" +
                (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where)) +
                " ORDER BY occurred_utc DESC, source_kind DESC, source_id DESC LIMIT @Take;",
                parameters).ToArray();

            var pageRows = rows.Take(filter.PageSize).ToArray();
            var entries = pageRows.Select(ToEntry).ToArray();
            var nextCursor = rows.Length > filter.PageSize && pageRows.Length > 0
                ? ToCursor(pageRows[pageRows.Length - 1])
                : null;
            return new UnifiedAuditPage(entries, nextCursor, QueryConsoleCommandGaps(connection, filter, pageRows));
        }

        private static void AddFilters(
            UnifiedAuditFilter filter,
            ICollection<string> where,
            DynamicParameters parameters)
        {
            if (filter.FromUtc.HasValue)
            {
                where.Add("occurred_utc >= @FromUtc");
                parameters.Add("FromUtc", filter.FromUtc.Value.ToUnixTimeMilliseconds());
            }
            if (filter.ToUtc.HasValue)
            {
                where.Add("occurred_utc <= @ToUtc");
                parameters.Add("ToUtc", filter.ToUtc.Value.ToUnixTimeMilliseconds());
            }
            AddTextFilter(filter.ActorSubject, "actor_subject", "ActorSubject", where, parameters);
            AddTextFilter(filter.TargetRef, "target_ref", "TargetRef", where, parameters);
            AddTextFilter(filter.Action, "action", "Action", where, parameters);
            AddTextFilter(filter.SourceKind, "source_kind", "SourceKind", where, parameters);
            AddTextFilter(filter.Status, "status", "Status", where, parameters);
            if (filter.Cursor != null)
            {
                where.Add(
                    "(occurred_utc < @CursorUtc OR " +
                    "(occurred_utc = @CursorUtc AND source_kind < @CursorSourceKind) OR " +
                    "(occurred_utc = @CursorUtc AND source_kind = @CursorSourceKind AND source_id < @CursorSourceId))");
                parameters.Add("CursorUtc", filter.Cursor.OccurredAtUtc.ToUnixTimeMilliseconds());
                parameters.Add("CursorSourceKind", filter.Cursor.SourceKind);
                parameters.Add("CursorSourceId", filter.Cursor.SourceId);
            }
        }

        private static void AddTextFilter(
            string? value,
            string column,
            string parameterName,
            ICollection<string> where,
            DynamicParameters parameters)
        {
            if (value == null) return;
            where.Add(column + " = @" + parameterName);
            parameters.Add(parameterName, value);
        }

        private static IReadOnlyList<AuditSourceGap> QueryConsoleCommandGaps(
            System.Data.IDbConnection connection,
            UnifiedAuditFilter filter,
            IReadOnlyList<AuditRow> pageRows)
        {
            if (pageRows.Count == 0 ||
                (filter.SourceKind != null && !string.Equals(filter.SourceKind, "consoleCommand", StringComparison.Ordinal)))
            {
                return Array.Empty<AuditSourceGap>();
            }

            var oldest = pageRows.Min(row => row.OccurredUtc);
            var newest = filter.Cursor == null
                ? pageRows.Max(row => row.OccurredUtc)
                : Math.Max(pageRows.Max(row => row.OccurredUtc), filter.Cursor.OccurredAtUtc.ToUnixTimeMilliseconds());
            return connection.Query<GapRow>(
                @"SELECT started_utc AS StartedUtc, completed_utc AS CompletedUtc,
                         dropped_count AS DroppedCount, reason AS Reason
                  FROM console_command_audit_gap
                  WHERE completed_utc >= @OldestUtc AND started_utc <= @NewestUtc
                  ORDER BY started_utc ASC, gap_id ASC;",
                new { OldestUtc = oldest, NewestUtc = newest })
                .Select(row => new AuditSourceGap(
                    "consoleCommand",
                    DateTimeOffset.FromUnixTimeMilliseconds(row.StartedUtc),
                    DateTimeOffset.FromUnixTimeMilliseconds(row.CompletedUtc),
                    row.DroppedCount,
                    row.Reason))
                .ToArray();
        }

        private static UnifiedAuditEntry ToEntry(AuditRow row) => new UnifiedAuditEntry(
            row.SourceKind,
            row.SourceId,
            row.ActorSubject,
            row.TargetRef,
            row.Action,
            DateTimeOffset.FromUnixTimeMilliseconds(row.OccurredUtc),
            row.Status,
            row.CorrelationId,
            row.HasDetails != 0);

        private static UnifiedAuditCursor ToCursor(AuditRow row) => new UnifiedAuditCursor(
            DateTimeOffset.FromUnixTimeMilliseconds(row.OccurredUtc), row.SourceKind, row.SourceId);

        private sealed class AuditRow
        {
            public string SourceKind { get; set; } = string.Empty;
            public string SourceId { get; set; } = string.Empty;
            public string? ActorSubject { get; set; }
            public string? TargetRef { get; set; }
            public string Action { get; set; } = string.Empty;
            public long OccurredUtc { get; set; }
            public string Status { get; set; } = string.Empty;
            public string? CorrelationId { get; set; }
            public int HasDetails { get; set; }
        }

        private sealed class GapRow
        {
            public long StartedUtc { get; set; }
            public long CompletedUtc { get; set; }
            public long DroppedCount { get; set; }
            public string Reason { get; set; } = string.Empty;
        }
    }
}
