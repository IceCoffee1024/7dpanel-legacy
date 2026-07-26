using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class PlayerTrackPoint
    {
        public PlayerTrackPoint(
            long snapshotId,
            string name,
            float x,
            float y,
            float z,
            DateTimeOffset observedAtUtc)
        {
            if (snapshotId <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotId));
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (!IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
            if (!IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
            if (!IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(z));

            SnapshotId = snapshotId;
            Name = name;
            X = x;
            Y = y;
            Z = z;
            ObservedAtUtc = HistoryPlayerValidation.RequireUtc(observedAtUtc, nameof(observedAtUtc));
        }

        public long SnapshotId { get; }

        public string Name { get; }

        public float X { get; }

        public float Y { get; }

        public float Z { get; }

        public DateTimeOffset ObservedAtUtc { get; }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class PlayerTrackSegment
    {
        public PlayerTrackSegment(IEnumerable<PlayerTrackPoint> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            var values = points.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("A track segment requires at least one point.", nameof(points));
            if (values.Any(point => point == null))
                throw new ArgumentException("A track segment cannot contain null points.", nameof(points));

            Points = values;
        }

        public IReadOnlyList<PlayerTrackPoint> Points { get; }
    }
}
