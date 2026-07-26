using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class HistoricalPlayerLastRetainedLocation
    {
        public HistoricalPlayerLastRetainedLocation(
            long snapshotId,
            string crossplatformId,
            string displayName,
            MapLayerPosition position,
            DateTimeOffset observedAtUtc)
        {
            if (snapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotId));
            CrossplatformId = HistoryPlayerValidation.RequireCrossplatformId(
                crossplatformId,
                nameof(crossplatformId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("A display name is required.", nameof(displayName));
            SnapshotId = snapshotId;
            DisplayName = displayName;
            Position = position;
            ObservedAtUtc = HistoryPlayerValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
        }

        public long SnapshotId { get; }
        public string CrossplatformId { get; }
        public string DisplayName { get; }
        public MapLayerPosition Position { get; }
        public DateTimeOffset ObservedAtUtc { get; }
    }

    public sealed class HistoricalPlayerLastLocationsStoreQuery
    {
        public const int MaximumCandidateLimit = 2048;

        public HistoricalPlayerLastLocationsStoreQuery(MapExtent extent, int candidateLimit)
        {
            if (candidateLimit <= 0 || candidateLimit > MaximumCandidateLimit)
                throw new ArgumentOutOfRangeException(nameof(candidateLimit));
            Extent = extent;
            CandidateLimit = candidateLimit;
        }

        public MapExtent Extent { get; }
        public int CandidateLimit { get; }
    }

    public sealed class HistoricalPlayerLastLocationsRequest
    {
        public const int MinimumZoom = 1;

        public HistoricalPlayerLastLocationsRequest(MapExtent extent, int zoom, int limit)
        {
            if (zoom < 0) throw new ArgumentOutOfRangeException(nameof(zoom));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            if (limit > MapLayerQuery.MaximumResultLimit) throw new MapLayerLimitExceededException();
            Extent = extent;
            Zoom = zoom;
            Limit = limit;
        }

        public MapExtent Extent { get; }
        public int Zoom { get; }
        public int Limit { get; }
    }

    public sealed class HistoricalPlayerLastLocationsResult
    {
        private HistoricalPlayerLastLocationsResult(
            AvailabilityState availability,
            bool isZoomSufficient,
            IEnumerable<HistoricalPlayerLastRetainedLocation>? locations)
        {
            Availability = availability;
            IsZoomSufficient = isZoomSufficient;
            Locations = new ReadOnlyCollection<HistoricalPlayerLastRetainedLocation>(
                (locations ?? Enumerable.Empty<HistoricalPlayerLastRetainedLocation>()).ToArray());
        }

        public AvailabilityState Availability { get; }
        public bool IsZoomSufficient { get; }
        public IReadOnlyList<HistoricalPlayerLastRetainedLocation> Locations { get; }

        public static HistoricalPlayerLastLocationsResult Available(
            IEnumerable<HistoricalPlayerLastRetainedLocation> locations) =>
            new HistoricalPlayerLastLocationsResult(
                AvailabilityState.Available,
                true,
                locations ?? throw new ArgumentNullException(nameof(locations)));

        public static HistoricalPlayerLastLocationsResult ZoomTooLow() =>
            new HistoricalPlayerLastLocationsResult(
                AvailabilityState.Available,
                false,
                null);

        public static HistoricalPlayerLastLocationsResult Unavailable() =>
            new HistoricalPlayerLastLocationsResult(
                AvailabilityState.Unavailable,
                false,
                null);
    }

    public sealed class GetHistoricalPlayerLastLocationsUseCase
    {
        private readonly IPlayerHistoryStore store;
        private readonly IOnlinePlayerQuery onlinePlayers;

        public GetHistoricalPlayerLastLocationsUseCase(
            IPlayerHistoryStore store,
            IOnlinePlayerQuery onlinePlayers)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.onlinePlayers = onlinePlayers ?? throw new ArgumentNullException(nameof(onlinePlayers));
        }

        public async Task<HistoricalPlayerLastLocationsResult> ExecuteAsync(
            HistoricalPlayerLastLocationsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Zoom < HistoricalPlayerLastLocationsRequest.MinimumZoom)
                return HistoricalPlayerLastLocationsResult.ZoomTooLow();

            OnlinePlayersSnapshot onlineSnapshot;
            try
            {
                onlineSnapshot = await onlinePlayers.GetOnlineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return HistoricalPlayerLastLocationsResult.Unavailable();
            }

            var onlineIdentities = new HashSet<string>(
                onlineSnapshot.Players
                    .Select(player => player.CrossplatformIdentity?.CombinedId)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!),
                StringComparer.Ordinal);
            var candidateLimit = request.Limit + onlineIdentities.Count + 1;
            if (candidateLimit > HistoricalPlayerLastLocationsStoreQuery.MaximumCandidateLimit)
                throw new MapLayerLimitExceededException();

            var candidates = store.GetHistoricalPlayerLastRetainedLocations(
                new HistoricalPlayerLastLocationsStoreQuery(request.Extent, candidateLimit));
            var retained = candidates
                .Where(location => !onlineIdentities.Contains(location.CrossplatformId))
                .ToArray();
            if (retained.Length > request.Limit)
                throw new MapLayerLimitExceededException();

            return HistoricalPlayerLastLocationsResult.Available(retained);
        }
    }
}
