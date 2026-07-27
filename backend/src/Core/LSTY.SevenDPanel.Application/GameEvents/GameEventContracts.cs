using System;

namespace LSTY.SevenDPanel.Application.GameEvents
{
    public enum GameEventType
    {
        PlayerJoined,
        PlayerLeft,
        PlayerKilledEntity,
        PlayerDied
    }

    public enum GameEventGapReason
    {
        QueueFull,
        StoreFailure,
        DrainTimeout
    }

    public sealed class GameEventSubject
    {
        public GameEventSubject(
            string? crossplatformId,
            string? platformId,
            int? entityId,
            string? displayName)
        {
            if (entityId.HasValue && entityId.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(entityId));
            CrossplatformId = Normalize(crossplatformId);
            PlatformId = Normalize(platformId);
            EntityId = entityId;
            DisplayName = Normalize(displayName);
        }

        public string? CrossplatformId { get; }
        public string? StableIdentity => CrossplatformId;
        public string? PlatformId { get; }
        public int? EntityId { get; }
        public string? DisplayName { get; }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    public sealed class GameEventRecord
    {
        public GameEventRecord(
            string eventId,
            GameEventType eventType,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset observedAtUtc,
            GameEventSubject? actor,
            GameEventSubject? target,
            bool? gameShuttingDown)
        {
            if (!Guid.TryParseExact(eventId, "D", out _))
                throw new ArgumentException("A canonical GUID event identifier is required.", nameof(eventId));
            if (!Enum.IsDefined(typeof(GameEventType), eventType))
                throw new ArgumentOutOfRangeException(nameof(eventType));
            RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));

            EventId = eventId;
            EventType = eventType;
            OccurredAtUtc = occurredAtUtc;
            ObservedAtUtc = observedAtUtc;
            Actor = actor;
            Target = target;
            GameShuttingDown = gameShuttingDown;
        }

        public string EventId { get; }
        public GameEventType EventType { get; }
        public DateTimeOffset OccurredAtUtc { get; }
        public DateTimeOffset ObservedAtUtc { get; }
        public GameEventSubject? Actor { get; }
        public GameEventSubject? Target { get; }
        public bool? GameShuttingDown { get; }

        public static GameEventRecord Create(
            GameEventType eventType,
            DateTimeOffset occurredAtUtc,
            DateTimeOffset observedAtUtc,
            GameEventSubject? actor,
            GameEventSubject? target,
            bool? gameShuttingDown) =>
            new GameEventRecord(
                Guid.NewGuid().ToString("D"),
                eventType,
                occurredAtUtc,
                observedAtUtc,
                actor,
                target,
                gameShuttingDown);

        internal static void RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }

    public sealed class GameEventGap
    {
        public GameEventGap(
            string gapId,
            GameEventGapReason reason,
            DateTimeOffset startedAtUtc,
            DateTimeOffset? endedAtUtc,
            long affectedCount)
        {
            if (!Guid.TryParseExact(gapId, "D", out _))
                throw new ArgumentException("A canonical GUID gap identifier is required.", nameof(gapId));
            if (!Enum.IsDefined(typeof(GameEventGapReason), reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            GameEventRecord.RequireUtc(startedAtUtc, nameof(startedAtUtc));
            if (endedAtUtc.HasValue)
            {
                GameEventRecord.RequireUtc(endedAtUtc.Value, nameof(endedAtUtc));
                if (endedAtUtc.Value < startedAtUtc)
                    throw new ArgumentException("A gap cannot end before it starts.", nameof(endedAtUtc));
            }
            if (affectedCount <= 0) throw new ArgumentOutOfRangeException(nameof(affectedCount));

            GapId = gapId;
            Reason = reason;
            StartedAtUtc = startedAtUtc;
            EndedAtUtc = endedAtUtc;
            AffectedCount = affectedCount;
        }

        public string GapId { get; }
        public GameEventGapReason Reason { get; }
        public DateTimeOffset StartedAtUtc { get; }
        public DateTimeOffset? EndedAtUtc { get; }
        public long AffectedCount { get; }
    }
}
