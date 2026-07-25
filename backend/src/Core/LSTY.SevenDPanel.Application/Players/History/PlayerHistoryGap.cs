using System;

namespace LSTY.SevenDPanel.Application
{
    public enum PlayerHistoryGapReason
    {
        QueueFull,
        StoreFailure,
        ShutdownTimeout
    }

    public sealed class PlayerHistoryGap
    {
        public PlayerHistoryGap(
            string gapId,
            string crossplatformId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            long droppedCount,
            PlayerHistoryGapReason reason,
            DateTimeOffset recordedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(gapId))
                throw new ArgumentException("A gap identifier is required.", nameof(gapId));
            if (completedAtUtc < startedAtUtc)
                throw new ArgumentOutOfRangeException(nameof(completedAtUtc));
            if (droppedCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(droppedCount));

            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            StartedAtUtc = HistoryPlayerValidation.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            CompletedAtUtc = HistoryPlayerValidation.RequireUtc(completedAtUtc, nameof(completedAtUtc));
            RecordedAtUtc = HistoryPlayerValidation.RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
            GapId = gapId;
            DroppedCount = droppedCount;
            Reason = reason;
        }

        public string GapId { get; }

        public string CrossplatformId { get; }

        public DateTimeOffset StartedAtUtc { get; }

        public DateTimeOffset CompletedAtUtc { get; }

        public long DroppedCount { get; }

        public PlayerHistoryGapReason Reason { get; }

        public DateTimeOffset RecordedAtUtc { get; }
    }
}
