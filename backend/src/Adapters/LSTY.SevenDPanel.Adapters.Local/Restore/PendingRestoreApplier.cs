using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Adapters.Local.Restore
{
    public sealed record RestoreArchiveLimits(
        int MaxEntries,
        long MaxEntryBytes,
        long MaxTotalBytes,
        double MaxCompressionRatio,
        long MaxManifestBytes)
    {
        public static RestoreArchiveLimits Default { get; } = new RestoreArchiveLimits(
            10000,
            8L * 1024 * 1024 * 1024,
            64L * 1024 * 1024 * 1024,
            1000,
            1024 * 1024);

        internal void Validate()
        {
            if (MaxEntries < 1 || MaxEntryBytes < 1 || MaxTotalBytes < 1 ||
                MaxCompressionRatio <= 0 || MaxManifestBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(RestoreArchiveLimits));
            }
        }
    }

    public sealed record RestoreApplyResult(
        Guid JobId,
        RestoreExecutionStage Stage,
        string? ErrorCode);

    public sealed class PendingRestoreApplier
    {
        public const string CatalogSnapshotMismatchError = "restore_catalog_snapshot_mismatch";
        public const string ArchiveMissingError = "restore_archive_missing";
        public const string ArchiveSizeMismatchError = "restore_archive_size_mismatch";
        public const string ArchiveChecksumMismatchError = "restore_archive_checksum_mismatch";
        public const string ArchiveManifestInvalidError = "restore_archive_manifest_invalid";
        public const string ArchiveEntryInvalidError = "restore_archive_entry_invalid";
        public const string ArchiveDuplicateTargetError = "restore_archive_duplicate_target";
        public const string ArchiveLimitExceededError = "restore_archive_limit_exceeded";
        public const string TargetMissingError = "restore_target_missing";
        public const string ReplaceFailedError = "restore_replace_failed";
        public const string RollbackFailedError = "restore_rollback_failed";
        public const string ApplyFailedError = "restore_apply_failed";

        private static readonly StringComparer TargetComparer = StringComparer.OrdinalIgnoreCase;

        private readonly ApprovedStorageRoots roots;
        private readonly IBackupCatalog catalog;
        private readonly JsonPendingRestoreStore store;
        private readonly WorldRestoreTimingGate worldTimingGate;
        private readonly string panelDatabaseFileName;
        private readonly RestoreArchiveLimits limits;

        public PendingRestoreApplier(
            ApprovedStorageRoots roots,
            IBackupCatalog catalog,
            JsonPendingRestoreStore store,
            WorldRestoreTimingGate worldTimingGate,
            string panelDatabaseFileName,
            RestoreArchiveLimits limits)
        {
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.worldTimingGate = worldTimingGate ?? throw new ArgumentNullException(nameof(worldTimingGate));
            RestoreStateValidation.RequireOpaque(panelDatabaseFileName, "panel_database_file_name_invalid");
            this.panelDatabaseFileName = panelDatabaseFileName;
            this.limits = limits ?? throw new ArgumentNullException(nameof(limits));
            limits.Validate();
        }

        public RestoreApplyResult? ApplyPending()
        {
            var marker = store.ReadMarker();
            if (marker == null) return null;

            var existingReceipt = store.ReadReceipt();
            if (existingReceipt != null)
            {
                if (!existingReceipt.HasSameIdentity(marker))
                    throw new RestoreStateException(JsonPendingRestoreStore.ReceiptConflictError);
                if (existingReceipt.Stage != RestoreExecutionStage.Prepared)
                {
                    store.DeleteMarker(marker.JobSnapshot.JobId);
                    return ResultFromExistingReceipt(existingReceipt);
                }
                return RecoverPrepared(marker);
            }

            if (marker.BackupKind == BackupKind.World &&
                !worldTimingGate.IsApproved(
                    roots.CurrentWorldName,
                    roots.CurrentWorldDirectory,
                    roots.GameVersion))
            {
                return CompleteWithoutMutation(marker, WorldRestoreTimingGate.UnverifiedError);
            }

            RestorePlan? plan = null;
            try
            {
                var artifact = ReadAndValidateArtifact(marker);
                var archivePath = ResolveAndValidateArchive(marker, artifact);
                var validated = ValidateArchive(archivePath, artifact, marker);
                plan = BuildPlan(marker, validated);
                PrepareStagingAndSafetyCopies(archivePath, plan);
                store.WriteReceipt(RestoreResultReceipt.FromMarker(
                    marker,
                    RestoreExecutionStage.Prepared));

                try
                {
                    foreach (var file in plan.Files)
                        File.Replace(file.StagingPath, file.TargetPath, null);
                }
                catch (Exception exception) when (
                    exception is IOException || exception is UnauthorizedAccessException)
                {
                    var rollback = TryRollback(plan);
                    if (rollback == RollbackOutcome.RetryableFailure)
                        throw new RestoreStateException(RollbackFailedError, exception);
                    var rolledBack = rollback == RollbackOutcome.Succeeded;
                    var stage = rolledBack
                        ? RestoreExecutionStage.RolledBack
                        : RestoreExecutionStage.RollbackFailed;
                    store.WriteReceipt(RestoreResultReceipt.FromMarker(marker, stage));
                    if (rolledBack) Cleanup(plan);
                    store.DeleteMarker(marker.JobSnapshot.JobId);
                    return new RestoreApplyResult(
                        marker.JobSnapshot.JobId,
                        stage,
                        rolledBack ? ReplaceFailedError : RollbackFailedError);
                }

                store.WriteReceipt(RestoreResultReceipt.FromMarker(
                    marker,
                    RestoreExecutionStage.Applied));
                Cleanup(plan);
                store.DeleteMarker(marker.JobSnapshot.JobId);
                return new RestoreApplyResult(
                    marker.JobSnapshot.JobId,
                    RestoreExecutionStage.Applied,
                    null);
            }
            catch (RestoreApplyException exception)
            {
                if (plan != null) Cleanup(plan);
                return CompleteWithoutMutation(marker, exception.ErrorCode);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException ||
                exception is InvalidDataException || exception is ArgumentException ||
                exception is KeyNotFoundException)
            {
                if (plan != null) Cleanup(plan);
                return CompleteWithoutMutation(marker, ApplyFailedError);
            }
        }

        private RestoreApplyResult RecoverPrepared(PendingRestoreMarker marker)
        {
            try
            {
                var artifact = ReadAndValidateArtifact(marker);
                var archivePath = ResolveAndValidateArchive(marker, artifact);
                var validated = ValidateArchive(archivePath, artifact, marker);
                var plan = BuildPlan(marker, validated);
                var rollback = TryRollback(plan);
                if (rollback == RollbackOutcome.RetryableFailure)
                    throw new RestoreStateException(RollbackFailedError);
                if (rollback == RollbackOutcome.Succeeded)
                {
                    store.WriteReceipt(RestoreResultReceipt.FromMarker(
                        marker,
                        RestoreExecutionStage.RolledBack));
                    Cleanup(plan);
                    store.DeleteMarker(marker.JobSnapshot.JobId);
                    return new RestoreApplyResult(
                        marker.JobSnapshot.JobId,
                        RestoreExecutionStage.RolledBack,
                        ReplaceFailedError);
                }
            }
            catch (Exception exception) when (IsRetryableFileAccess(exception))
            {
                // Keep Prepared authoritative when recovery material cannot be inspected
                // because of transient file access. A later startup can retry safely.
                throw new RestoreStateException(RollbackFailedError, exception);
            }
            catch (Exception exception) when (
                exception is RestoreApplyException || exception is IOException ||
                exception is UnauthorizedAccessException || exception is InvalidDataException ||
                exception is ArgumentException || exception is KeyNotFoundException)
            {
                // The independent receipt remains the authority at the uncertain boundary.
            }

            store.WriteReceipt(RestoreResultReceipt.FromMarker(
                marker,
                RestoreExecutionStage.RollbackFailed));
            store.DeleteMarker(marker.JobSnapshot.JobId);
            return new RestoreApplyResult(
                marker.JobSnapshot.JobId,
                RestoreExecutionStage.RollbackFailed,
                RollbackFailedError);
        }

        private static bool IsRetryableFileAccess(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is IOException || current is UnauthorizedAccessException)
                    return true;
            }
            return false;
        }

        private BackupArtifact ReadAndValidateArtifact(PendingRestoreMarker marker)
        {
            BackupArtifact artifact;
            try
            {
                artifact = catalog.Get(marker.ArtifactId);
            }
            catch (Exception exception) when (
                exception is KeyNotFoundException || exception is InvalidOperationException)
            {
                throw new RestoreApplyException(CatalogSnapshotMismatchError, exception);
            }

            if (artifact.Id != marker.ArtifactId ||
                artifact.Kind != marker.BackupKind ||
                !string.Equals(artifact.BackupRootId, marker.BackupRootId, StringComparison.Ordinal) ||
                !string.Equals(artifact.BackupRootId, roots.BackupRootId, StringComparison.Ordinal) ||
                !string.Equals(artifact.RelativeResourceId, marker.RelativeResourceId, StringComparison.Ordinal) ||
                !string.Equals(artifact.Sha256, marker.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(artifact.ValidationStatus, "Verified", StringComparison.Ordinal) ||
                artifact.ManifestVersion != BackupManifest.CurrentVersion)
            {
                throw new RestoreApplyException(CatalogSnapshotMismatchError);
            }
            return artifact;
        }

        private string ResolveAndValidateArchive(
            PendingRestoreMarker marker,
            BackupArtifact artifact)
        {
            string archivePath;
            try
            {
                archivePath = roots.ResolveBackupResource(marker.RelativeResourceId);
            }
            catch (Exception exception) when (
                exception is ArgumentException || exception is InvalidOperationException)
            {
                throw new RestoreApplyException(CatalogSnapshotMismatchError, exception);
            }
            if (!File.Exists(archivePath))
                throw new RestoreApplyException(ArchiveMissingError);
            if (new FileInfo(archivePath).Length != artifact.SizeBytes)
                throw new RestoreApplyException(ArchiveSizeMismatchError);
            string actualSha256;
            try
            {
                actualSha256 = FileSystemBackupArchiveStore.ComputeSha256(archivePath);
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new RestoreApplyException(ArchiveChecksumMismatchError, exception);
            }
            if (!string.Equals(actualSha256, marker.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new RestoreApplyException(ArchiveChecksumMismatchError);
            return archivePath;
        }

        private ValidatedArchive ValidateArchive(
            string archivePath,
            BackupArtifact artifact,
            PendingRestoreMarker marker)
        {
            try
            {
                using var stream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                var entries = new Dictionary<string, ZipArchiveEntry>(TargetComparer);
                ZipArchiveEntry? manifestEntry = null;
                long totalBytes = 0;
                foreach (var entry in zip.Entries)
                {
                    var normalized = ValidateEntryName(entry.FullName);
                    if (entries.ContainsKey(normalized))
                        throw new RestoreApplyException(ArchiveDuplicateTargetError);
                    entries.Add(normalized, entry);
                    CheckCompressionRatio(entry);
                    if (string.Equals(normalized, BackupManifest.EntryName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (manifestEntry != null)
                            throw new RestoreApplyException(ArchiveDuplicateTargetError);
                        if (entry.Length > limits.MaxManifestBytes)
                            throw new RestoreApplyException(ArchiveLimitExceededError);
                        manifestEntry = entry;
                        continue;
                    }

                    if (entries.Count(item => !string.Equals(
                            item.Key,
                            BackupManifest.EntryName,
                            StringComparison.OrdinalIgnoreCase)) > limits.MaxEntries ||
                        entry.Length > limits.MaxEntryBytes)
                    {
                        throw new RestoreApplyException(ArchiveLimitExceededError);
                    }
                    try
                    {
                        totalBytes = checked(totalBytes + entry.Length);
                    }
                    catch (OverflowException exception)
                    {
                        throw new RestoreApplyException(ArchiveLimitExceededError, exception);
                    }
                    if (totalBytes > limits.MaxTotalBytes)
                        throw new RestoreApplyException(ArchiveLimitExceededError);
                }

                if (manifestEntry == null)
                    throw new RestoreApplyException(ArchiveManifestInvalidError);
                string manifestJson;
                using (var content = manifestEntry.Open())
                using (var reader = new StreamReader(
                    content,
                    new UTF8Encoding(false, true),
                    true,
                    4096,
                    leaveOpen: false))
                {
                    manifestJson = reader.ReadToEnd();
                }
                var manifest = ParseManifest(manifestJson);
                ValidateManifestHeader(manifest, artifact, marker);

                var dataEntries = entries
                    .Where(item => !string.Equals(
                        item.Key,
                        BackupManifest.EntryName,
                        StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(item => item.Key, item => item.Value, TargetComparer);
                if (dataEntries.Count != manifest.Files.Count)
                    throw new RestoreApplyException(ArchiveManifestInvalidError);
                var validatedFiles = new List<ValidatedFile>(manifest.Files.Count);
                var manifestTargets = new HashSet<string>(TargetComparer);
                foreach (var expected in manifest.Files)
                {
                    var normalized = ValidateEntryName(expected.RelativePath);
                    if (!manifestTargets.Add(normalized))
                        throw new RestoreApplyException(ArchiveDuplicateTargetError);
                    if (!dataEntries.TryGetValue(normalized, out var entry) ||
                        entry.Length != expected.SizeBytes)
                    {
                        throw new RestoreApplyException(ArchiveManifestInvalidError);
                    }
                    using var content = entry.Open();
                    if (!string.Equals(
                            ComputeSha256(content),
                            expected.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RestoreApplyException(ArchiveManifestInvalidError);
                    }
                    validatedFiles.Add(new ValidatedFile(normalized, expected.SizeBytes, expected.Sha256));
                }
                return new ValidatedArchive(validatedFiles);
            }
            catch (RestoreApplyException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException || exception is InvalidDataException ||
                exception is UnauthorizedAccessException || exception is FormatException ||
                exception is DecoderFallbackException)
            {
                throw new RestoreApplyException(ArchiveManifestInvalidError, exception);
            }
        }

        private RestorePlan BuildPlan(PendingRestoreMarker marker, ValidatedArchive archive)
        {
            string targetRoot;
            switch (marker.BackupKind)
            {
                case BackupKind.PanelDatabase:
                    if (archive.Files.Count != 1 ||
                        !string.Equals(
                            archive.Files[0].RelativePath,
                            "panel-database.sqlite",
                            StringComparison.Ordinal))
                    {
                        throw new RestoreApplyException(ArchiveManifestInvalidError);
                    }
                    targetRoot = roots.PanelStateRoot;
                    break;
                case BackupKind.ServerConfiguration:
                    targetRoot = roots.ServerConfigurationRoot;
                    break;
                case BackupKind.World:
                    targetRoot = roots.RequireCurrentWorldDirectory(roots.CurrentWorldName);
                    break;
                default:
                    throw new RestoreApplyException(CatalogSnapshotMismatchError);
            }

            var operationId = marker.JobSnapshot.JobId.ToString("N");
            var stagingRoot = Path.Combine(targetRoot, ".restore-" + operationId + ".staging");
            ValidateTargetPath(marker.BackupKind, stagingRoot, allowPanelState: true);
            var files = new List<RestoreFilePlan>(archive.Files.Count);
            foreach (var file in archive.Files)
            {
                var targetPath = marker.BackupKind switch
                {
                    BackupKind.PanelDatabase => Path.Combine(roots.PanelStateRoot, panelDatabaseFileName),
                    BackupKind.ServerConfiguration => roots.ResolveServerConfigurationFile(file.RelativePath),
                    BackupKind.World => Path.GetFullPath(Path.Combine(
                        targetRoot,
                        file.RelativePath.Replace('/', Path.DirectorySeparatorChar))),
                    _ => throw new RestoreApplyException(CatalogSnapshotMismatchError)
                };
                ValidateTargetPath(marker.BackupKind, targetPath, allowPanelState: true);
                var stagingPath = Path.GetFullPath(Path.Combine(
                    stagingRoot,
                    file.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                ValidateTargetPath(marker.BackupKind, stagingPath, allowPanelState: true);
                var targetDirectory = Path.GetDirectoryName(targetPath) ??
                    throw new RestoreApplyException(TargetMissingError);
                var safetyPath = Path.Combine(
                    targetDirectory,
                    ".restore-" + operationId + "-" + Path.GetFileName(targetPath) + ".safety");
                ValidateTargetPath(marker.BackupKind, safetyPath, allowPanelState: true);
                var rollbackPath = Path.Combine(
                    targetDirectory,
                    ".restore-" + operationId + "-" + Path.GetFileName(targetPath) + ".rollback");
                ValidateTargetPath(marker.BackupKind, rollbackPath, allowPanelState: true);
                files.Add(new RestoreFilePlan(
                    file.RelativePath,
                    file.Sha256,
                    targetPath,
                    stagingPath,
                    safetyPath,
                    rollbackPath));
            }
            return new RestorePlan(stagingRoot, files);
        }

        private void PrepareStagingAndSafetyCopies(string archivePath, RestorePlan plan)
        {
            if (Directory.Exists(plan.StagingRoot)) Directory.Delete(plan.StagingRoot, true);
            foreach (var file in plan.Files)
            {
                if (!File.Exists(file.TargetPath))
                    throw new RestoreApplyException(TargetMissingError);
                if (File.Exists(file.SafetyPath) || File.Exists(file.RollbackPath))
                    throw new RestoreApplyException(ApplyFailedError);
            }
            Directory.CreateDirectory(plan.StagingRoot);
            try
            {
                using (var stream = File.Open(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    var entries = zip.Entries.ToDictionary(
                        entry => entry.FullName.Replace('\\', '/'),
                        entry => entry,
                        TargetComparer);
                    foreach (var file in plan.Files)
                    {
                        var directory = Path.GetDirectoryName(file.StagingPath) ??
                            throw new RestoreApplyException(ApplyFailedError);
                        Directory.CreateDirectory(directory);
                        using var input = entries[file.RelativePath].Open();
                        using var output = File.Open(
                            file.StagingPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None);
                        input.CopyTo(output);
                    }
                }

                foreach (var file in plan.Files)
                {
                    if (!string.Equals(
                            FileSystemBackupArchiveStore.ComputeSha256(file.StagingPath),
                            file.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new RestoreApplyException(ArchiveManifestInvalidError);
                    }
                }
                foreach (var file in plan.Files)
                    File.Copy(file.TargetPath, file.SafetyPath, overwrite: false);
            }
            catch
            {
                Cleanup(plan);
                throw;
            }
        }

        private RollbackOutcome TryRollback(RestorePlan plan)
        {
            try
            {
                foreach (var file in plan.Files)
                {
                    var safetyExists = File.Exists(file.SafetyPath);
                    var stagingExists = File.Exists(file.StagingPath);
                    if (!safetyExists) return RollbackOutcome.RecoveryMaterialMissing;
                    if (stagingExists)
                    {
                        continue;
                    }

                    // Keep the authoritative safety copy until the terminal receipt is durable.
                    // A process interruption can then repeat this replacement safely.
                    File.Copy(file.SafetyPath, file.RollbackPath, overwrite: true);
                    if (File.Exists(file.TargetPath))
                    {
                        File.Replace(file.RollbackPath, file.TargetPath, null);
                    }
                    else
                    {
                        File.Move(file.RollbackPath, file.TargetPath);
                    }
                }
                return RollbackOutcome.Succeeded;
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
                return RollbackOutcome.RetryableFailure;
            }
        }

        private RestoreApplyResult CompleteWithoutMutation(
            PendingRestoreMarker marker,
            string errorCode)
        {
            store.WriteReceipt(RestoreResultReceipt.FromMarker(
                marker,
                RestoreExecutionStage.RolledBack));
            store.DeleteMarker(marker.JobSnapshot.JobId);
            return new RestoreApplyResult(
                marker.JobSnapshot.JobId,
                RestoreExecutionStage.RolledBack,
                errorCode);
        }

        private static RestoreApplyResult ResultFromExistingReceipt(RestoreResultReceipt receipt) =>
            new RestoreApplyResult(
                receipt.JobSnapshot.JobId,
                receipt.Stage,
                receipt.Stage switch
                {
                    RestoreExecutionStage.Applied => null,
                    RestoreExecutionStage.RolledBack => RestoreResultReconciler.ApplyFailedRolledBackError,
                    RestoreExecutionStage.RollbackFailed => RollbackFailedError,
                    _ => RestoreResultReconciler.ResultUnknownError
                });

        private void ValidateTargetPath(
            BackupKind kind,
            string fullPath,
            bool allowPanelState)
        {
            switch (kind)
            {
                case BackupKind.PanelDatabase when allowPanelState:
                    roots.ValidatePanelStatePath(fullPath);
                    break;
                case BackupKind.ServerConfiguration:
                {
                    var canonical = Path.GetFullPath(fullPath);
                    var prefix = roots.ServerConfigurationRoot + Path.DirectorySeparatorChar;
                    if (!canonical.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new RestoreApplyException(ArchiveEntryInvalidError);
                    break;
                }
                case BackupKind.World:
                    roots.ValidateCurrentWorldPath(fullPath);
                    break;
                default:
                    throw new RestoreApplyException(ArchiveEntryInvalidError);
            }
        }

        private static string ValidateEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName) ||
                Path.IsPathRooted(entryName) ||
                entryName.IndexOf('\\') >= 0)
            {
                throw new RestoreApplyException(ArchiveEntryInvalidError);
            }
            var normalized = entryName.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) || normalized.IndexOf(':') >= 0)
                throw new RestoreApplyException(ArchiveEntryInvalidError);
            foreach (var segment in normalized.Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                    throw new RestoreApplyException(ArchiveEntryInvalidError);
            }
            return normalized;
        }

        private void CheckCompressionRatio(ZipArchiveEntry entry)
        {
            if (entry.Length == 0) return;
            if (entry.CompressedLength <= 0 ||
                entry.Length / (double)entry.CompressedLength > limits.MaxCompressionRatio)
            {
                throw new RestoreApplyException(ArchiveLimitExceededError);
            }
        }

        private static ParsedManifest ParseManifest(string json)
        {
            try
            {
                var root = RestoreJsonCodec.ParseObject(json);
                RestoreJsonCodec.RequireProperties(
                    root,
                    "version", "kind", "worldId", "gameVersion",
                    "createdAtUtc", "sourceJobId", "files");
                var files = RestoreJsonCodec.ReadArray(root, "files")
                    .Select(item =>
                    {
                        var file = RestoreJsonCodec.RequireObject(item);
                        RestoreJsonCodec.RequireProperties(file, "path", "sizeBytes", "sha256");
                        return new ParsedManifestFile(
                            RestoreJsonCodec.ReadString(file, "path"),
                            RestoreJsonCodec.ReadInt64(file, "sizeBytes"),
                            RestoreJsonCodec.ReadString(file, "sha256"));
                    })
                    .ToArray();
                return new ParsedManifest(
                    RestoreJsonCodec.ReadInt32(root, "version"),
                    RestoreJsonCodec.ReadString(root, "kind"),
                    RestoreJsonCodec.ReadString(root, "worldId"),
                    RestoreJsonCodec.ReadString(root, "gameVersion"),
                    RestoreJsonCodec.ParseUtc(RestoreJsonCodec.ReadString(root, "createdAtUtc")),
                    RestoreJsonCodec.ParseGuid(RestoreJsonCodec.ReadString(root, "sourceJobId")),
                    files);
            }
            catch (RestoreApplyException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is FormatException || exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                throw new RestoreApplyException(ArchiveManifestInvalidError, exception);
            }
        }

        private static void ValidateManifestHeader(
            ParsedManifest manifest,
            BackupArtifact artifact,
            PendingRestoreMarker marker)
        {
            if (manifest.Version != BackupManifest.CurrentVersion ||
                manifest.Version != artifact.ManifestVersion ||
                !string.Equals(manifest.Kind, marker.BackupKind.ToString(), StringComparison.Ordinal) ||
                manifest.CreatedAtUtc != artifact.CreatedAtUtc ||
                manifest.SourceJobId != artifact.SourceJobId)
            {
                throw new RestoreApplyException(ArchiveManifestInvalidError);
            }
            if (marker.BackupKind == BackupKind.World)
            {
                if (!string.Equals(manifest.WorldId, artifact.WorldId, StringComparison.Ordinal) ||
                    !string.Equals(manifest.GameVersion, artifact.GameVersion, StringComparison.Ordinal))
                {
                    throw new RestoreApplyException(ArchiveManifestInvalidError);
                }
            }
            else if (manifest.WorldId.Length != 0 || manifest.GameVersion.Length != 0)
            {
                throw new RestoreApplyException(ArchiveManifestInvalidError);
            }
        }

        private static string ComputeSha256(Stream stream)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(stream).Select(value =>
                value.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static void Cleanup(RestorePlan plan)
        {
            foreach (var file in plan.Files)
            {
                try
                {
                    if (File.Exists(file.SafetyPath)) File.Delete(file.SafetyPath);
                    if (File.Exists(file.RollbackPath)) File.Delete(file.RollbackPath);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                if (Directory.Exists(plan.StagingRoot)) Directory.Delete(plan.StagingRoot, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed record ValidatedArchive(IReadOnlyList<ValidatedFile> Files);
        private sealed record ValidatedFile(string RelativePath, long SizeBytes, string Sha256);
        private sealed record RestorePlan(string StagingRoot, IReadOnlyList<RestoreFilePlan> Files);
        private sealed record RestoreFilePlan(
            string RelativePath,
            string Sha256,
            string TargetPath,
            string StagingPath,
            string SafetyPath,
            string RollbackPath);

        private enum RollbackOutcome
        {
            Succeeded,
            RetryableFailure,
            RecoveryMaterialMissing
        }

        private sealed record ParsedManifest(
            int Version,
            string Kind,
            string WorldId,
            string GameVersion,
            DateTimeOffset CreatedAtUtc,
            Guid SourceJobId,
            IReadOnlyList<ParsedManifestFile> Files);
        private sealed record ParsedManifestFile(string RelativePath, long SizeBytes, string Sha256);

        private sealed class RestoreApplyException : Exception
        {
            public RestoreApplyException(string errorCode)
                : base(errorCode) => ErrorCode = errorCode;

            public RestoreApplyException(string errorCode, Exception innerException)
                : base(errorCode, innerException) => ErrorCode = errorCode;

            public string ErrorCode { get; }
        }
    }
}
