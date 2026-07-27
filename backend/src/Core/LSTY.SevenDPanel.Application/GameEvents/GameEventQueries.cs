using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application.GameEvents
{
    public sealed class GameEventCursor : IComparable<GameEventCursor>
    {
        public GameEventCursor(DateTimeOffset occurredAtUtc, string eventId)
        {
            GameEventRecord.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            if (!Guid.TryParseExact(eventId, "D", out _))
                throw new ArgumentException("A canonical GUID event identifier is required.", nameof(eventId));
            OccurredAtUtc = occurredAtUtc;
            EventId = eventId;
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string EventId { get; }

        public int CompareTo(GameEventCursor? other)
        {
            if (other == null) return -1;
            var occurred = other.OccurredAtUtc.CompareTo(OccurredAtUtc);
            return occurred != 0
                ? occurred
                : string.Compare(other.EventId, EventId, StringComparison.Ordinal);
        }
    }

    public sealed class GameEventQuery
    {
        public GameEventQuery(
            int pageSize = 50,
            DateTimeOffset? fromUtc = null,
            DateTimeOffset? toUtc = null,
            GameEventType? eventType = null,
            string? crossplatformId = null,
            GameEventCursor? cursor = null)
        {
            if (pageSize < 1 || pageSize > 200) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (fromUtc.HasValue) GameEventRecord.RequireUtc(fromUtc.Value, nameof(fromUtc));
            if (toUtc.HasValue) GameEventRecord.RequireUtc(toUtc.Value, nameof(toUtc));
            if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
                throw new ArgumentException("The start time cannot follow the end time.");
            if (eventType.HasValue && !Enum.IsDefined(typeof(GameEventType), eventType.Value))
                throw new ArgumentOutOfRangeException(nameof(eventType));

            PageSize = pageSize;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            EventType = eventType;
            CrossplatformId = string.IsNullOrWhiteSpace(crossplatformId) ? null : crossplatformId!.Trim();
            Cursor = cursor;
        }

        public int PageSize { get; }
        public DateTimeOffset? FromUtc { get; }
        public DateTimeOffset? ToUtc { get; }
        public GameEventType? EventType { get; }
        public string? CrossplatformId { get; }
        public GameEventCursor? Cursor { get; }
    }

    public sealed class GameEventPage
    {
        public GameEventPage(
            IEnumerable<GameEventRecord> events,
            GameEventCursor? nextCursor,
            IEnumerable<GameEventGap> gaps)
        {
            Events = (events ?? throw new ArgumentNullException(nameof(events))).ToArray();
            Gaps = (gaps ?? throw new ArgumentNullException(nameof(gaps))).ToArray();
            NextCursor = nextCursor;
        }

        public IReadOnlyList<GameEventRecord> Events { get; }
        public GameEventCursor? NextCursor { get; }
        public IReadOnlyList<GameEventGap> Gaps { get; }
    }
}
