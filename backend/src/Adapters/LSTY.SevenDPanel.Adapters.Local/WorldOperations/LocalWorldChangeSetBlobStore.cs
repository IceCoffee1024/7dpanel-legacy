using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using LSTY.SevenDPanel.Application.WorldOperations;

namespace LSTY.SevenDPanel.Adapters.Local.WorldOperations
{
    public sealed class LocalWorldChangeSetBlobStore : IWorldChangeSetBlobStore
    {
        private static readonly byte[] Magic =
        {
            (byte)'7', (byte)'D', (byte)'P', (byte)'W',
            (byte)'C', (byte)'S', (byte)'0', (byte)'1'
        };

        private readonly string rootPath;

        public LocalWorldChangeSetBlobStore(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathRooted(rootPath))
                throw new ArgumentException("world_change_set_root_must_be_absolute", nameof(rootPath));
            this.rootPath = NormalizeRoot(rootPath);
            Directory.CreateDirectory(this.rootPath);
            RejectReparsePoint(this.rootPath);
        }

        public static string CreateStorageResourceId() =>
            WorldChangeSetValidation.CreateStorageResourceId();

        public WorldChangeSetBlobReceipt Write(WorldChangeSetBlobDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var resourceId = RequireGeneratedResourceId(draft.StorageResourceId);
            var contentHash = ComputeHash(draft.Content);
            if (!string.Equals(contentHash, draft.ExpectedHash, StringComparison.Ordinal))
                throw new InvalidDataException("world_change_set_content_hash_mismatch");

            RejectReparsePoint(rootPath);
            var finalPath = ResolvePath(resourceId);
            if (File.Exists(finalPath))
                throw new IOException("world_change_set_resource_already_exists");

            var temporaryPath = Path.Combine(
                rootPath,
                "." + resourceId + "-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           81920,
                           FileOptions.WriteThrough))
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(Magic);
                    writer.Write(draft.Content.LongLength);
                    writer.Write(HashBytes(contentHash));
                    writer.Flush();
                    using (var compressed = new GZipStream(
                               stream,
                               CompressionLevel.Optimal,
                               leaveOpen: true))
                    {
                        compressed.Write(draft.Content, 0, draft.Content.Length);
                    }
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporaryPath, finalPath);
                return new WorldChangeSetBlobReceipt(
                    resourceId,
                    contentHash,
                    draft.Content.LongLength);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        public WorldChangeSetBlobReadResult Read(string storageResourceId, string expectedHash)
        {
            var resourceId = RequireGeneratedResourceId(storageResourceId);
            expectedHash = WorldChangeSetValidation.RequireHash(expectedHash, nameof(expectedHash));
            RejectReparsePoint(rootPath);
            var path = ResolvePath(resourceId);
            if (!File.Exists(path))
                throw new FileNotFoundException("world_change_set_resource_missing");
            RejectReparsePoint(path);

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                var magic = reader.ReadBytes(Magic.Length);
                if (!Equal(magic, Magic))
                    throw new InvalidDataException("world_change_set_header_invalid");
                var expectedLength = reader.ReadInt64();
                if (expectedLength < 0 || expectedLength > int.MaxValue)
                    throw new InvalidDataException("world_change_set_length_invalid");
                var storedHashBytes = reader.ReadBytes(32);
                if (storedHashBytes.Length != 32)
                    throw new InvalidDataException("world_change_set_header_invalid");
                var storedHash = ToHex(storedHashBytes);
                if (!string.Equals(storedHash, expectedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("world_change_set_expected_hash_mismatch");

                byte[] content;
                using (var compressed = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true))
                using (var output = new MemoryStream(checked((int)expectedLength)))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = compressed.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        total += read;
                        if (total > expectedLength)
                            throw new InvalidDataException("world_change_set_length_mismatch");
                        output.Write(buffer, 0, read);
                    }
                    if (total != expectedLength)
                        throw new InvalidDataException("world_change_set_length_mismatch");
                    content = output.ToArray();
                }

                var actualHash = ComputeHash(content);
                if (!string.Equals(actualHash, storedHash, StringComparison.Ordinal))
                    throw new InvalidDataException("world_change_set_content_hash_mismatch");
                return new WorldChangeSetBlobReadResult(resourceId, actualHash, content);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException("world_change_set_header_invalid", exception);
            }
        }

        private string ResolvePath(string resourceId)
        {
            var path = Path.GetFullPath(Path.Combine(rootPath, resourceId + ".wcs"));
            var prefix = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? rootPath
                : rootPath + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!path.StartsWith(prefix, comparison))
                throw new InvalidOperationException("world_change_set_path_outside_root");
            return path;
        }

        private static string RequireGeneratedResourceId(string value)
        {
            value = WorldChangeSetValidation.RequireResourceId(value, nameof(value));
            if (value.Length != 36 || !value.StartsWith("wcs-", StringComparison.Ordinal))
                throw new ArgumentException("world_change_set_resource_id_invalid", nameof(value));
            for (var index = 4; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') ||
                      (character >= 'a' && character <= 'f')))
                {
                    throw new ArgumentException("world_change_set_resource_id_invalid", nameof(value));
                }
            }
            return value;
        }

        private static string NormalizeRoot(string value)
        {
            var fullPath = Path.GetFullPath(value);
            var volumeRoot = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, volumeRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("world_change_set_reparse_not_allowed");
        }

        private static string ComputeHash(byte[] content)
        {
            using var sha256 = SHA256.Create();
            return ToHex(sha256.ComputeHash(content));
        }

        private static byte[] HashBytes(string hash)
        {
            var bytes = new byte[32];
            for (var index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(hash.Substring(index * 2, 2), 16);
            return bytes;
        }

        private static string ToHex(byte[] bytes)
        {
            var characters = new char[bytes.Length * 2];
            const string alphabet = "0123456789abcdef";
            for (var index = 0; index < bytes.Length; index++)
            {
                characters[index * 2] = alphabet[bytes[index] >> 4];
                characters[index * 2 + 1] = alphabet[bytes[index] & 0x0f];
            }
            return new string(characters);
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index]) return false;
            }
            return true;
        }
    }
}
