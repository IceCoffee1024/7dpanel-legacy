using System;

namespace LSTY.SevenDPanel.Application.ConsoleCommands
{
    public sealed class ConsoleCommandAuditGap
    {
        public ConsoleCommandAuditGap(
            string gapId,
            DateTimeOffset startedAtUtc,
            DateTimeOffset completedAtUtc,
            long droppedCount,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(gapId))
                throw new ArgumentException("A gap identifier is required.", nameof(gapId));
            if (completedAtUtc < startedAtUtc)
                throw new ArgumentOutOfRangeException(
                    nameof(completedAtUtc),
                    "The completion time cannot precede the start time.");
            if (droppedCount <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(droppedCount),
                    "A gap must describe at least one dropped record.");
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("A gap reason is required.", nameof(reason));

            GapId = gapId;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            DroppedCount = droppedCount;
            Reason = reason;
        }

        public string GapId { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset CompletedAtUtc { get; }
        public long DroppedCount { get; }
        public string Reason { get; }
    }
}