using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public readonly struct PlayerMapRectangle
    {
        public PlayerMapRectangle(double minimumX, double minimumZ, double maximumX, double maximumZ)
        {
            if (!IsFinite(minimumX)) throw new ArgumentOutOfRangeException(nameof(minimumX));
            if (!IsFinite(minimumZ)) throw new ArgumentOutOfRangeException(nameof(minimumZ));
            if (!IsFinite(maximumX)) throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (!IsFinite(maximumZ)) throw new ArgumentOutOfRangeException(nameof(maximumZ));
            if (maximumX <= minimumX) throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (maximumZ <= minimumZ) throw new ArgumentOutOfRangeException(nameof(maximumZ));

            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
        }

        public double MinimumX { get; }

        public double MinimumZ { get; }

        public double MaximumX { get; }

        public double MaximumZ { get; }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct PlayerMapCircle
    {
        public PlayerMapCircle(double centerX, double centerZ, double radius)
        {
            if (!IsFinite(centerX)) throw new ArgumentOutOfRangeException(nameof(centerX));
            if (!IsFinite(centerZ)) throw new ArgumentOutOfRangeException(nameof(centerZ));
            if (!IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (!IsFinite(centerX - radius) || !IsFinite(centerX + radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (!IsFinite(centerZ - radius) || !IsFinite(centerZ + radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            var radiusSquared = radius * radius;
            if (!IsFinite(radiusSquared)) throw new ArgumentOutOfRangeException(nameof(radius));

            CenterX = centerX;
            CenterZ = centerZ;
            Radius = radius;
            RadiusSquared = radiusSquared;
        }

        public double CenterX { get; }

        public double CenterZ { get; }

        public double Radius { get; }

        internal double RadiusSquared { get; }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class SearchPlayersInAreaRequest
    {
        public const int DefaultCandidateObservationLimit = 5000;
        public const int MaximumCandidateObservationLimit = 20000;
        public const int DefaultPlayerResultLimit = 250;
        public const int MaximumPlayerResultLimit = 1000;
        public const double CoordinateTolerance = 0.00001d;
        public static readonly TimeSpan MaximumRange = TimeSpan.FromDays(30);

        public SearchPlayersInAreaRequest(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            PlayerMapRectangle? rectangle,
            PlayerMapCircle? circle,
            int candidateObservationLimit = DefaultCandidateObservationLimit,
            int playerResultLimit = DefaultPlayerResultLimit)
        {
            FromUtc = HistoryPlayerValidation.RequireUtc(fromUtc, nameof(fromUtc));
            ToUtc = HistoryPlayerValidation.RequireUtc(toUtc, nameof(toUtc));
            if (ToUtc < FromUtc) throw new ArgumentOutOfRangeException(nameof(toUtc));
            if (ToUtc - FromUtc > MaximumRange) throw new ArgumentOutOfRangeException(nameof(toUtc));
            if (rectangle.HasValue == circle.HasValue)
                throw new ArgumentException("Exactly one rectangle or circle is required.");
            if (rectangle.HasValue)
            {
                var value = rectangle.Value;
                if (!IsFinite(value.MinimumX) || !IsFinite(value.MinimumZ) ||
                    !IsFinite(value.MaximumX) || !IsFinite(value.MaximumZ) ||
                    value.MaximumX <= value.MinimumX || value.MaximumZ <= value.MinimumZ)
                    throw new ArgumentOutOfRangeException(nameof(rectangle));
            }
            if (circle.HasValue)
            {
                var value = circle.Value;
                if (!IsFinite(value.CenterX) || !IsFinite(value.CenterZ) ||
                    !IsFinite(value.Radius) || value.Radius <= 0 ||
                    !IsFinite(value.CenterX - value.Radius) ||
                    !IsFinite(value.CenterX + value.Radius) ||
                    !IsFinite(value.CenterZ - value.Radius) ||
                    !IsFinite(value.CenterZ + value.Radius) ||
                    !IsFinite(value.CenterX - value.Radius - CoordinateTolerance) ||
                    !IsFinite(value.CenterX + value.Radius + CoordinateTolerance) ||
                    !IsFinite(value.CenterZ - value.Radius - CoordinateTolerance) ||
                    !IsFinite(value.CenterZ + value.Radius + CoordinateTolerance) ||
                    !IsFinite(value.Radius * value.Radius))
                    throw new ArgumentOutOfRangeException(nameof(circle));
            }
            if (candidateObservationLimit <= 0 || candidateObservationLimit > MaximumCandidateObservationLimit)
                throw new ArgumentOutOfRangeException(nameof(candidateObservationLimit));
            if (playerResultLimit <= 0 || playerResultLimit > MaximumPlayerResultLimit)
                throw new ArgumentOutOfRangeException(nameof(playerResultLimit));

            Rectangle = rectangle;
            Circle = circle;
            CandidateObservationLimit = candidateObservationLimit;
            PlayerResultLimit = playerResultLimit;
        }

        public DateTimeOffset FromUtc { get; }

        public DateTimeOffset ToUtc { get; }

        public PlayerMapRectangle? Rectangle { get; }

        public PlayerMapCircle? Circle { get; }

        public int CandidateObservationLimit { get; }

        public int PlayerResultLimit { get; }

        internal PlayerMapRectangle BoundingBox => Rectangle ?? new PlayerMapRectangle(
            Circle!.Value.CenterX - Circle.Value.Radius,
            Circle.Value.CenterZ - Circle.Value.Radius,
            Circle.Value.CenterX + Circle.Value.Radius,
            Circle.Value.CenterZ + Circle.Value.Radius);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class PlayerAreaCandidateQuery
    {
        public const int MaximumCandidateObservationLimit =
            SearchPlayersInAreaRequest.MaximumCandidateObservationLimit + 1;

        public PlayerAreaCandidateQuery(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            double minimumX,
            double minimumZ,
            double maximumX,
            double maximumZ,
            int candidateObservationLimit)
        {
            FromUtc = HistoryPlayerValidation.RequireUtc(fromUtc, nameof(fromUtc));
            ToUtc = HistoryPlayerValidation.RequireUtc(toUtc, nameof(toUtc));
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
            CandidateObservationLimit = candidateObservationLimit;
            Validate();
        }

        public DateTimeOffset FromUtc { get; }

        public DateTimeOffset ToUtc { get; }

        public double MinimumX { get; }

        public double MinimumZ { get; }

        public double MaximumX { get; }

        public double MaximumZ { get; }

        public int CandidateObservationLimit { get; }

        public void Validate()
        {
            HistoryPlayerValidation.RequireUtc(FromUtc, nameof(FromUtc));
            HistoryPlayerValidation.RequireUtc(ToUtc, nameof(ToUtc));
            if (ToUtc < FromUtc) throw new ArgumentOutOfRangeException(nameof(ToUtc));
            if (ToUtc - FromUtc > SearchPlayersInAreaRequest.MaximumRange)
                throw new ArgumentOutOfRangeException(nameof(ToUtc));
            if (!IsFinite(MinimumX)) throw new ArgumentOutOfRangeException(nameof(MinimumX));
            if (!IsFinite(MinimumZ)) throw new ArgumentOutOfRangeException(nameof(MinimumZ));
            if (!IsFinite(MaximumX) || MaximumX <= MinimumX)
                throw new ArgumentOutOfRangeException(nameof(MaximumX));
            if (!IsFinite(MaximumZ) || MaximumZ <= MinimumZ)
                throw new ArgumentOutOfRangeException(nameof(MaximumZ));
            if (CandidateObservationLimit <= 0 ||
                CandidateObservationLimit > MaximumCandidateObservationLimit)
                throw new ArgumentOutOfRangeException(nameof(CandidateObservationLimit));
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class PlayerAreaObservationCandidate
    {
        public PlayerAreaObservationCandidate(
            long snapshotId,
            string crossplatformId,
            string displayName,
            DateTimeOffset observedAtUtc,
            double x,
            double y,
            double z)
        {
            SnapshotId = snapshotId;
            CrossplatformId = crossplatformId;
            DisplayName = displayName;
            ObservedAtUtc = observedAtUtc;
            X = x;
            Y = y;
            Z = z;
        }

        public long SnapshotId { get; }

        public string CrossplatformId { get; }

        public string DisplayName { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }

    public interface IPlayerMapSpatialQueryStore
    {
        IReadOnlyList<PlayerAreaObservationCandidate> GetPlayerAreaCandidates(PlayerAreaCandidateQuery query);
    }

    public readonly struct PlayerMapPosition
    {
        public PlayerMapPosition(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }
    }

    /// <summary>
    /// Describes retained observations that hit the requested area. It does not imply continuous presence or dwell time.
    /// </summary>
    public sealed class PlayerAreaRetainedObservationHit
    {
        internal PlayerAreaRetainedObservationHit(
            string crossplatformId,
            string displayName,
            DateTimeOffset firstHitUtc,
            DateTimeOffset lastHitUtc,
            int hitObservationCount,
            PlayerMapPosition lastPosition,
            long lastSnapshotId)
        {
            CrossplatformId = crossplatformId;
            DisplayName = displayName;
            FirstHitUtc = firstHitUtc;
            LastHitUtc = lastHitUtc;
            HitObservationCount = hitObservationCount;
            LastPosition = lastPosition;
            LastSnapshotId = lastSnapshotId;
        }

        public string CrossplatformId { get; }

        public string DisplayName { get; }

        public DateTimeOffset FirstHitUtc { get; }

        public DateTimeOffset LastHitUtc { get; }

        public int HitObservationCount { get; }

        public PlayerMapPosition LastPosition { get; }

        internal long LastSnapshotId { get; }
    }

    public sealed class SearchPlayersInAreaResult
    {
        internal SearchPlayersInAreaResult(
            IEnumerable<PlayerAreaRetainedObservationHit> hits,
            int candidateObservationCount,
            int matchingObservationCount,
            bool candidateObservationLimitReached,
            bool playerResultLimitReached)
        {
            Hits = hits.ToArray();
            CandidateObservationCount = candidateObservationCount;
            MatchingObservationCount = matchingObservationCount;
            CandidateObservationLimitReached = candidateObservationLimitReached;
            PlayerResultLimitReached = playerResultLimitReached;
        }

        public IReadOnlyList<PlayerAreaRetainedObservationHit> Hits { get; }

        public int CandidateObservationCount { get; }

        public int MatchingObservationCount { get; }

        public bool CandidateObservationLimitReached { get; }

        public bool PlayerResultLimitReached { get; }
    }

    public sealed class SearchPlayersInAreaUseCase
    {
        private readonly IPlayerMapSpatialQueryStore store;

        public SearchPlayersInAreaUseCase(IPlayerMapSpatialQueryStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public SearchPlayersInAreaResult Execute(SearchPlayersInAreaRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var bounds = request.BoundingBox;
            var tolerance = request.Circle.HasValue
                ? SearchPlayersInAreaRequest.CoordinateTolerance
                : 0d;
            var storedCandidates = store.GetPlayerAreaCandidates(new PlayerAreaCandidateQuery(
                request.FromUtc,
                request.ToUtc,
                bounds.MinimumX - tolerance,
                bounds.MinimumZ - tolerance,
                bounds.MaximumX + tolerance,
                bounds.MaximumZ + tolerance,
                request.CandidateObservationLimit + 1));
            var candidateLimitReached = storedCandidates.Count > request.CandidateObservationLimit;
            var candidates = storedCandidates.Take(request.CandidateObservationLimit).ToArray();
            var matching = candidates.Where(candidate => IsInside(candidate, request)).ToArray();
            var allHits = matching
                .GroupBy(candidate => candidate.CrossplatformId, StringComparer.Ordinal)
                .Select(ToHit)
                .OrderByDescending(hit => hit.LastHitUtc)
                .ThenByDescending(hit => hit.LastSnapshotId)
                .ThenBy(hit => hit.CrossplatformId, StringComparer.Ordinal)
                .ToArray();

            return new SearchPlayersInAreaResult(
                allHits.Take(request.PlayerResultLimit),
                candidates.Length,
                matching.Length,
                candidateLimitReached,
                allHits.Length > request.PlayerResultLimit);
        }

        private static PlayerAreaRetainedObservationHit ToHit(
            IGrouping<string, PlayerAreaObservationCandidate> observations)
        {
            var ordered = observations
                .OrderBy(candidate => candidate.ObservedAtUtc)
                .ThenBy(candidate => candidate.SnapshotId)
                .ToArray();
            var last = ordered[ordered.Length - 1];
            return new PlayerAreaRetainedObservationHit(
                observations.Key,
                last.DisplayName,
                ordered[0].ObservedAtUtc,
                last.ObservedAtUtc,
                ordered.Length,
                new PlayerMapPosition(last.X, last.Y, last.Z),
                last.SnapshotId);
        }

        private static bool IsInside(
            PlayerAreaObservationCandidate candidate,
            SearchPlayersInAreaRequest request)
        {
            var bounds = request.BoundingBox;
            var tolerance = request.Circle.HasValue
                ? SearchPlayersInAreaRequest.CoordinateTolerance
                : 0d;
            if (candidate.X < bounds.MinimumX - tolerance ||
                candidate.X > bounds.MaximumX + tolerance ||
                candidate.Z < bounds.MinimumZ - tolerance ||
                candidate.Z > bounds.MaximumZ + tolerance)
                return false;
            if (!request.Circle.HasValue) return true;

            var circle = request.Circle.Value;
            var deltaX = candidate.X - circle.CenterX;
            var deltaZ = candidate.Z - circle.CenterZ;
            var toleratedRadius = circle.Radius + SearchPlayersInAreaRequest.CoordinateTolerance;
            return deltaX * deltaX + deltaZ * deltaZ <= toleratedRadius * toleratedRadius;
        }
    }
}
