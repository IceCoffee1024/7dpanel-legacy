using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class AuditPageHttpResponse
    {
        public AuditPageHttpResponse(UnifiedAuditPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            Entries = page.Entries.Select(entry => new AuditEntryHttpResponse(entry)).ToArray();
            NextCursor = page.NextCursor == null ? null : AuditCursorCodec.Encode(page.NextCursor);
            SourceGaps = page.Gaps.Select(gap => new AuditSourceGapHttpResponse(gap)).ToArray();
        }

        public IReadOnlyList<AuditEntryHttpResponse> Entries { get; }
        public string? NextCursor { get; }
        public IReadOnlyList<AuditSourceGapHttpResponse> SourceGaps { get; }
    }

    public sealed class AuditEntryHttpResponse
    {
        public AuditEntryHttpResponse(UnifiedAuditEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            SourceKind = entry.SourceKind;
            SourceId = entry.SourceId;
            ActorSubject = entry.ActorSubject;
            TargetRef = entry.TargetRef;
            Action = entry.Action;
            OccurredAtUtc = entry.OccurredAtUtc;
            Status = entry.Status;
            CorrelationId = entry.CorrelationId;
            HasDetails = entry.HasDetails;
        }

        public string SourceKind { get; }
        public string SourceId { get; }
        public string? ActorSubject { get; }
        public string? TargetRef { get; }
        public string Action { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public string Status { get; }
        public string? CorrelationId { get; }
        public bool HasDetails { get; }
    }

    public sealed class AuditSourceGapHttpResponse
    {
        public AuditSourceGapHttpResponse(AuditSourceGap gap)
        {
            if (gap == null) throw new ArgumentNullException(nameof(gap));
            SourceKind = gap.SourceKind;
            StartedAtUtc = gap.StartedAtUtc;
            EndedAtUtc = gap.EndedAtUtc;
            AffectedCount = gap.AffectedCount;
            Reason = gap.Reason;
        }

        public string SourceKind { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public long AffectedCount { get; }
        public string Reason { get; }
    }
}
