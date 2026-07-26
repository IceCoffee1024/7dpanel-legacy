using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapGameTimeProjection : IMapGameTimeQuery
    {
        private readonly object sync = new object();
        private MapGameTimeProjectionSnapshot snapshot = MapGameTimeProjectionSnapshot.Unavailable();

        public MapGameTimeProjectionSnapshot Query()
        {
            lock (sync) return snapshot;
        }

        internal void Publish(SevenDaysMapSample sample, DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            lock (sync)
            {
                try
                {
                    var value = sample.GameTime;
                    snapshot = sample.GameTimeCaptureFailed
                        ? MapGameTimeProjectionSnapshot.Stale(snapshot)
                        : value == null
                            ? MapGameTimeProjectionSnapshot.Unavailable()
                            : MapGameTimeProjectionSnapshot.Available(new MapGameTime(
                                value.Day,
                                value.Hour,
                                value.Minute,
                                observedAtUtc));
                }
                catch
                {
                    snapshot = MapGameTimeProjectionSnapshot.Stale(snapshot);
                }
            }
        }

        internal void MarkCaptureFailed()
        {
            lock (sync) snapshot = MapGameTimeProjectionSnapshot.Stale(snapshot);
        }

        internal void Clear()
        {
            lock (sync) snapshot = MapGameTimeProjectionSnapshot.Unavailable();
        }
    }

    public sealed class SevenDaysMapGameTimeSample
    {
        public SevenDaysMapGameTimeSample(int day, int hour, int minute)
        {
            Day = day;
            Hour = hour;
            Minute = minute;
        }

        public int Day { get; }
        public int Hour { get; }
        public int Minute { get; }
    }
}
