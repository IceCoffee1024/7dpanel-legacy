using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public enum SevenDaysTransientEntityKind
    {
        Animal,
        Hostile
    }

    public readonly struct SevenDaysTransientEntityPosition
    {
        public SevenDaysTransientEntityPosition(float x, float y, float z)
        {
            ValidateFinite(x, nameof(x));
            ValidateFinite(y, nameof(y));
            ValidateFinite(z, nameof(z));
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        internal static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    public sealed class SevenDaysTransientEntity
    {
        internal SevenDaysTransientEntity(
            int entityId,
            string entityType,
            SevenDaysTransientEntityPosition position)
        {
            EntityId = entityId;
            EntityType = entityType;
            Position = position;
        }

        public int EntityId { get; }

        public string EntityType { get; }

        public SevenDaysTransientEntityPosition Position { get; }
    }

    public sealed class SevenDaysTransientEntityQuery
    {
        public const int MinimumZoom = 3;
        public const int MaximumResultLimit = 500;

        public SevenDaysTransientEntityQuery(
            SevenDaysTransientEntityKind kind,
            MapExtent extent,
            int zoom,
            int limit)
        {
            if (!Enum.IsDefined(typeof(SevenDaysTransientEntityKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ValidateExtent(extent);
            if (zoom < 0) throw new ArgumentOutOfRangeException(nameof(zoom));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            if (limit > MaximumResultLimit)
                throw new SevenDaysTransientEntityLimitExceededException(limit, null);
            Kind = kind;
            Extent = extent;
            Zoom = zoom;
            Limit = limit;
        }

        public SevenDaysTransientEntityKind Kind { get; }

        public MapExtent Extent { get; }

        public int Zoom { get; }

        public int Limit { get; }

        private static void ValidateExtent(MapExtent extent)
        {
            SevenDaysTransientEntityPosition.ValidateFinite(extent.MinimumX, nameof(extent));
            SevenDaysTransientEntityPosition.ValidateFinite(extent.MinimumZ, nameof(extent));
            SevenDaysTransientEntityPosition.ValidateFinite(extent.MaximumX, nameof(extent));
            SevenDaysTransientEntityPosition.ValidateFinite(extent.MaximumZ, nameof(extent));
            if (extent.MaximumX <= extent.MinimumX || extent.MaximumZ <= extent.MinimumZ)
                throw new ArgumentOutOfRangeException(nameof(extent));
        }
    }

    public sealed class SevenDaysTransientEntityLimitExceededException : InvalidOperationException
    {
        public const string StableMessage = "The transient entity result limit was exceeded.";

        internal SevenDaysTransientEntityLimitExceededException(int limit, int? matchedCount)
            : base(StableMessage)
        {
            Limit = limit;
            MatchedCount = matchedCount;
        }

        public int Limit { get; }

        public int? MatchedCount { get; }
    }

    public sealed class SevenDaysTransientEntitySnapshot
    {
        private SevenDaysTransientEntitySnapshot(
            AvailabilityState availability,
            SevenDaysTransientEntityKind kind,
            bool isZoomSufficient,
            DateTimeOffset? observedAtUtc,
            IEnumerable<SevenDaysTransientEntity>? entities)
        {
            Availability = availability;
            Kind = kind;
            IsZoomSufficient = isZoomSufficient;
            ObservedAtUtc = observedAtUtc;
            Entities = new ReadOnlyCollection<SevenDaysTransientEntity>(
                (entities ?? Enumerable.Empty<SevenDaysTransientEntity>()).ToArray());
        }

        public AvailabilityState Availability { get; }

        public SevenDaysTransientEntityKind Kind { get; }

        public bool IsZoomSufficient { get; }

        public DateTimeOffset? ObservedAtUtc { get; }

        public IReadOnlyList<SevenDaysTransientEntity> Entities { get; }

        internal static SevenDaysTransientEntitySnapshot Available(
            SevenDaysTransientEntityKind kind,
            DateTimeOffset observedAtUtc,
            IEnumerable<SevenDaysTransientEntity> entities,
            bool isZoomSufficient) =>
            new SevenDaysTransientEntitySnapshot(
                AvailabilityState.Available,
                kind,
                isZoomSufficient,
                observedAtUtc,
                isZoomSufficient ? entities : null);

        internal static SevenDaysTransientEntitySnapshot Stale(
            SevenDaysTransientEntityKind kind,
            DateTimeOffset observedAtUtc,
            IEnumerable<SevenDaysTransientEntity> entities,
            bool isZoomSufficient) =>
            new SevenDaysTransientEntitySnapshot(
                AvailabilityState.Stale,
                kind,
                isZoomSufficient,
                observedAtUtc,
                isZoomSufficient ? entities : null);

        internal static SevenDaysTransientEntitySnapshot Unavailable(
            SevenDaysTransientEntityKind kind) =>
            new SevenDaysTransientEntitySnapshot(
                AvailabilityState.Unavailable,
                kind,
                false,
                null,
                null);
    }
}
