using System;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.SevenDays.Outbound.World
{
    public sealed class SevenDaysWorldToolCatalog : IWorldToolCatalog
    {
        private readonly object sync = new object();
        private WorldToolCatalogSnapshot snapshot = WorldToolCatalogSnapshot.Unavailable();

        public WorldToolCatalogSnapshot Read()
        {
            lock (sync) return snapshot;
        }

        internal void Publish(SevenDaysWorldScalarSnapshot sample, DateTimeOffset observedAtUtc)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (observedAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(observedAtUtc));
            lock (sync)
            {
                snapshot = !sample.WorldAvailable || sample.ToolCatalogCaptureFailed
                    ? WorldToolCatalogSnapshot.Unavailable()
                    : WorldToolCatalogSnapshot.Available(
                        SevenDaysWorldResourceId.CatalogVersion(sample),
                        observedAtUtc,
                        sample.BlockInternalNames,
                        sample.PrefabResourceIds,
                        sample.EntityTypeResourceIds);
            }
        }

        internal void MarkCaptureFailed()
        {
            lock (sync) snapshot = WorldToolCatalogSnapshot.Stale(snapshot);
        }

        internal void Clear()
        {
            lock (sync) snapshot = WorldToolCatalogSnapshot.Unavailable();
        }
    }
}
