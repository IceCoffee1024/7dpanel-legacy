using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class UnifiedAuditFilter
    {
        public UnifiedAuditFilter(
            int pageSize,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            string? actorSubject,
            string? targetRef,
            string? action,
            string? sourceKind,
            string? status,
            UnifiedAuditCursor? cursor)
        {
            if (pageSize < 1 || pageSize > 200) throw new ArgumentOutOfRangeException(nameof(pageSize));
            if (fromUtc.HasValue) UnifiedAuditEntry.RequireUtc(fromUtc.Value, nameof(fromUtc));
            if (toUtc.HasValue) UnifiedAuditEntry.RequireUtc(toUtc.Value, nameof(toUtc));
            if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
                throw new ArgumentException("The start time cannot follow the end time.");

            PageSize = pageSize;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            ActorSubject = UnifiedAuditEntry.Normalize(actorSubject);
            TargetRef = UnifiedAuditEntry.Normalize(targetRef);
            Action = UnifiedAuditEntry.Normalize(action);
            SourceKind = UnifiedAuditEntry.Normalize(sourceKind);
            Status = UnifiedAuditEntry.Normalize(status);
            Cursor = cursor;
        }

        public int PageSize { get; }
        public DateTimeOffset? FromUtc { get; }
        public DateTimeOffset? ToUtc { get; }
        public string? ActorSubject { get; }
        public string? TargetRef { get; }
        public string? Action { get; }
        public string? SourceKind { get; }
        public string? Status { get; }
        public UnifiedAuditCursor? Cursor { get; }
    }

    public interface IUnifiedAuditQuery
    {
        UnifiedAuditPage Query(UnifiedAuditFilter filter);
    }
}
