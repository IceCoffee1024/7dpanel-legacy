using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed class WorldRegion
    {
        public const long MaximumOperationVolume = 1_000_000;

        public WorldRegion(WorldCoordinate first, WorldCoordinate second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            MinimumX = ToBlockCoordinate(Math.Min(first.X, second.X), nameof(first));
            MinimumY = ToBlockCoordinate(Math.Min(first.Y, second.Y), nameof(first));
            MinimumZ = ToBlockCoordinate(Math.Min(first.Z, second.Z), nameof(first));
            MaximumX = ToBlockCoordinate(Math.Max(first.X, second.X), nameof(second));
            MaximumY = ToBlockCoordinate(Math.Max(first.Y, second.Y), nameof(second));
            MaximumZ = ToBlockCoordinate(Math.Max(first.Z, second.Z), nameof(second));
            Volume = checked(
                ((long)MaximumX - MinimumX + 1) *
                ((long)MaximumY - MinimumY + 1) *
                ((long)MaximumZ - MinimumZ + 1));
            if (Volume > MaximumOperationVolume)
                throw new ArgumentOutOfRangeException(nameof(second));
            Minimum = new WorldCoordinate(MinimumX, MinimumY, MinimumZ);
            Maximum = new WorldCoordinate(MaximumX, MaximumY, MaximumZ);
        }

        public WorldCoordinate Minimum { get; }
        public WorldCoordinate Maximum { get; }
        public long Volume { get; }
        internal int MinimumX { get; }
        internal int MinimumY { get; }
        internal int MinimumZ { get; }
        internal int MaximumX { get; }
        internal int MaximumY { get; }
        internal int MaximumZ { get; }

        private static int ToBlockCoordinate(double value, string parameterName)
        {
            if (value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                throw new ArgumentOutOfRangeException(parameterName);
            return checked((int)value);
        }
    }

    public sealed class WorldChangeSetDraft
    {
        public WorldChangeSetDraft(
            string sourceOperationId,
            string worldId,
            string worldVersion,
            WorldRegion region,
            string beforeHash,
            string afterHash,
            string storageResourceId,
            DateTimeOffset createdAtUtc,
            DateTimeOffset expiresAtUtc)
        {
            SourceOperationId = MapWorldOperationValidation.RequireText(sourceOperationId, nameof(sourceOperationId));
            WorldId = MapWorldOperationValidation.RequireText(worldId, nameof(worldId));
            WorldVersion = MapWorldOperationValidation.RequireText(worldVersion, nameof(worldVersion));
            Region = region ?? throw new ArgumentNullException(nameof(region));
            BeforeHash = WorldChangeSetValidation.RequireHash(beforeHash, nameof(beforeHash));
            AfterHash = WorldChangeSetValidation.RequireHash(afterHash, nameof(afterHash));
            StorageResourceId = WorldChangeSetValidation.RequireResourceId(storageResourceId, nameof(storageResourceId));
            MapWorldOperationValidation.RequireUtc(createdAtUtc, nameof(createdAtUtc));
            MapWorldOperationValidation.RequireUtc(expiresAtUtc, nameof(expiresAtUtc));
            if (expiresAtUtc <= createdAtUtc) throw new ArgumentOutOfRangeException(nameof(expiresAtUtc));
            CreatedAtUtc = createdAtUtc;
            ExpiresAtUtc = expiresAtUtc;
        }

        public string SourceOperationId { get; }
        public string WorldId { get; }
        public string WorldVersion { get; }
        public WorldRegion Region { get; }
        public string BeforeHash { get; }
        public string AfterHash { get; }
        public string StorageResourceId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
    }

    public sealed record WorldChangeSetDescriptor(
        string ChangeSetId,
        string SourceOperationId,
        string WorldId,
        string WorldVersion,
        WorldRegion Region,
        string BeforeHash,
        string AfterHash,
        string StorageResourceId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc);

    public sealed class WorldChangeSetBlobDraft
    {
        public WorldChangeSetBlobDraft(string storageResourceId, string expectedHash, byte[] content)
        {
            StorageResourceId = WorldChangeSetValidation.RequireResourceId(
                storageResourceId,
                nameof(storageResourceId));
            ExpectedHash = WorldChangeSetValidation.RequireHash(expectedHash, nameof(expectedHash));
            if (content == null) throw new ArgumentNullException(nameof(content));
            Content = (byte[])content.Clone();
        }

        public string StorageResourceId { get; }
        public string ExpectedHash { get; }
        public byte[] Content { get; }
    }

    public sealed record WorldChangeSetBlobReceipt(
        string StorageResourceId,
        string ContentHash,
        long ByteCount);

    public sealed class WorldChangeSetBlobReadResult
    {
        public WorldChangeSetBlobReadResult(string storageResourceId, string contentHash, byte[] content)
        {
            StorageResourceId = WorldChangeSetValidation.RequireResourceId(
                storageResourceId,
                nameof(storageResourceId));
            ContentHash = WorldChangeSetValidation.RequireHash(contentHash, nameof(contentHash));
            Content = content == null
                ? throw new ArgumentNullException(nameof(content))
                : (byte[])content.Clone();
        }

        public string StorageResourceId { get; }
        public string ContentHash { get; }
        public byte[] Content { get; }
    }

    public static class WorldChangeSetValidation
    {
        public static string CreateStorageResourceId() =>
            "wcs-" + Guid.NewGuid().ToString("N");

        public static string RequireHash(string value, string parameterName)
        {
            value = MapWorldOperationValidation.RequireText(value, parameterName);
            if (value.Length != 64)
                throw new ArgumentException("A SHA-256 hash is required.", parameterName);
            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    throw new ArgumentException("A SHA-256 hash is required.", parameterName);
                }
            }
            return value.ToLowerInvariant();
        }

        public static string RequireResourceId(string value, string parameterName)
        {
            value = MapWorldOperationValidation.RequireText(value, parameterName);
            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf("..", StringComparison.Ordinal) >= 0)
                throw new ArgumentException("An opaque storage resource ID is required.", parameterName);
            return value;
        }
    }
}
