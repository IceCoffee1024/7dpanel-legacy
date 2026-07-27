using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class UnifiedAuditEntry
    {
        public UnifiedAuditEntry(
            string sourceKind,
            string sourceId,
            string? actorSubject,
            string? targetRef,
            string action,
            DateTimeOffset occurredAtUtc,
            string status,
            string? correlationId,
            bool hasDetails)
        {
            SourceKind = RequireText(sourceKind, nameof(sourceKind));
            SourceId = RequireText(sourceId, nameof(sourceId));
            ActorSubject = Normalize(actorSubject);
            TargetRef = Normalize(targetRef);
            Action = RequireText(action, nameof(action));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
            Status = RequireText(status, nameof(status));
            CorrelationId = Normalize(correlationId);
            HasDetails = hasDetails;
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

        internal static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        internal static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    public sealed class AuditSourceGap
    {
        public AuditSourceGap(
            string sourceKind,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            long affectedCount,
            string reason)
        {
            SourceKind = UnifiedAuditEntry.RequireText(sourceKind, nameof(sourceKind));
            UnifiedAuditEntry.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            if (endedAtUtc.HasValue)
            {
                UnifiedAuditEntry.RequireUtc(endedAtUtc.Value, nameof(endedAtUtc));
                if (endedAtUtc.Value < startedAtUtc)
                    throw new ArgumentException("A gap cannot end before it starts.", nameof(endedAtUtc));
            }
            if (affectedCount <= 0) throw new ArgumentOutOfRangeException(nameof(affectedCount));

            StartedAtUtc = startedAtUtc;
            EndedAtUtc = endedAtUtc;
            AffectedCount = affectedCount;
            Reason = UnifiedAuditEntry.RequireText(reason, nameof(reason));
        }

        public string SourceKind { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public long AffectedCount { get; }
        public string Reason { get; }
    }

    public sealed class UnifiedAuditCursor : IComparable<UnifiedAuditCursor>
    {
        public UnifiedAuditCursor(DateTimeOffset occurredAtUtc, string sourceKind, string sourceId)
        {
            UnifiedAuditEntry.RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            OccurredAtUtc = occurredAtUtc;
            SourceKind = UnifiedAuditEntry.RequireText(sourceKind, nameof(sourceKind));
            SourceId = UnifiedAuditEntry.RequireText(sourceId, nameof(sourceId));
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string SourceKind { get; }
        public string SourceId { get; }

        public int CompareTo(UnifiedAuditCursor? other)
        {
            if (other == null) return -1;
            var occurred = other.OccurredAtUtc.CompareTo(OccurredAtUtc);
            if (occurred != 0) return occurred;
            var source = string.Compare(other.SourceKind, SourceKind, StringComparison.Ordinal);
            return source != 0
                ? source
                : string.Compare(other.SourceId, SourceId, StringComparison.Ordinal);
        }
    }

    public sealed class UnifiedAuditPage
    {
        public UnifiedAuditPage(
            IEnumerable<UnifiedAuditEntry> entries,
            UnifiedAuditCursor? nextCursor,
            IEnumerable<AuditSourceGap> gaps)
        {
            Entries = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
            NextCursor = nextCursor;
            Gaps = (gaps ?? throw new ArgumentNullException(nameof(gaps))).ToArray();
        }

        public IReadOnlyList<UnifiedAuditEntry> Entries { get; }
        public UnifiedAuditCursor? NextCursor { get; }
        public IReadOnlyList<AuditSourceGap> Gaps { get; }
    }
}
