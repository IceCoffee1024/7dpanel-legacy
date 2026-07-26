using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public enum TransientEntityMapKind
    {
        Animals,
        Hostiles
    }

    public sealed class TransientEntityMapItem
    {
        public TransientEntityMapItem(
            int entityId,
            string entityType,
            MapLayerPosition position)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(entityType))
                throw new ArgumentException("An entity type is required.", nameof(entityType));
            EntityId = entityId;
            EntityType = entityType;
            Position = position;
        }

        public int EntityId { get; }
        public string EntityType { get; }
        public MapLayerPosition Position { get; }
    }

    public sealed class TransientEntityMapQuery
    {
        public const int MinimumZoom = 3;
        public const int MaximumResultLimit = 500;

        public TransientEntityMapQuery(
            TransientEntityMapKind kind,
            MapExtent extent,
            int zoom,
            int limit)
        {
            if (!Enum.IsDefined(typeof(TransientEntityMapKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (zoom < 0) throw new ArgumentOutOfRangeException(nameof(zoom));
            if (limit <= 0 || limit > MaximumResultLimit)
                throw new MapLayerLimitExceededException();
            Kind = kind;
            Extent = extent;
            Zoom = zoom;
            Limit = limit;
        }

        public TransientEntityMapKind Kind { get; }
        public MapExtent Extent { get; }
        public int Zoom { get; }
        public int Limit { get; }
    }

    public sealed class TransientEntityMapSnapshot
    {
        private TransientEntityMapSnapshot(
            AvailabilityState availability,
            TransientEntityMapKind kind,
            bool isZoomSufficient,
            DateTimeOffset? observedAtUtc,
            IEnumerable<TransientEntityMapItem>? items)
        {
            Availability = availability;
            Kind = kind;
            IsZoomSufficient = isZoomSufficient;
            ObservedAtUtc = observedAtUtc;
            Items = new ReadOnlyCollection<TransientEntityMapItem>(
                (items ?? Enumerable.Empty<TransientEntityMapItem>()).ToArray());
        }

        public AvailabilityState Availability { get; }
        public TransientEntityMapKind Kind { get; }
        public bool IsZoomSufficient { get; }
        public DateTimeOffset? ObservedAtUtc { get; }
        public IReadOnlyList<TransientEntityMapItem> Items { get; }

        public static TransientEntityMapSnapshot Available(
            TransientEntityMapKind kind,
            DateTimeOffset observedAtUtc,
            IEnumerable<TransientEntityMapItem> items,
            bool isZoomSufficient) =>
            Create(AvailabilityState.Available, kind, observedAtUtc, items, isZoomSufficient);

        public static TransientEntityMapSnapshot Stale(
            TransientEntityMapKind kind,
            DateTimeOffset observedAtUtc,
            IEnumerable<TransientEntityMapItem> items,
            bool isZoomSufficient) =>
            Create(AvailabilityState.Stale, kind, observedAtUtc, items, isZoomSufficient);

        public static TransientEntityMapSnapshot Unavailable(TransientEntityMapKind kind) =>
            new TransientEntityMapSnapshot(
                AvailabilityState.Unavailable,
                kind,
                false,
                null,
                null);

        private static TransientEntityMapSnapshot Create(
            AvailabilityState availability,
            TransientEntityMapKind kind,
            DateTimeOffset observedAtUtc,
            IEnumerable<TransientEntityMapItem> items,
            bool isZoomSufficient)
        {
            if (observedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
            if (items == null) throw new ArgumentNullException(nameof(items));
            return new TransientEntityMapSnapshot(
                availability,
                kind,
                isZoomSufficient,
                observedAtUtc,
                isZoomSufficient ? items : null);
        }
    }

    public interface ITransientEntityMapProjection
    {
        TransientEntityMapSnapshot Query(TransientEntityMapQuery query);
    }

    public sealed class GetTransientEntityMapLayerUseCase
    {
        private readonly ITransientEntityMapProjection projection;

        public GetTransientEntityMapLayerUseCase(ITransientEntityMapProjection projection)
        {
            this.projection = projection ?? throw new ArgumentNullException(nameof(projection));
        }

        public TransientEntityMapSnapshot Execute(TransientEntityMapQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return projection.Query(query);
        }
    }
}
