using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LSTY.SevenDPanel.Application
{
    public sealed class MapTileKey
    {
        public MapTileKey(string worldId, int zoom, int x, int y)
        {
            if (!IsSafeWorldIdentifier(worldId))
                throw new ArgumentException("A safe world identifier is required.", nameof(worldId));
            if (zoom < 0) throw new ArgumentOutOfRangeException(nameof(zoom));

            WorldId = worldId;
            Zoom = zoom;
            X = x;
            Y = y;
        }

        public string WorldId { get; }

        public int Zoom { get; }

        public int X { get; }

        public int Y { get; }

        public static bool IsSafeWorldIdentifier(string? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value) || value.Length > 128)
                return false;

            foreach (var character in value)
            {
                if ((character >= 'a' && character <= 'z') ||
                    (character >= 'A' && character <= 'Z') ||
                    (character >= '0' && character <= '9') ||
                    character == '-' || character == '_')
                {
                    continue;
                }

                return false;
            }

            return true;
        }
    }

    public enum MapTileReadStatus
    {
        Available,
        Missing,
        Unavailable
    }

    public sealed class MapTileReadResult
    {
        private MapTileReadResult(
            MapTileReadStatus status,
            byte[]? content,
            string? contentType,
            string? etag,
            string? resourceVersion)
        {
            Status = status;
            Content = content;
            ContentType = contentType;
            ETag = etag;
            ResourceVersion = resourceVersion;
        }

        public MapTileReadStatus Status { get; }

        public byte[]? Content { get; }

        public string? ContentType { get; }

        public string? ETag { get; }

        public string? ResourceVersion { get; }

        public static MapTileReadResult Available(
            byte[] content,
            string contentType,
            string etag,
            string? resourceVersion)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.Length == 0)
                throw new ArgumentException("Tile content cannot be empty.", nameof(content));
            if (!string.Equals(contentType, "image/png", StringComparison.Ordinal) &&
                !string.Equals(contentType, "image/webp", StringComparison.Ordinal))
            {
                throw new ArgumentException("The tile content type is not approved.", nameof(contentType));
            }
            if (string.IsNullOrWhiteSpace(etag) ||
                etag.Length < 2 ||
                etag[0] != '"' ||
                etag[etag.Length - 1] != '"' ||
                etag.IndexOf('\r') >= 0 ||
                etag.IndexOf('\n') >= 0)
            {
                throw new ArgumentException("A quoted entity tag is required.", nameof(etag));
            }
            if (resourceVersion != null && string.IsNullOrWhiteSpace(resourceVersion))
                throw new ArgumentException("A resource version cannot be blank.", nameof(resourceVersion));

            return new MapTileReadResult(
                MapTileReadStatus.Available,
                content,
                contentType,
                etag,
                resourceVersion);
        }

        public static MapTileReadResult Missing() =>
            new MapTileReadResult(MapTileReadStatus.Missing, null, null, null, null);

        public static MapTileReadResult Unavailable() =>
            new MapTileReadResult(MapTileReadStatus.Unavailable, null, null, null, null);
    }

    public interface IMapTileStore
    {
        Task<MapTileReadResult> ReadAsync(
            MapTileKey key,
            CancellationToken cancellationToken);
    }

    public sealed class GetMapTileUseCase
    {
        private readonly IMapMetadataQuery metadataQuery;
        private readonly IMapTileStore store;

        public GetMapTileUseCase(IMapMetadataQuery metadataQuery, IMapTileStore store)
        {
            this.metadataQuery = metadataQuery ?? throw new ArgumentNullException(nameof(metadataQuery));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public Task<MapTileReadResult> ExecuteAsync(
            MapTileKey key,
            CancellationToken cancellationToken)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = metadataQuery.Query();
            if (snapshot.Metadata == null || snapshot.WorldId == null)
                return Task.FromResult(MapTileReadResult.Unavailable());
            if (!string.Equals(snapshot.WorldId, key.WorldId, StringComparison.Ordinal))
                throw new ArgumentException("The world identifier is not the current map world.", nameof(key));

            ValidateGridCoordinate(snapshot.Metadata, key);
            return store.ReadAsync(key, cancellationToken);
        }

        private static void ValidateGridCoordinate(MapMetadata metadata, MapTileKey key)
        {
            if (!metadata.AvailableZoomLevels.Contains(key.Zoom))
                throw new ArgumentOutOfRangeException(nameof(key), "The zoom level is not available.");

            var maximumZoom = metadata.AvailableZoomLevels[metadata.AvailableZoomLevels.Count - 1];
            var zoomDifference = maximumZoom - key.Zoom;
            var tileSpan = metadata.TileSizePixels * Math.Pow(2d, zoomDifference);
            if (double.IsInfinity(tileSpan) || tileSpan <= 0d ||
                !Intersects(key.X, tileSpan, metadata.Extent.MinimumX, metadata.Extent.MaximumX))
            {
                throw new ArgumentOutOfRangeException(nameof(key), "The tile X coordinate is outside the map grid.");
            }
            if (!Intersects(key.Y, tileSpan, metadata.Extent.MinimumZ, metadata.Extent.MaximumZ))
                throw new ArgumentOutOfRangeException(nameof(key), "The tile Y coordinate is outside the map grid.");
        }

        private static bool Intersects(int coordinate, double tileSpan, double minimum, double maximum)
        {
            var tileMinimum = coordinate * tileSpan;
            var tileMaximum = tileMinimum + tileSpan;
            return tileMinimum < maximum && tileMaximum > minimum;
        }
    }
}
