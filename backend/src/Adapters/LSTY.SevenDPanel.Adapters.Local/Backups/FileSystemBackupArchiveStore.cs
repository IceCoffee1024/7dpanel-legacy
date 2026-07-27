using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed class FileSystemBackupArchiveStore : IBackupArchiveStorage
    {
        public const string SourceUnavailableError = "world_source_unavailable";
        public const string ZipFailedError = "backup_zip_failed";
        public const string ChecksumFailedError = "backup_checksum_failed";

        private readonly ApprovedStorageRoots roots;
        private readonly AtomicFileWriter atomicWriter;

        public FileSystemBackupArchiveStore(
            ApprovedStorageRoots roots,
            AtomicFileWriter atomicWriter)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            this.atomicWriter = atomicWriter ?? throw new ArgumentNullException(nameof(atomicWriter));
        }

        public string BackupRootId => roots.BackupRootId;
        public string GameVersion => roots.GameVersion;

        public Stream OpenRead(BackupArtifact artifact)
        {
            var path = ResolveManagedArtifact(artifact);
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public void Delete(BackupArtifact artifact)
        {
            var path = ResolveManagedArtifact(artifact);
            if (!File.Exists(path))
                throw new FileNotFoundException("The catalog-managed backup file does not exist.", path);
            File.Delete(path);
        }

        public void ValidateWorldSelection(string worldName)
        {
            roots.RequireCurrentWorldDirectory(worldName);
        }

        public WorldBackupArchive CreateWorldArchive(
            Guid sourceJobId,
            string worldName,
            DateTimeOffset createdAtUtc)
        {
            if (createdAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(createdAtUtc));

            string worldDirectory;
            try
            {
                worldDirectory = roots.RequireCurrentWorldDirectory(worldName);
            }
            catch (ArgumentException exception)
            {
                throw new BackupArchiveException(SourceUnavailableError, exception);
            }
            catch (InvalidOperationException exception)
            {
                throw new BackupArchiveException(SourceUnavailableError, exception);
            }

            if (!Directory.Exists(worldDirectory))
                throw new BackupArchiveException(SourceUnavailableError);

            var relativeResourceId = "world-" +
                createdAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                sourceJobId.ToString("N") + ".zip";
            try
            {
                return atomicWriter.Write(relativeResourceId, temporaryPath =>
                    BuildAndValidateArchive(
                        temporaryPath,
                        relativeResourceId,
                        sourceJobId,
                        worldName,
                        worldDirectory,
                        createdAtUtc));
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException)
            {
                throw new BackupArchiveException(ZipFailedError, exception);
            }
        }

        public CompletedBackup CreatePanelDatabaseArchive(
            Guid sourceJobId,
            string consistentDatabasePath,
            DateTimeOffset createdAtUtc)
        {
            if (string.IsNullOrWhiteSpace(consistentDatabasePath))
                throw new ArgumentException("A consistent database path is required.", nameof(consistentDatabasePath));
            if (!File.Exists(consistentDatabasePath))
                throw new BackupArchiveException(ZipFailedError);

            var relativeResourceId = "panel-database-" +
                createdAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                sourceJobId.ToString("N") + ".zip";
            return CreateFixedFilesArchive(
                sourceJobId,
                BackupKind.PanelDatabase,
                relativeResourceId,
                createdAtUtc,
                new[] { new SourceFile(consistentDatabasePath, "panel-database.sqlite") });
        }

        public CompletedBackup CreateServerConfigurationArchive(
            Guid sourceJobId,
            IReadOnlyCollection<string> approvedRelativeFiles,
            DateTimeOffset createdAtUtc)
        {
            if (approvedRelativeFiles == null)
                throw new ArgumentNullException(nameof(approvedRelativeFiles));

            SourceFile[] sourceFiles;
            try
            {
                sourceFiles = approvedRelativeFiles.Select(relativePath =>
                {
                    var normalized = roots.NormalizeServerConfigurationRelativePath(relativePath);
                    var fullPath = roots.ResolveServerConfigurationFile(normalized);
                    if (!File.Exists(fullPath))
                        throw new FileNotFoundException(
                            "A required server configuration file is missing.",
                            fullPath);
                    return new SourceFile(fullPath, normalized);
                }).ToArray();
            }
            catch (Exception exception)
            {
                throw new BackupArchiveException(
                    ServerConfigurationBackupJobHandler.SourceUnavailableError,
                    exception);
            }

            var relativeResourceId = "server-configuration-" +
                createdAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" +
                sourceJobId.ToString("N") + ".zip";
            return CreateFixedFilesArchive(
                sourceJobId,
                BackupKind.ServerConfiguration,
                relativeResourceId,
                createdAtUtc,
                sourceFiles);
        }

        public static void ValidateEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
                throw new ArgumentException("zip_entry_path_invalid", nameof(entryName));
            var normalized = entryName.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
                throw new ArgumentException("zip_entry_path_invalid", nameof(entryName));
            foreach (var segment in normalized.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    throw new ArgumentException("zip_entry_path_invalid", nameof(entryName));
            }
        }

        public static string ComputeSha256(string path)
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ComputeSha256(stream);
        }

        public static void VerifyChecksum(string path, string expectedSha256)
        {
            try
            {
                var actual = ComputeSha256(path);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new BackupArchiveException(ChecksumFailedError);
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new BackupArchiveException(ChecksumFailedError, exception);
            }
        }

        private string ResolveManagedArtifact(BackupArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (!string.Equals(
                artifact.BackupRootId,
                roots.BackupRootId,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("backup_root_not_approved");
            }
            if (string.IsNullOrWhiteSpace(artifact.RelativeResourceId) ||
                artifact.RelativeResourceId.IndexOf('/') >= 0 ||
                artifact.RelativeResourceId.IndexOf('\\') >= 0 ||
                artifact.RelativeResourceId.Contains(".."))
            {
                throw new InvalidOperationException("backup_resource_id_invalid");
            }
            return roots.ResolveBackupResource(artifact.RelativeResourceId);
        }

        private WorldBackupArchive BuildAndValidateArchive(
            string temporaryPath,
            string relativeResourceId,
            Guid sourceJobId,
            string worldName,
            string worldDirectory,
            DateTimeOffset createdAtUtc)
        {
            try
            {
                var sourceFiles = EnumerateSourceFiles(worldDirectory).ToArray();
                var manifestEntries = sourceFiles.Select(file => new BackupManifestEntry(
                    file.RelativePath,
                    new FileInfo(file.FullPath).Length,
                    ComputeSha256(file.FullPath))).ToArray();
                var manifest = new BackupManifest(
                    BackupManifest.CurrentVersion,
                    "World",
                    worldName,
                    roots.GameVersion,
                    createdAtUtc,
                    sourceJobId,
                    manifestEntries);

                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var source in sourceFiles)
                    {
                        var entry = zip.CreateEntry(source.RelativePath, CompressionLevel.Optimal);
                        using var input = File.Open(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var entryOutput = entry.Open();
                        input.CopyTo(entryOutput);
                    }

                    var manifestEntry = zip.CreateEntry(BackupManifest.EntryName, CompressionLevel.Optimal);
                    using var manifestOutput = manifestEntry.Open();
                    using var writer = new StreamWriter(manifestOutput, new UTF8Encoding(false));
                    writer.Write(manifest.ToJson());
                }

                ValidateArchive(temporaryPath, manifestEntries);
                var sha256 = ComputeSha256(temporaryPath);
                VerifyChecksum(temporaryPath, sha256);
                return new WorldBackupArchive(
                    sourceJobId,
                    relativeResourceId,
                    new FileInfo(temporaryPath).Length,
                    sha256,
                    manifestEntries.LongLength,
                    manifestEntries.Sum(entry => entry.SizeBytes),
                    BackupManifest.CurrentVersion);
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException || exception is ArgumentException)
            {
                throw new BackupArchiveException(ZipFailedError, exception);
            }
        }

        private CompletedBackup CreateFixedFilesArchive(
            Guid sourceJobId,
            BackupKind kind,
            string relativeResourceId,
            DateTimeOffset createdAtUtc,
            IReadOnlyCollection<SourceFile> sourceFiles)
        {
            if (createdAtUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(createdAtUtc));

            try
            {
                return atomicWriter.Write(relativeResourceId, temporaryPath =>
                    BuildAndValidateFixedFilesArchive(
                        temporaryPath,
                        relativeResourceId,
                        sourceJobId,
                        kind,
                        sourceFiles,
                        createdAtUtc));
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException || exception is ArgumentException)
            {
                throw new BackupArchiveException(ZipFailedError, exception);
            }
        }

        private CompletedBackup BuildAndValidateFixedFilesArchive(
            string temporaryPath,
            string relativeResourceId,
            Guid sourceJobId,
            BackupKind kind,
            IReadOnlyCollection<SourceFile> sourceFiles,
            DateTimeOffset createdAtUtc)
        {
            try
            {
                var manifestEntries = sourceFiles.Select(file => new BackupManifestEntry(
                    file.RelativePath,
                    new FileInfo(file.FullPath).Length,
                    ComputeSha256(file.FullPath))).ToArray();
                var manifest = new BackupManifest(
                    BackupManifest.CurrentVersion,
                    kind.ToString(),
                    string.Empty,
                    string.Empty,
                    createdAtUtc,
                    sourceJobId,
                    manifestEntries);

                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var source in sourceFiles)
                    {
                        ValidateEntryName(source.RelativePath);
                        var entry = zip.CreateEntry(source.RelativePath, CompressionLevel.Optimal);
                        using var input = File.Open(source.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var entryOutput = entry.Open();
                        input.CopyTo(entryOutput);
                    }

                    var manifestEntry = zip.CreateEntry(BackupManifest.EntryName, CompressionLevel.Optimal);
                    using var manifestOutput = manifestEntry.Open();
                    using var writer = new StreamWriter(manifestOutput, new UTF8Encoding(false));
                    writer.Write(manifest.ToJson());
                }

                ValidateArchive(temporaryPath, manifestEntries);
                var sha256 = ComputeSha256(temporaryPath);
                VerifyChecksum(temporaryPath, sha256);
                return new CompletedBackup(
                    sourceJobId,
                    kind,
                    roots.BackupRootId,
                    relativeResourceId,
                    new FileInfo(temporaryPath).Length,
                    sha256,
                    null,
                    null,
                    "Verified",
                    createdAtUtc,
                    sourceJobId,
                    BackupManifest.CurrentVersion);
            }
            catch (BackupArchiveException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException || exception is ArgumentException)
            {
                throw new BackupArchiveException(ZipFailedError, exception);
            }
        }

        private IEnumerable<SourceFile> EnumerateSourceFiles(string worldDirectory)
        {
            var pending = new Stack<string>();
            pending.Push(worldDirectory);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                roots.ValidateCurrentWorldPath(directory);
                foreach (var childDirectory in Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.Ordinal))
                {
                    roots.ValidateCurrentWorldPath(childDirectory);
                    pending.Push(childDirectory);
                }

                foreach (var file in Directory.GetFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
                {
                    roots.ValidateCurrentWorldPath(file);
                    var relative = file.Substring(worldDirectory.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    ValidateEntryName(relative);
                    if (string.Equals(relative, BackupManifest.EntryName, StringComparison.OrdinalIgnoreCase))
                        throw new BackupArchiveException(ZipFailedError);
                    yield return new SourceFile(file, relative);
                }
            }
        }

        private static void ValidateArchive(
            string archivePath,
            IReadOnlyCollection<BackupManifestEntry> manifestEntries)
        {
            using var stream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                ValidateEntryName(entry.FullName);
                var normalizedName = entry.FullName.Replace('\\', '/');
                if (entries.ContainsKey(normalizedName))
                    throw new BackupArchiveException(ChecksumFailedError);
                entries.Add(normalizedName, entry);
            }

            if (!entries.ContainsKey(BackupManifest.EntryName))
                throw new BackupArchiveException(ChecksumFailedError);
            if (entries.Count != manifestEntries.Count + 1)
                throw new BackupArchiveException(ChecksumFailedError);

            foreach (var expected in manifestEntries)
            {
                if (!entries.TryGetValue(expected.RelativePath, out var entry) ||
                    entry.Length != expected.SizeBytes)
                {
                    throw new BackupArchiveException(ChecksumFailedError);
                }
                using var content = entry.Open();
                var actual = ComputeSha256(content);
                if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new BackupArchiveException(ChecksumFailedError);
            }
        }

        private static string ComputeSha256(Stream stream)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private sealed record SourceFile(string FullPath, string RelativePath);
    }

    public sealed record WorldBackupArchive(
        Guid Id,
        string RelativeResourceId,
        long SizeBytes,
        string Sha256,
        long FileCount,
        long SourceBytes,
        int ManifestVersion);

    public sealed class BackupArchiveException : Exception
    {
        public BackupArchiveException(string errorCode)
            : base(errorCode)
        {
            ErrorCode = errorCode;
        }

        public BackupArchiveException(string errorCode, Exception innerException)
            : base(errorCode, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }
}
