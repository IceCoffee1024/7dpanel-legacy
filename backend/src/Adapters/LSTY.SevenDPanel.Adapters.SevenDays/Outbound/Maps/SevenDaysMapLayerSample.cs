using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapLayerSample
    {
        public static SevenDaysMapLayerSample Empty { get; } = new SevenDaysMapLayerSample(
            Array.Empty<SevenDaysTraderMapSample>(),
            Array.Empty<SevenDaysLandClaimMapSample>(),
            Array.Empty<SevenDaysVehicleMapSample>(),
            Array.Empty<SevenDaysDroneMapSample>());

        public SevenDaysMapLayerSample(
            IEnumerable<SevenDaysTraderMapSample> traders,
            IEnumerable<SevenDaysLandClaimMapSample> landClaims,
            IEnumerable<SevenDaysVehicleMapSample> vehicles,
            IEnumerable<SevenDaysDroneMapSample> drones)
        {
            Traders = Copy(traders, nameof(traders));
            LandClaims = Copy(landClaims, nameof(landClaims));
            Vehicles = Copy(vehicles, nameof(vehicles));
            Drones = Copy(drones, nameof(drones));
        }

        public IReadOnlyList<SevenDaysTraderMapSample> Traders { get; }
        public IReadOnlyList<SevenDaysLandClaimMapSample> LandClaims { get; }
        public IReadOnlyList<SevenDaysVehicleMapSample> Vehicles { get; }
        public IReadOnlyList<SevenDaysDroneMapSample> Drones { get; }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, string parameterName)
            where T : class
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            var copy = source.ToArray();
            if (copy.Any(item => item == null))
                throw new ArgumentException("Map layer samples cannot contain null items.", parameterName);
            return new ReadOnlyCollection<T>(copy);
        }
    }
}
