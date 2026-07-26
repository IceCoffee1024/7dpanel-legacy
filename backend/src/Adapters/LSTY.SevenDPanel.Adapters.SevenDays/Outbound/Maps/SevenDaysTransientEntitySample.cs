using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysTransientEntitySample
    {
        public static SevenDaysTransientEntitySample Empty { get; } =
            new SevenDaysTransientEntitySample(
                Array.Empty<SevenDaysTransientEntitySampleItem>(),
                Array.Empty<SevenDaysTransientEntitySampleItem>());

        public SevenDaysTransientEntitySample(
            IEnumerable<SevenDaysTransientEntitySampleItem> animals,
            IEnumerable<SevenDaysTransientEntitySampleItem> hostiles)
        {
            Animals = Copy(animals, nameof(animals));
            Hostiles = Copy(hostiles, nameof(hostiles));
        }

        public IReadOnlyList<SevenDaysTransientEntitySampleItem> Animals { get; }

        public IReadOnlyList<SevenDaysTransientEntitySampleItem> Hostiles { get; }

        private static IReadOnlyList<SevenDaysTransientEntitySampleItem> Copy(
            IEnumerable<SevenDaysTransientEntitySampleItem> source,
            string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            var copy = source.ToArray();
            if (copy.Any(item => item == null))
                throw new ArgumentException("Transient entity samples cannot contain null items.", parameterName);
            return new ReadOnlyCollection<SevenDaysTransientEntitySampleItem>(copy);
        }
    }

    public sealed class SevenDaysTransientEntitySampleItem
    {
        public SevenDaysTransientEntitySampleItem(
            int entityId,
            string entityType,
            float x,
            float y,
            float z)
        {
            if (entityId < 0) throw new ArgumentOutOfRangeException(nameof(entityId));
            if (string.IsNullOrWhiteSpace(entityType))
                throw new ArgumentException("An entity type is required.", nameof(entityType));
            SevenDaysTransientEntityPosition.ValidateFinite(x, nameof(x));
            SevenDaysTransientEntityPosition.ValidateFinite(y, nameof(y));
            SevenDaysTransientEntityPosition.ValidateFinite(z, nameof(z));
            EntityId = entityId;
            EntityType = entityType;
            X = x;
            Y = y;
            Z = z;
        }

        public int EntityId { get; }

        public string EntityType { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }
    }
}
