using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysTransientEntityProjection : ITransientEntityMapProjection
    {
        private static readonly TimeSpan DefaultFreshLifetime = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan DefaultStaleLifetime = TimeSpan.FromSeconds(20);

        private readonly object sync = new object();
        private readonly TimeSpan freshLifetime;
        private readonly TimeSpan staleLifetime;
        private readonly Func<DateTimeOffset> utcNow;
        private PublishedSnapshot? published;

        public SevenDaysTransientEntityProjection()
            : this(
                DefaultFreshLifetime,
                DefaultStaleLifetime,
                () => DateTimeOffset.UtcNow)
        {
        }

        internal SevenDaysTransientEntityProjection(
            TimeSpan freshLifetime,
            TimeSpan staleLifetime,
            Func<DateTimeOffset> utcNow)
        {
            if (freshLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(freshLifetime));
            if (staleLifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(staleLifetime));
            this.freshLifetime = freshLifetime;
            this.staleLifetime = staleLifetime;
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public void Capture(
            SevenDaysTransientEntitySample sample,
            DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            RequireUtc(observedAtUtc, nameof(observedAtUtc));

            var animals = Copy(sample.Animals);
            var hostiles = Copy(sample.Hostiles);
            lock (sync)
            {
                published = new PublishedSnapshot(observedAtUtc, animals, hostiles);
            }
        }

        public SevenDaysTransientEntitySnapshot Query(SevenDaysTransientEntityQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            lock (sync)
            {
                if (published == null)
                    return SevenDaysTransientEntitySnapshot.Unavailable(query.Kind);

                var age = RequireUtc(utcNow(), "utcNow") - published.ObservedAtUtc;
                if (age > freshLifetime + staleLifetime)
                {
                    published = null;
                    return SevenDaysTransientEntitySnapshot.Unavailable(query.Kind);
                }

                var isZoomSufficient = query.Zoom >= SevenDaysTransientEntityQuery.MinimumZoom;
                var source = query.Kind == SevenDaysTransientEntityKind.Animal
                    ? published.Animals
                    : published.Hostiles;
                var matches = isZoomSufficient
                    ? source.Where(entity => Contains(query.Extent, entity.Position)).ToArray()
                    : Array.Empty<SevenDaysTransientEntity>();
                if (matches.Length > query.Limit)
                    throw new SevenDaysTransientEntityLimitExceededException(query.Limit, matches.Length);

                return age > freshLifetime
                    ? SevenDaysTransientEntitySnapshot.Stale(
                        query.Kind,
                        published.ObservedAtUtc,
                        matches,
                        isZoomSufficient)
                    : SevenDaysTransientEntitySnapshot.Available(
                        query.Kind,
                        published.ObservedAtUtc,
                        matches,
                        isZoomSufficient);
            }
        }

        public TransientEntityMapSnapshot Query(TransientEntityMapQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            SevenDaysTransientEntitySnapshot snapshot;
            try
            {
                snapshot = Query(new SevenDaysTransientEntityQuery(
                    query.Kind == TransientEntityMapKind.Animals
                        ? SevenDaysTransientEntityKind.Animal
                        : SevenDaysTransientEntityKind.Hostile,
                    query.Extent,
                    query.Zoom,
                    query.Limit));
            }
            catch (SevenDaysTransientEntityLimitExceededException)
            {
                throw new MapLayerLimitExceededException();
            }

            var kind = query.Kind;
            var items = snapshot.Entities.Select(entity => new TransientEntityMapItem(
                entity.EntityId,
                entity.EntityType,
                new MapLayerPosition(
                    entity.Position.X,
                    entity.Position.Y,
                    entity.Position.Z)));
            if (snapshot.Availability == AvailabilityState.Unavailable ||
                !snapshot.ObservedAtUtc.HasValue)
            {
                return TransientEntityMapSnapshot.Unavailable(kind);
            }

            return snapshot.Availability == AvailabilityState.Stale
                ? TransientEntityMapSnapshot.Stale(
                    kind,
                    snapshot.ObservedAtUtc.Value,
                    items,
                    snapshot.IsZoomSufficient)
                : TransientEntityMapSnapshot.Available(
                    kind,
                    snapshot.ObservedAtUtc.Value,
                    items,
                    snapshot.IsZoomSufficient);
        }

        public void Stop()
        {
            lock (sync) published = null;
        }

        private static SevenDaysTransientEntity[] Copy(
            IReadOnlyList<SevenDaysTransientEntitySampleItem> samples)
        {
            var copy = new SevenDaysTransientEntity[samples.Count];
            for (var index = 0; index < samples.Count; index++)
            {
                var sample = samples[index];
                copy[index] = new SevenDaysTransientEntity(
                    sample.EntityId,
                    sample.EntityType,
                    new SevenDaysTransientEntityPosition(sample.X, sample.Y, sample.Z));
            }
            return copy;
        }

        private static bool Contains(
            MapExtent extent,
            SevenDaysTransientEntityPosition position) =>
            position.X >= extent.MinimumX &&
            position.X <= extent.MaximumX &&
            position.Z >= extent.MinimumZ &&
            position.Z <= extent.MaximumZ;

        private static DateTimeOffset RequireUtc(DateTimeOffset value, string parameterName)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(parameterName);
            return value;
        }

        private sealed class PublishedSnapshot
        {
            public PublishedSnapshot(
                DateTimeOffset observedAtUtc,
                SevenDaysTransientEntity[] animals,
                SevenDaysTransientEntity[] hostiles)
            {
                ObservedAtUtc = observedAtUtc;
                Animals = animals;
                Hostiles = hostiles;
            }

            public DateTimeOffset ObservedAtUtc { get; }

            public SevenDaysTransientEntity[] Animals { get; }

            public SevenDaysTransientEntity[] Hostiles { get; }
        }
    }
}
