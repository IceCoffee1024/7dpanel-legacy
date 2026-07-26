using System;
using System.Collections.Generic;
using System.Linq;

namespace LSTY.SevenDPanel.Application
{
    public sealed class MapMetadata
    {
        public MapMetadata(
            string worldName,
            MapExtent extent,
            MapAxisConvention axes,
            IEnumerable<int> availableZoomLevels,
            int tileSizePixels,
            string? resourceVersion)
        {
            if (string.IsNullOrWhiteSpace(worldName))
                throw new ArgumentException("A world name is required.", nameof(worldName));
            if (availableZoomLevels == null)
                throw new ArgumentNullException(nameof(availableZoomLevels));
            if (axes == null)
                throw new ArgumentNullException(nameof(axes));
            if (tileSizePixels <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileSizePixels));
            var zoomLevels = availableZoomLevels.Distinct().OrderBy(value => value).ToArray();
            if (zoomLevels.Length == 0 || zoomLevels.Any(value => value < 0))
                throw new ArgumentOutOfRangeException(nameof(availableZoomLevels));

            WorldName = worldName;
            Extent = extent;
            Axes = axes;
            AvailableZoomLevels = zoomLevels;
            TileSizePixels = tileSizePixels;
            ResourceVersion = resourceVersion;
        }

        public string WorldName { get; }

        public MapExtent Extent { get; }

        public MapAxisConvention Axes { get; }

        public IReadOnlyList<int> AvailableZoomLevels { get; }

        public int TileSizePixels { get; }

        public string? ResourceVersion { get; }
    }

    public readonly struct MapExtent
    {
        public MapExtent(float minimumX, float minimumZ, float maximumX, float maximumZ)
        {
            if (!IsFinite(minimumX)) throw new ArgumentOutOfRangeException(nameof(minimumX));
            if (!IsFinite(minimumZ)) throw new ArgumentOutOfRangeException(nameof(minimumZ));
            if (!IsFinite(maximumX)) throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (!IsFinite(maximumZ)) throw new ArgumentOutOfRangeException(nameof(maximumZ));
            if (maximumX <= minimumX) throw new ArgumentOutOfRangeException(nameof(maximumX));
            if (maximumZ <= minimumZ) throw new ArgumentOutOfRangeException(nameof(maximumZ));

            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
        }

        public float MinimumX { get; }

        public float MinimumZ { get; }

        public float MaximumX { get; }

        public float MaximumZ { get; }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class MapAxisConvention
    {
        public MapAxisConvention(string xAxisDirection, string zAxisDirection)
        {
            if (string.IsNullOrWhiteSpace(xAxisDirection))
                throw new ArgumentException("An X-axis direction is required.", nameof(xAxisDirection));
            if (string.IsNullOrWhiteSpace(zAxisDirection))
                throw new ArgumentException("A Z-axis direction is required.", nameof(zAxisDirection));

            XAxisDirection = xAxisDirection;
            ZAxisDirection = zAxisDirection;
        }

        public string XAxisDirection { get; }

        public string ZAxisDirection { get; }
    }
}
