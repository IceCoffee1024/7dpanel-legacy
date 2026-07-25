using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class RecentActivityItem
    {
        public RecentActivityItem(DateTimeOffset occurredAtUtc, string code, string? summary)
            : this(occurredAtUtc, code, Enumerable.Empty<KeyValuePair<string, string>>())
        {
        }

        public RecentActivityItem(
            DateTimeOffset occurredAtUtc,
            string messageKey,
            IEnumerable<KeyValuePair<string, string>>? messageArguments)
        {
            OccurredAtUtc = occurredAtUtc;
            MessageKey = messageKey ?? throw new ArgumentNullException(nameof(messageKey));
            MessageArguments = new ReadOnlyDictionary<string, string>(
                (messageArguments ?? Enumerable.Empty<KeyValuePair<string, string>>())
                    .ToDictionary(argument => argument.Key, argument => argument.Value));
        }

        public DateTimeOffset OccurredAtUtc { get; }
        public string MessageKey { get; }
        public IReadOnlyDictionary<string, string> MessageArguments { get; }

        // Compatibility aliases for the task 2 contract; consumers should move to MessageKey and MessageArguments.
        public string Code => MessageKey;
        public string? Summary => null;
    }

    public sealed class RecentActivitySnapshot
    {
        public RecentActivitySnapshot(AvailabilityState availability, DateTimeOffset? sampledAtUtc, IEnumerable<RecentActivityItem>? items)
        {
            Availability = availability;
            SampledAtUtc = sampledAtUtc;
            Items = new ReadOnlyCollection<RecentActivityItem>((items ?? Enumerable.Empty<RecentActivityItem>()).ToArray());
            TotalCount = Items.Count;
            LatestOccurredAtUtc = Items.Count == 0 ? null : Items.Max(item => item.OccurredAtUtc);
        }

        public RecentActivitySnapshot(
            AvailabilityState availability,
            DateTimeOffset? sampledAtUtc,
            int totalCount,
            DateTimeOffset? latestOccurredAtUtc,
            IEnumerable<RecentActivityItem>? items)
        {
            if (totalCount < 0) throw new ArgumentOutOfRangeException(nameof(totalCount));

            Availability = availability;
            SampledAtUtc = sampledAtUtc;
            TotalCount = totalCount;
            LatestOccurredAtUtc = latestOccurredAtUtc;
            Items = new ReadOnlyCollection<RecentActivityItem>((items ?? Enumerable.Empty<RecentActivityItem>()).ToArray());
        }

        public AvailabilityState Availability { get; }
        public DateTimeOffset? SampledAtUtc { get; }
        public int TotalCount { get; }
        public DateTimeOffset? LatestOccurredAtUtc { get; }
        public IReadOnlyList<RecentActivityItem> Items { get; }
        public static RecentActivitySnapshot Unavailable() => new RecentActivitySnapshot(AvailabilityState.Unavailable, null, Enumerable.Empty<RecentActivityItem>());
    }
}
