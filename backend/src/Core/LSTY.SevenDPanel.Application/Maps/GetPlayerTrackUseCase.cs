using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class GetPlayerTrackQuery
    {
        public const int MaximumObservations = 5000;
        public const int MaximumContinuityGaps = 5000;
        public static readonly TimeSpan MaximumRange = TimeSpan.FromDays(30);

        public GetPlayerTrackQuery(string crossplatformId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
        {
            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            FromUtc = HistoryPlayerValidation.RequireUtc(fromUtc, nameof(fromUtc));
            ToUtc = HistoryPlayerValidation.RequireUtc(toUtc, nameof(toUtc));
            if (ToUtc < FromUtc)
                throw new ArgumentOutOfRangeException(nameof(toUtc));
            if (ToUtc - FromUtc > MaximumRange)
                throw new ArgumentOutOfRangeException(nameof(toUtc));
        }

        public string CrossplatformId { get; }

        public DateTimeOffset FromUtc { get; }

        public DateTimeOffset ToUtc { get; }
    }

    public sealed class PlayerTrackObservation
    {
        public PlayerTrackObservation(
            long snapshotId,
            string? crossplatformId,
            string? name,
            float x,
            float y,
            float z,
            DateTimeOffset observedAtUtc)
        {
            SnapshotId = snapshotId;
            CrossplatformId = crossplatformId;
            Name = name;
            X = x;
            Y = y;
            Z = z;
            ObservedAtUtc = observedAtUtc;
        }

        public long SnapshotId { get; }

        public string? CrossplatformId { get; }

        public string? Name { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public DateTimeOffset ObservedAtUtc { get; }
    }

    public sealed class PlayerTrackHistory
    {
        public PlayerTrackHistory(
            IEnumerable<PlayerTrackObservation> observations,
            IEnumerable<PlayerHistoryGap> gaps)
        {
            if (observations == null) throw new ArgumentNullException(nameof(observations));
            if (gaps == null) throw new ArgumentNullException(nameof(gaps));

            var observationValues = observations.ToArray();
            var gapValues = gaps.ToArray();
            if (observationValues.Any(observation => observation == null))
                throw new ArgumentException("Track observations cannot contain null elements.", nameof(observations));
            if (gapValues.Any(gap => gap == null))
                throw new ArgumentException("Track gaps cannot contain null elements.", nameof(gaps));

            Observations = observationValues;
            Gaps = gapValues;
        }

        public IReadOnlyList<PlayerTrackObservation> Observations { get; }

        public IReadOnlyList<PlayerHistoryGap> Gaps { get; }
    }

    public sealed class GetPlayerTrackResult
    {
        public GetPlayerTrackResult(IEnumerable<PlayerTrackSegment> segments)
        {
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            var values = segments.ToArray();
            if (values.Any(segment => segment == null))
                throw new ArgumentException("Track results cannot contain null segments.", nameof(segments));

            Segments = values;
            ObservationCount = Segments.Sum(segment => segment.Points.Count);
        }

        public IReadOnlyList<PlayerTrackSegment> Segments { get; }

        public int ObservationCount { get; }
    }

    public sealed class PlayerTrackLimitExceededException : InvalidOperationException
    {
        public PlayerTrackLimitExceededException()
            : base("The player track query exceeded its retained history limit.")
        {
        }
    }

    public sealed class GetPlayerTrackUseCase
    {
        private readonly IPlayerHistoryStore store;

        public GetPlayerTrackUseCase(IPlayerHistoryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public GetPlayerTrackResult? Execute(GetPlayerTrackQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var history = store.GetPlayerTrack(query);
            if (history == null) return null;
            if (history.Observations.Count > GetPlayerTrackQuery.MaximumObservations)
                throw new PlayerTrackLimitExceededException();
            if (history.Gaps.Count > GetPlayerTrackQuery.MaximumContinuityGaps)
                throw new PlayerTrackLimitExceededException();

            var ordered = history.Observations
                .OrderBy(observation => observation.ObservedAtUtc)
                .ThenBy(observation => observation.SnapshotId)
                .ToArray();
            var orderedGaps = history.Gaps
                .OrderBy(gap => gap.StartedAtUtc)
                .ThenBy(gap => gap.CompletedAtUtc)
                .ThenBy(gap => gap.GapId, StringComparer.Ordinal)
                .ToArray();
            var segments = new List<PlayerTrackSegment>();
            var current = new List<PlayerTrackPoint>();
            PlayerTrackObservation? previous = null;
            var gapIndex = 0;

            foreach (var observation in ordered)
            {
                if (!IsValid(observation, query.CrossplatformId))
                {
                    CompleteSegment(current, segments);
                    previous = null;
                    continue;
                }

                if (previous != null &&
                    (HasIntersectingGap(
                         previous.ObservedAtUtc,
                         observation.ObservedAtUtc,
                         orderedGaps,
                         ref gapIndex) ||
                     HasNonIncreasingKey(previous, observation)))
                {
                    CompleteSegment(current, segments);
                }

                current.Add(new PlayerTrackPoint(
                    observation.SnapshotId,
                    observation.Name!,
                    observation.X,
                    observation.Y,
                    observation.Z,
                    observation.ObservedAtUtc));
                previous = observation;
            }

            CompleteSegment(current, segments);
            return new GetPlayerTrackResult(segments);
        }

        private static bool IsValid(PlayerTrackObservation observation, string crossplatformId) =>
            observation.SnapshotId > 0 &&
            string.Equals(observation.CrossplatformId, crossplatformId, StringComparison.Ordinal) &&
            observation.Name != null &&
            observation.ObservedAtUtc.Offset == TimeSpan.Zero &&
            IsFinite(observation.X) && IsFinite(observation.Y) && IsFinite(observation.Z);

        private static bool HasIntersectingGap(
            DateTimeOffset previousUtc,
            DateTimeOffset currentUtc,
            IReadOnlyList<PlayerHistoryGap> gaps,
            ref int gapIndex)
        {
            while (gapIndex < gaps.Count && gaps[gapIndex].CompletedAtUtc < previousUtc)
                gapIndex++;

            return gapIndex < gaps.Count && gaps[gapIndex].StartedAtUtc <= currentUtc;
        }

        private static bool HasNonIncreasingKey(
            PlayerTrackObservation previous,
            PlayerTrackObservation current) =>
            current.ObservedAtUtc < previous.ObservedAtUtc ||
            (current.ObservedAtUtc == previous.ObservedAtUtc && current.SnapshotId <= previous.SnapshotId);

        private static void CompleteSegment(
            List<PlayerTrackPoint> current,
            ICollection<PlayerTrackSegment> segments)
        {
            if (current.Count == 0) return;
            segments.Add(new PlayerTrackSegment(current));
            current.Clear();
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
