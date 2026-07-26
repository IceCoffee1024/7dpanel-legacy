using System;
using System.Linq;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Maps
{
    public sealed class SevenDaysMapMetadataProjection : IMapMetadataQuery
    {
        private readonly object sync = new object();
        private MapMetadataProjectionSnapshot snapshot = MapMetadataProjectionSnapshot.Unavailable();

        public MapMetadataProjectionSnapshot Query()
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
                    var value = sample.Metadata;
                    snapshot = sample.MetadataCaptureFailed
                        ? MapMetadataProjectionSnapshot.Stale(snapshot)
                        : value == null
                            ? MapMetadataProjectionSnapshot.Unavailable()
                            : MapMetadataProjectionSnapshot.Available(
                                value.WorldId,
                                new MapMetadata(
                                    value.WorldName,
                                    new MapExtent(value.MinimumX, value.MinimumZ, value.MaximumX, value.MaximumZ),
                                    new MapAxisConvention("east", "north"),
                                    Enumerable.Range(0, value.ZoomLevelCount),
                                    value.TileSizePixels,
                                    null),
                                observedAtUtc);
                }
                catch
                {
                    snapshot = MapMetadataProjectionSnapshot.Stale(snapshot);
                }
            }
        }

        internal void MarkCaptureFailed()
        {
            lock (sync) snapshot = MapMetadataProjectionSnapshot.Stale(snapshot);
        }

        internal void Clear()
        {
            lock (sync) snapshot = MapMetadataProjectionSnapshot.Unavailable();
        }
    }

    public sealed class SevenDaysMapMetadataSample
    {
        public SevenDaysMapMetadataSample(
            string worldName,
            string worldId,
            int minimumX,
            int minimumZ,
            int maximumX,
            int maximumZ,
            int tileSizePixels,
            int zoomLevelCount)
        {
            WorldName = worldName;
            WorldId = worldId;
            MinimumX = minimumX;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumZ = maximumZ;
            TileSizePixels = tileSizePixels;
            ZoomLevelCount = zoomLevelCount;
        }

        public string WorldName { get; }
        public string WorldId { get; }
        public int MinimumX { get; }
        public int MinimumZ { get; }
        public int MaximumX { get; }
        public int MaximumZ { get; }
        public int TileSizePixels { get; }
        public int ZoomLevelCount { get; }
    }
}
