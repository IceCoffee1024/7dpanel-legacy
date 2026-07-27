using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Local.MapTiles
{
    public sealed class LocalMapResourcePublication
    {
        internal LocalMapResourcePublication(
            string worldId,
            string rootPath,
            string mapResourceVersion,
            int tileSize)
        {
            WorldId = worldId;
            RootPath = rootPath;
            MapResourceVersion = mapResourceVersion;
            TileSize = tileSize;
        }

        public string WorldId { get; }

        public string RootPath { get; }

        public string MapResourceVersion { get; }

        public int TileSize { get; }
    }

    public sealed class LocalMapResourcePublishException : MapResourcePublishException
    {
        public LocalMapResourcePublishException(string errorCode)
            : base(errorCode) { }

        public LocalMapResourcePublishException(string errorCode, Exception innerException)
            : base(errorCode, innerException) { }
    }

    public sealed class LocalMapResourcePublisher : IMapResourcePublisher
    {
        public const string ManifestFileName = "manifest.json";
        public const string ManifestInvalid = "map_manifest_invalid";
        public const string PathInvalid = "map_path_invalid";
        public const string TileInvalid = "map_tile_invalid";
        public const string PublishFailed = "map_publish_failed";

        private const int MaximumManifestBytes = 1024 * 1024;
        private const int MaximumTileCount = 250000;
        private const long MaximumTileBytes = 64L * 1024L * 1024L;
        private static readonly byte[] PngSignature =
        {
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a
        };

        private readonly string approvedTemporaryRoot;
        private readonly string publishedRoot;
        private readonly object publishGate = new object();
        private LocalMapResourcePublication? current;

        public LocalMapResourcePublisher(
            string approvedTemporaryRoot,
            string publishedRoot)
        {
            this.approvedTemporaryRoot = PrepareRoot(
                approvedTemporaryRoot,
                nameof(approvedTemporaryRoot));
            this.publishedRoot = PrepareRoot(publishedRoot, nameof(publishedRoot));
            if (!string.Equals(
                    Path.GetPathRoot(this.approvedTemporaryRoot),
                    Path.GetPathRoot(this.publishedRoot),
                    PathComparison))
            {
                throw new ArgumentException(
                    "map_roots_must_share_volume",
                    nameof(publishedRoot));
            }
        }

        public LocalMapResourcePublication? Current
        {
            get
            {
                lock (publishGate) return current;
            }
        }

        public LocalMapResourcePublication Publish(
            string expectedWorldId,
            string stagedRoot)
        {
            expectedWorldId = RequireWorldId(expectedWorldId, nameof(expectedWorldId));
            lock (publishGate)
            {
                try
                {
                    RejectReparsePoints(approvedTemporaryRoot, approvedTemporaryRoot);
                    RejectReparsePoints(publishedRoot, publishedRoot);
                    var stage = RequireStagedRoot(stagedRoot);
                    var manifest = ReadManifest(stage);
                    ValidateManifest(stage, expectedWorldId, manifest);

                    var version = "map-" + Guid.NewGuid().ToString("N");
                    var destination = Path.GetFullPath(Path.Combine(publishedRoot, version));
                    EnsureContained(publishedRoot, destination, allowRoot: false);
                    if (Directory.Exists(destination) || File.Exists(destination))
                        throw new IOException("map_publish_target_exists");

                    var publication = new LocalMapResourcePublication(
                        expectedWorldId,
                        destination,
                        version,
                        manifest.TileSize);
                    Directory.Move(stage, destination);
                    current = publication;
                    return publication;
                }
                catch (LocalMapResourcePublishException)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is IOException ||
                    exception is UnauthorizedAccessException ||
                    exception is SecurityException ||
                    exception is ArgumentException ||
                    exception is NotSupportedException)
                {
                    throw new LocalMapResourcePublishException(PublishFailed, exception);
                }
            }
        }

        MapResourcePublication IMapResourcePublisher.Publish(
            string expectedWorldId,
            string stagedRoot)
        {
            var publication = Publish(expectedWorldId, stagedRoot);
            return new MapResourcePublication(
                publication.WorldId,
                publication.MapResourceVersion,
                publication.TileSize);
        }

        private string RequireStagedRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new LocalMapResourcePublishException(PathInvalid);
            string path;
            try
            {
                path = NormalizeRoot(value);
                EnsureContained(approvedTemporaryRoot, path, allowRoot: false);
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is NotSupportedException)
            {
                throw new LocalMapResourcePublishException(PathInvalid, exception);
            }
            if (!Directory.Exists(path))
                throw new LocalMapResourcePublishException(PathInvalid);
            RejectReparsePoints(approvedTemporaryRoot, path);
            return path;
        }

        private static ManifestDocument ReadManifest(string stageRoot)
        {
            var path = Path.Combine(stageRoot, ManifestFileName);
            RejectReparsePoints(stageRoot, path);
            if (!File.Exists(path))
                throw new LocalMapResourcePublishException(ManifestInvalid);
            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumManifestBytes)
                    throw new LocalMapResourcePublishException(ManifestInvalid);
                string json;
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                using (var reader = new StreamReader(
                           stream,
                           new UTF8Encoding(false, true),
                           detectEncodingFromByteOrderMarks: false))
                {
                    json = reader.ReadToEnd();
                }
                var document = new JavaScriptSerializer
                {
                    MaxJsonLength = MaximumManifestBytes,
                    RecursionLimit = 16
                }.Deserialize<ManifestDocument>(json);
                return document ?? throw new LocalMapResourcePublishException(ManifestInvalid);
            }
            catch (LocalMapResourcePublishException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is DecoderFallbackException ||
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                throw new LocalMapResourcePublishException(ManifestInvalid, exception);
            }
        }

        private static void ValidateManifest(
            string stageRoot,
            string expectedWorldId,
            ManifestDocument manifest)
        {
            if (manifest.SchemaVersion != 1 ||
                !string.Equals(manifest.WorldId, expectedWorldId, StringComparison.Ordinal) ||
                manifest.TileSize <= 0 || manifest.TileSize > 4096 ||
                (manifest.TileSize & (manifest.TileSize - 1)) != 0 ||
                manifest.Tiles == null ||
                manifest.Tiles.Count == 0 ||
                manifest.Tiles.Count > MaximumTileCount)
            {
                throw new LocalMapResourcePublishException(ManifestInvalid);
            }

            var expectedFiles = new HashSet<string>(PathComparer)
            {
                ManifestFileName
            };
            foreach (var tile in manifest.Tiles)
            {
                ValidateTile(stageRoot, manifest.TileSize, tile, expectedFiles);
            }

            var actualFiles = EnumerateSafeFiles(stageRoot)
                .Select(path => RelativePath(stageRoot, path))
                .ToArray();
            if (actualFiles.Length != expectedFiles.Count ||
                actualFiles.Any(path => !expectedFiles.Contains(path)))
            {
                throw new LocalMapResourcePublishException(ManifestInvalid);
            }
        }

        private static void ValidateTile(
            string stageRoot,
            int expectedTileSize,
            ManifestTile tile,
            ISet<string> expectedFiles)
        {
            if (tile == null ||
                tile.Zoom < 0 || tile.Zoom > 30 ||
                string.IsNullOrWhiteSpace(tile.RelativePath) ||
                tile.SizeBytes <= 0 || tile.SizeBytes > MaximumTileBytes ||
                !IsSha256(tile.Sha256))
            {
                throw new LocalMapResourcePublishException(TileInvalid);
            }

            var extension = Path.GetExtension(tile.RelativePath);
            if (!string.Equals(extension, ".png", StringComparison.Ordinal) &&
                !string.Equals(extension, ".webp", StringComparison.Ordinal))
            {
                throw new LocalMapResourcePublishException(TileInvalid);
            }

            var canonical = tile.Zoom.ToString(CultureInfo.InvariantCulture) + "/" +
                            tile.X.ToString(CultureInfo.InvariantCulture) + "/" +
                            tile.Y.ToString(CultureInfo.InvariantCulture) + extension;
            if (!string.Equals(canonical, tile.RelativePath, StringComparison.Ordinal))
            {
                throw new LocalMapResourcePublishException(PathInvalid);
            }

            if (!expectedFiles.Add(canonical))
                throw new LocalMapResourcePublishException(ManifestInvalid);

            string path;
            try
            {
                path = Path.GetFullPath(Path.Combine(
                    stageRoot,
                    canonical.Replace('/', Path.DirectorySeparatorChar)));
                EnsureContained(stageRoot, path, allowRoot: false);
                RejectReparsePoints(stageRoot, path);
            }
            catch (LocalMapResourcePublishException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is NotSupportedException)
            {
                throw new LocalMapResourcePublishException(PathInvalid, exception);
            }

            if (!File.Exists(path))
                throw new LocalMapResourcePublishException(TileInvalid);
            try
            {
                var info = new FileInfo(path);
                if (info.Length != tile.SizeBytes)
                    throw new LocalMapResourcePublishException(TileInvalid);
                byte[] content;
                using (var stream = new FileStream(
                           path,
                           FileMode.Open,
                           FileAccess.Read,
                           FileShare.Read))
                {
                    content = ReadExactly(stream, checked((int)info.Length));
                }
                if (!string.Equals(Hash(content), tile.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    !HasExpectedDimensions(content, extension, expectedTileSize))
                {
                    throw new LocalMapResourcePublishException(TileInvalid);
                }
            }
            catch (LocalMapResourcePublishException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException ||
                exception is OverflowException)
            {
                throw new LocalMapResourcePublishException(TileInvalid, exception);
            }
        }

        private static IReadOnlyList<string> EnumerateSafeFiles(string root)
        {
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            try
            {
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    RejectReparsePoints(root, directory);
                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        RejectReparsePoints(root, child);
                        pending.Push(child);
                    }
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        RejectReparsePoints(root, file);
                        files.Add(file);
                    }
                }
                return files;
            }
            catch (LocalMapResourcePublishException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is SecurityException)
            {
                throw new LocalMapResourcePublishException(PathInvalid, exception);
            }
        }

        private static bool HasExpectedDimensions(
            byte[] content,
            string extension,
            int expectedSize)
        {
            if (string.Equals(extension, ".png", StringComparison.Ordinal))
                return TryPngDimensions(content, out var width, out var height) &&
                       width == expectedSize && height == expectedSize;
            return TryWebpDimensions(content, out var webpWidth, out var webpHeight) &&
                   webpWidth == expectedSize && webpHeight == expectedSize;
        }

        private static bool TryPngDimensions(byte[] content, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (content.Length < 45 ||
                !StartsWith(content, PngSignature) ||
                ReadBigEndianInt32(content, 8) != 13 ||
                !Matches(content, 12, "IHDR") ||
                !Matches(content, content.Length - 8, "IEND"))
            {
                return false;
            }
            width = ReadBigEndianInt32(content, 16);
            height = ReadBigEndianInt32(content, 20);
            return width > 0 && height > 0;
        }

        private static bool TryWebpDimensions(byte[] content, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (content.Length < 30 ||
                !Matches(content, 0, "RIFF") ||
                !Matches(content, 8, "WEBP") ||
                ReadLittleEndianUInt32(content, 4) + 8 != content.LongLength)
            {
                return false;
            }

            if (Matches(content, 12, "VP8X"))
            {
                width = 1 + ReadLittleEndian24(content, 24);
                height = 1 + ReadLittleEndian24(content, 27);
                return true;
            }
            if (Matches(content, 12, "VP8L") && content[20] == 0x2f)
            {
                width = 1 + content[21] + ((content[22] & 0x3f) << 8);
                height = 1 + (content[22] >> 6) + (content[23] << 2) +
                         ((content[24] & 0x0f) << 10);
                return true;
            }
            if (Matches(content, 12, "VP8 ") &&
                content[23] == 0x9d && content[24] == 0x01 && content[25] == 0x2a)
            {
                width = (content[26] | (content[27] << 8)) & 0x3fff;
                height = (content[28] | (content[29] << 8)) & 0x3fff;
                return width > 0 && height > 0;
            }
            return false;
        }

        private static byte[] ReadExactly(Stream stream, int length)
        {
            var content = new byte[length];
            var offset = 0;
            while (offset < content.Length)
            {
                var read = stream.Read(content, offset, content.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return content;
        }

        private static bool StartsWith(byte[] content, byte[] expected)
        {
            if (content.Length < expected.Length) return false;
            for (var index = 0; index < expected.Length; index++)
            {
                if (content[index] != expected[index]) return false;
            }
            return true;
        }

        private static bool Matches(byte[] content, int offset, string value)
        {
            if (offset < 0 || offset + value.Length > content.Length) return false;
            for (var index = 0; index < value.Length; index++)
            {
                if (content[offset + index] != (byte)value[index]) return false;
            }
            return true;
        }

        private static int ReadBigEndianInt32(byte[] content, int offset) =>
            (content[offset] << 24) |
            (content[offset + 1] << 16) |
            (content[offset + 2] << 8) |
            content[offset + 3];

        private static long ReadLittleEndianUInt32(byte[] content, int offset) =>
            (long)content[offset] |
            ((long)content[offset + 1] << 8) |
            ((long)content[offset + 2] << 16) |
            ((long)content[offset + 3] << 24);

        private static int ReadLittleEndian24(byte[] content, int offset) =>
            content[offset] | (content[offset + 1] << 8) | (content[offset + 2] << 16);

        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
            {
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f') ||
                      (character >= 'A' && character <= 'F')))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Hash(byte[] content)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(content)
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static string RequireWorldId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
                throw new ArgumentException("map_world_id_invalid", parameterName);
            foreach (var character in value)
            {
                if (!((character >= 'a' && character <= 'z') ||
                      (character >= 'A' && character <= 'Z') ||
                      (character >= '0' && character <= '9') ||
                      character == '-' || character == '_'))
                {
                    throw new ArgumentException("map_world_id_invalid", parameterName);
                }
            }
            return value;
        }

        private static string PrepareRoot(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
                throw new ArgumentException("map_root_must_be_absolute", parameterName);
            var root = NormalizeRoot(value);
            Directory.CreateDirectory(root);
            RejectReparsePoints(root, root);
            return root;
        }

        private static string RelativePath(string root, string path) =>
            path.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');

        private static void EnsureContained(
            string root,
            string path,
            bool allowRoot)
        {
            var normalizedRoot = NormalizeRoot(root);
            var normalizedPath = Path.GetFullPath(path);
            if (allowRoot && string.Equals(normalizedRoot, normalizedPath, PathComparison))
                return;
            var prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(prefix, PathComparison))
                throw new LocalMapResourcePublishException(PathInvalid);
        }

        private static void RejectReparsePoints(string root, string path)
        {
            var normalizedRoot = NormalizeRoot(root);
            var normalizedPath = Path.GetFullPath(path);
            EnsureContained(normalizedRoot, normalizedPath, allowRoot: true);
            CheckReparsePoint(normalizedRoot);
            if (string.Equals(normalizedRoot, normalizedPath, PathComparison)) return;
            var relative = normalizedPath.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = normalizedRoot;
            foreach (var segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                CheckReparsePoint(current);
            }
        }

        private static void CheckReparsePoint(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new LocalMapResourcePublishException(PathInvalid);
            }
        }

        private static string NormalizeRoot(string value)
        {
            var fullPath = Path.GetFullPath(value);
            var volumeRoot = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, volumeRoot, PathComparison)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static StringComparison PathComparison =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private sealed class ManifestDocument
        {
            public int SchemaVersion { get; set; }
            public string? WorldId { get; set; }
            public int TileSize { get; set; }
            public List<ManifestTile>? Tiles { get; set; }
        }

        private sealed class ManifestTile
        {
            public int Zoom { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public string? RelativePath { get; set; }
            public long SizeBytes { get; set; }
            public string? Sha256 { get; set; }
        }
    }
}
