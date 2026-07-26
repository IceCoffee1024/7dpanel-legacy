using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;

namespace LSTY.SevenDPanel.Adapters.Persistence.Sqlite.MapTiles
{
    public sealed class LocalMapTileRoot
    {
        public LocalMapTileRoot(string worldId, string rootPath, string? resourceVersion)
        {
            if (!MapTileKey.IsSafeWorldIdentifier(worldId))
                throw new ArgumentException("A safe world identifier is required.", nameof(worldId));
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("A server-controlled tile root is required.", nameof(rootPath));
            if (resourceVersion != null && string.IsNullOrWhiteSpace(resourceVersion))
                throw new ArgumentException("A resource version cannot be blank.", nameof(resourceVersion));

            WorldId = worldId;
            RootPath = rootPath;
            ResourceVersion = resourceVersion;
        }

        public string WorldId { get; }

        public string RootPath { get; }

        public string? ResourceVersion { get; }
    }

    public sealed class LocalMapTileStore : IMapTileStore
    {
        private static readonly string[] ApprovedExtensions = { ".png", ".webp" };
        private readonly Func<LocalMapTileRoot?> rootQuery;

        public LocalMapTileStore(Func<LocalMapTileRoot?> rootQuery)
        {
            this.rootQuery = rootQuery ?? throw new ArgumentNullException(nameof(rootQuery));
        }

        public Task<MapTileReadResult> ReadAsync(
            MapTileKey key,
            CancellationToken cancellationToken)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            cancellationToken.ThrowIfCancellationRequested();
            return Task.Run(() => ReadOnWorker(key, cancellationToken), cancellationToken);
        }

        private MapTileReadResult ReadOnWorker(
            MapTileKey key,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = rootQuery();
                if (source == null ||
                    !string.Equals(source.WorldId, key.WorldId, StringComparison.Ordinal))
                {
                    return MapTileReadResult.Unavailable();
                }

                var rootPath = Path.GetFullPath(source.RootPath);
                if (!Directory.Exists(rootPath))
                    return MapTileReadResult.Unavailable();

                foreach (var extension in ApprovedExtensions)
                {
                    var candidate = ResolveContainedPath(rootPath, key, extension);
                    if (candidate == null || !File.Exists(candidate))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();
                    var content = ReadAllBytes(candidate, cancellationToken);
                    var contentType = string.Equals(extension, ".png", StringComparison.Ordinal)
                        ? "image/png"
                        : "image/webp";
                    return MapTileReadResult.Available(
                        content,
                        contentType,
                        CreateContentETag(content),
                        source.ResourceVersion);
                }

                return MapTileReadResult.Missing();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is ArgumentException ||
                exception is NotSupportedException ||
                exception is CryptographicException)
            {
                return MapTileReadResult.Unavailable();
            }
        }

        private static string? ResolveContainedPath(
            string rootPath,
            MapTileKey key,
            string extension)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                rootPath,
                key.Zoom.ToString(CultureInfo.InvariantCulture),
                key.X.ToString(CultureInfo.InvariantCulture),
                key.Y.ToString(CultureInfo.InvariantCulture) + extension));
            var rootWithSeparator = rootPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return candidate.StartsWith(rootWithSeparator, comparison) ? candidate : null;
        }

        private static byte[] ReadAllBytes(string path, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.SequentialScan);
            if (stream.Length > int.MaxValue)
                throw new IOException("The map tile is too large.");

            var content = new byte[(int)stream.Length];
            var offset = 0;
            while (offset < content.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(content, offset, content.Length - offset);
                if (read == 0) throw new IOException("Unexpected end of map tile stream.");
                offset += read;
            }

            return content;
        }

        private static string CreateContentETag(byte[] content)
        {
            using var algorithm = SHA256.Create();
            var hash = algorithm.ComputeHash(content);
            return "\"" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() + "\"";
        }
    }
}
