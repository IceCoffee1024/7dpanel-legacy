using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class MapGameTime
    {
        public MapGameTime(int day, int hour, int minute, DateTimeOffset observedAtUtc)
        {
            if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
            if (hour < 0 || hour > 23) throw new ArgumentOutOfRangeException(nameof(hour));
            if (minute < 0 || minute > 59) throw new ArgumentOutOfRangeException(nameof(minute));

            Day = day;
            Hour = hour;
            Minute = minute;
            ObservedAtUtc = HistoryPlayerValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
        }

        public int Day { get; }

        public int Hour { get; }

        public int Minute { get; }

        public DateTimeOffset ObservedAtUtc { get; }
    }
}
