using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.GameEvents;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class GameEventPageHttpResponse
    {
        internal GameEventPageHttpResponse(GameEventPage page, GameEventCursorFilters filters)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (filters == null) throw new ArgumentNullException(nameof(filters));
            Events = page.Events.Select(value => new GameEventHttpResponse(value)).ToArray();
            Gaps = page.Gaps.Select(value => new GameEventGapHttpResponse(value)).ToArray();
            NextCursor = page.NextCursor == null ? null : GameEventCursorCodec.Encode(page.NextCursor, filters);
        }
        public IReadOnlyList<GameEventHttpResponse> Events { get; }
        public IReadOnlyList<GameEventGapHttpResponse> Gaps { get; }
        public string? NextCursor { get; }
    }
    public sealed class GameEventHttpResponse
    {
        public GameEventHttpResponse(GameEventRecord value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            EventId = value.EventId; EventType = value.EventType.ToString(); OccurredAtUtc = value.OccurredAtUtc; ObservedAtUtc = value.ObservedAtUtc;
            Actor = value.Actor == null ? null : new GameEventSubjectHttpResponse(value.Actor); Target = value.Target == null ? null : new GameEventSubjectHttpResponse(value.Target); GameShuttingDown = value.GameShuttingDown;
        }
        public string EventId { get; } public string EventType { get; } public DateTimeOffset OccurredAtUtc { get; } public DateTimeOffset ObservedAtUtc { get; } public GameEventSubjectHttpResponse? Actor { get; } public GameEventSubjectHttpResponse? Target { get; } public bool? GameShuttingDown { get; }
    }
    public sealed class GameEventSubjectHttpResponse
    {
        public GameEventSubjectHttpResponse(GameEventSubject value) { CrossplatformId = value.CrossplatformId; PlatformId = value.PlatformId; EntityId = value.EntityId; DisplayName = value.DisplayName; }
        public string? CrossplatformId { get; } public string? PlatformId { get; } public int? EntityId { get; } public string? DisplayName { get; }
    }
    public sealed class GameEventGapHttpResponse
    {
        public GameEventGapHttpResponse(GameEventGap value) { GapId = value.GapId; Reason = value.Reason.ToString(); StartedAtUtc = value.StartedAtUtc; EndedAtUtc = value.EndedAtUtc; AffectedCount = value.AffectedCount; }
        public string GapId { get; } public string Reason { get; } public DateTimeOffset StartedAtUtc { get; } public DateTimeOffset? EndedAtUtc { get; } public long AffectedCount { get; }
    }
}
