using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class HistoricalPlayerSummary
    {
        public HistoricalPlayerSummary(
            string crossplatformId,
            string latestName,
            DateTimeOffset firstObservedAtUtc,
            DateTimeOffset lastObservedAtUtc,
            long totalObservationCount,
            long retainedSnapshotCount,
            long compactedSnapshotCount,
            bool hasGaps)
        {
            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            if (string.IsNullOrWhiteSpace(latestName))
                throw new ArgumentException("A latest player name is required.", nameof(latestName));

            FirstObservedAtUtc = HistoryPlayerValidation.RequireUtc(
                firstObservedAtUtc,
                nameof(firstObservedAtUtc));
            LastObservedAtUtc = HistoryPlayerValidation.RequireUtc(
                lastObservedAtUtc,
                nameof(lastObservedAtUtc));
            if (lastObservedAtUtc < firstObservedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(lastObservedAtUtc));
            if (totalObservationCount < 0 || retainedSnapshotCount < 0 || compactedSnapshotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(totalObservationCount));
            if (totalObservationCount != retainedSnapshotCount + compactedSnapshotCount)
                throw new ArgumentException(
                    "Total observations must equal retained plus compacted snapshots.",
                    nameof(totalObservationCount));

            LatestName = latestName;
            TotalObservationCount = totalObservationCount;
            RetainedSnapshotCount = retainedSnapshotCount;
            CompactedSnapshotCount = compactedSnapshotCount;
            HasGaps = hasGaps;
        }

        public string CrossplatformId { get; }

        public string LatestName { get; }

        public DateTimeOffset FirstObservedAtUtc { get; }

        public DateTimeOffset LastObservedAtUtc { get; }

        public long TotalObservationCount { get; }

        public long RetainedSnapshotCount { get; }

        public long CompactedSnapshotCount { get; }

        public bool HasGaps { get; }
    }

    public sealed class PlayerHistoryGapSummary
    {
        public PlayerHistoryGapSummary(long gapCount, long droppedObservationCount)
        {
            if (gapCount < 0)
                throw new ArgumentOutOfRangeException(nameof(gapCount));
            if (droppedObservationCount < 0)
                throw new ArgumentOutOfRangeException(nameof(droppedObservationCount));

            GapCount = gapCount;
            DroppedObservationCount = droppedObservationCount;
        }

        public long GapCount { get; }

        public long DroppedObservationCount { get; }
    }

    public sealed class HistoricalPlayerDetails
    {
        public HistoricalPlayerDetails(HistoricalPlayerSummary player, PlayerHistoryGapSummary gapSummary)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            GapSummary = gapSummary ?? throw new ArgumentNullException(nameof(gapSummary));
        }

        public HistoricalPlayerSummary Player { get; }

        public PlayerHistoryGapSummary GapSummary { get; }
    }
}
