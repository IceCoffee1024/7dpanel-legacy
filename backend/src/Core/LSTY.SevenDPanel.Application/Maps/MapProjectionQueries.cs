using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class MapMetadataProjectionSnapshot
    {
        private MapMetadataProjectionSnapshot(
            AvailabilityState availability,
            string? worldId,
            MapMetadata? metadata,
            DateTimeOffset? observedAtUtc)
        {
            Availability = availability;
            WorldId = worldId;
            Metadata = metadata;
            ObservedAtUtc = observedAtUtc;
        }

        public AvailabilityState Availability { get; }
        public string? WorldId { get; }
        public MapMetadata? Metadata { get; }
        public DateTimeOffset? ObservedAtUtc { get; }

        public static MapMetadataProjectionSnapshot Available(
            string worldId,
            MapMetadata metadata,
            DateTimeOffset observedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("A world identifier is required.", nameof(worldId));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            return new MapMetadataProjectionSnapshot(
                AvailabilityState.Available,
                worldId,
                metadata,
                HistoryPlayerValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc)));
        }

        public static MapMetadataProjectionSnapshot Stale(
            MapMetadataProjectionSnapshot previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            if (previous.Metadata == null || previous.WorldId == null || !previous.ObservedAtUtc.HasValue)
                return Unavailable();
            return new MapMetadataProjectionSnapshot(
                AvailabilityState.Stale,
                previous.WorldId,
                previous.Metadata,
                previous.ObservedAtUtc);
        }

        public static MapMetadataProjectionSnapshot Unavailable() =>
            new MapMetadataProjectionSnapshot(
                AvailabilityState.Unavailable,
                null,
                null,
                null);
    }

    public sealed class MapGameTimeProjectionSnapshot
    {
        private MapGameTimeProjectionSnapshot(
            AvailabilityState availability,
            MapGameTime? gameTime)
        {
            Availability = availability;
            GameTime = gameTime;
        }

        public AvailabilityState Availability { get; }
        public MapGameTime? GameTime { get; }

        public static MapGameTimeProjectionSnapshot Available(MapGameTime gameTime) =>
            new MapGameTimeProjectionSnapshot(
                AvailabilityState.Available,
                gameTime ?? throw new ArgumentNullException(nameof(gameTime)));

        public static MapGameTimeProjectionSnapshot Stale(
            MapGameTimeProjectionSnapshot previous)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            return previous.GameTime == null
                ? Unavailable()
                : new MapGameTimeProjectionSnapshot(AvailabilityState.Stale, previous.GameTime);
        }

        public static MapGameTimeProjectionSnapshot Unavailable() =>
            new MapGameTimeProjectionSnapshot(AvailabilityState.Unavailable, null);
    }

    public interface IMapMetadataQuery
    {
        MapMetadataProjectionSnapshot Query();
    }

    public interface IMapGameTimeQuery
    {
        MapGameTimeProjectionSnapshot Query();
    }

    public sealed class GetMapMetadataUseCase
    {
        private readonly IMapMetadataQuery query;

        public GetMapMetadataUseCase(IMapMetadataQuery query)
        {
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public MapMetadataProjectionSnapshot Execute() => query.Query();
    }

    public sealed class GetMapGameTimeUseCase
    {
        private readonly IMapGameTimeQuery query;

        public GetMapGameTimeUseCase(IMapGameTimeQuery query)
        {
            this.query = query ?? throw new ArgumentNullException(nameof(query));
        }

        public MapGameTimeProjectionSnapshot Execute() => query.Query();
    }
}
