using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Restore;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class PendingRestoreApplierTests
    {
        [Fact]
        public void Panel_database_restore_validates_stages_safety_copies_and_atomically_replaces_the_target()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") });

            var result = fixture.Applier.ApplyPending();

            Assert.NotNull(result);
            Assert.Equal(RestoreExecutionStage.Applied, result!.Stage);
            Assert.Null(result.ErrorCode);
            Assert.Equal("new-panel", File.ReadAllText(target));
            Assert.Null(fixture.Store.ReadMarker());
            Assert.Equal(RestoreExecutionStage.Applied, fixture.Store.ReadReceipt()!.Stage);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Panel, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(".restore-", StringComparison.Ordinal));
        }

        [Fact]
        public void Server_configuration_restore_replaces_each_manifest_file_under_the_approved_root()
        {
            using var directories = new TestDirectories();
            var first = Path.Combine(directories.Configuration, "serverconfig.xml");
            var nestedDirectory = Path.Combine(directories.Configuration, "admin");
            Directory.CreateDirectory(nestedDirectory);
            var second = Path.Combine(nestedDirectory, "permissions.xml");
            File.WriteAllText(first, "old-server");
            File.WriteAllText(second, "old-admin");
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[]
                {
                    new ArchiveFile("serverconfig.xml", "new-server"),
                    new ArchiveFile("admin/permissions.xml", "new-admin")
                });

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.Applied, result!.Stage);
            Assert.Equal("new-server", File.ReadAllText(first));
            Assert.Equal("new-admin", File.ReadAllText(second));
        }

        [Fact]
        public void World_restore_is_stably_rejected_without_persisted_pre_world_open_timing_evidence()
        {
            using var directories = new TestDirectories();
            var worldFile = Path.Combine(directories.World, "main.ttw");
            File.WriteAllText(worldFile, "live-world");
            var catalogRead = false;
            var fixture = CreateFixture(
                directories,
                BackupKind.World,
                new[] { new ArchiveFile("main.ttw", "archived-world") },
                onCatalogGet: () => catalogRead = true);

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(WorldRestoreTimingGate.UnverifiedError, result.ErrorCode);
            Assert.False(catalogRead);
            Assert.Equal("live-world", File.ReadAllText(worldFile));
            Assert.Equal(RestoreExecutionStage.RolledBack, fixture.Store.ReadReceipt()!.Stage);
        }

        [Theory]
        [InlineData("v3.0.1-b4")]
        [InlineData("v3.0.1-b5")]
        public void World_restore_timing_gate_never_infers_approval_from_a_version_string(
            string gameVersion)
        {
            var gate = new WorldRestoreTimingGate();

            Assert.False(gate.IsApproved(gameVersion));
        }

        [Theory]
        [InlineData("root")]
        [InlineData("resource")]
        [InlineData("kind")]
        [InlineData("validation")]
        public void Catalog_must_still_match_the_immutable_marker_snapshot(string mismatch)
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") },
                mutateCatalog: artifact => mismatch switch
                {
                    "root" => artifact with { BackupRootId = "secondary" },
                    "resource" => artifact with { RelativeResourceId = "different.zip" },
                    "kind" => artifact with { Kind = BackupKind.ServerConfiguration },
                    "validation" => artifact with { ValidationStatus = "Corrupt" },
                    _ => artifact
                });

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.CatalogSnapshotMismatchError, result.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
        }

        [Fact]
        public void Archive_size_and_sha256_are_revalidated_immediately_before_restore()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") });
            using (var stream = File.Open(fixture.ArchivePath, FileMode.Append, FileAccess.Write, FileShare.None))
                stream.WriteByte(0);

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ArchiveSizeMismatchError, result.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
        }

        [Fact]
        public void Occupied_archive_fails_closed_before_prepared_receipt_or_target_mutation()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") });
            using var archiveLock = File.Open(
                fixture.ArchivePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ArchiveChecksumMismatchError, result.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
            Assert.Null(fixture.Store.ReadMarker());
            Assert.Equal(RestoreExecutionStage.RolledBack, fixture.Store.ReadReceipt()!.Stage);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Panel, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(".restore-", StringComparison.Ordinal));
        }

        [Fact]
        public void Manifest_kind_and_file_hashes_are_revalidated()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") },
                manifestKind: "ServerConfiguration");

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(PendingRestoreApplier.ArchiveManifestInvalidError, result!.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
        }

        [Theory]
        [InlineData("../escape.xml")]
        [InlineData("/absolute.xml")]
        [InlineData("C:/absolute.xml")]
        [InlineData("nested\\escape.xml")]
        public void Zip_entries_with_non_canonical_or_unsafe_paths_are_rejected(string entryName)
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Configuration, "serverconfig.xml");
            File.WriteAllText(target, "old-server");
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[] { new ArchiveFile(entryName, "malicious") });

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(PendingRestoreApplier.ArchiveEntryInvalidError, result!.ErrorCode);
            Assert.Equal("old-server", File.ReadAllText(target));
            Assert.False(File.Exists(Path.Combine(directories.Root, "escape.xml")));
        }

        [Fact]
        public void Duplicate_normalized_targets_are_rejected()
        {
            using var directories = new TestDirectories();
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[]
                {
                    new ArchiveFile("serverconfig.xml", "first"),
                    new ArchiveFile("SERVERCONFIG.XML", "second")
                });

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(PendingRestoreApplier.ArchiveDuplicateTargetError, result!.ErrorCode);
        }

        [Theory]
        [InlineData("entries")]
        [InlineData("entryBytes")]
        [InlineData("totalBytes")]
        [InlineData("ratio")]
        public void Zip_resource_limits_are_enforced_before_extraction(string limit)
        {
            using var directories = new TestDirectories();
            var content = new string('x', 4096);
            var limits = limit switch
            {
                "entries" => new RestoreArchiveLimits(1, 8192, 16384, 1000, 1024 * 1024),
                "entryBytes" => new RestoreArchiveLimits(10, 10, 16384, 1000, 1024 * 1024),
                "totalBytes" => new RestoreArchiveLimits(10, 8192, 10, 1000, 1024 * 1024),
                "ratio" => new RestoreArchiveLimits(10, 8192, 16384, 2, 1024 * 1024),
                _ => throw new InvalidOperationException()
            };
            var files = limit == "entries"
                ? new[] { new ArchiveFile("one.xml", "1"), new ArchiveFile("two.xml", "2") }
                : new[] { new ArchiveFile("serverconfig.xml", content) };
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                files,
                limits: limits);

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(PendingRestoreApplier.ArchiveLimitExceededError, result!.ErrorCode);
        }

        [Fact]
        public void Replacement_failure_rolls_back_files_already_replaced_from_same_volume_safety_copies()
        {
            using var directories = new TestDirectories();
            var first = Path.Combine(directories.Configuration, "first.xml");
            var second = Path.Combine(directories.Configuration, "second.xml");
            File.WriteAllText(first, "old-first");
            File.WriteAllText(second, "old-second");
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[]
                {
                    new ArchiveFile("first.xml", "new-first"),
                    new ArchiveFile("second.xml", "new-second")
                });
            using var lockSecond = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read);

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ReplaceFailedError, result.ErrorCode);
            Assert.Equal("old-first", File.ReadAllText(first));
            lockSecond.Position = 0;
            using var reader = new StreamReader(lockSecond, Encoding.UTF8, true, 1024, leaveOpen: true);
            Assert.Equal("old-second", reader.ReadToEnd());
        }

        [Theory]
        [InlineData(RestoreExecutionStage.Applied)]
        [InlineData(RestoreExecutionStage.RolledBack)]
        [InlineData(RestoreExecutionStage.RollbackFailed)]
        public void Existing_terminal_receipt_never_reapplies_the_archive(RestoreExecutionStage stage)
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "external-current-value");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "must-not-apply") });
            fixture.Store.WriteReceipt(RestoreResultReceipt.FromMarker(fixture.Marker, stage));

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(stage, result!.Stage);
            Assert.Equal("external-current-value", File.ReadAllText(target));
            Assert.Null(fixture.Store.ReadMarker());
        }

        [Fact]
        public void Prepared_receipt_without_complete_recovery_material_becomes_rollback_failed_without_reapplying()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "uncertain-current-value");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "must-not-apply") });
            fixture.Store.WriteReceipt(RestoreResultReceipt.FromMarker(
                fixture.Marker,
                RestoreExecutionStage.Prepared));

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RollbackFailed, result!.Stage);
            Assert.Equal(PendingRestoreApplier.RollbackFailedError, result.ErrorCode);
            Assert.Equal("uncertain-current-value", File.ReadAllText(target));
            Assert.Equal(RestoreExecutionStage.RollbackFailed, fixture.Store.ReadReceipt()!.Stage);
        }

        [Fact]
        public void Interrupted_prepared_rollback_keeps_safety_copies_and_retries_without_reapplying()
        {
            using var directories = new TestDirectories();
            var first = Path.Combine(directories.Configuration, "first.xml");
            var second = Path.Combine(directories.Configuration, "second.xml");
            File.WriteAllText(first, "new-first");
            File.WriteAllText(second, "new-second");
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[]
                {
                    new ArchiveFile("first.xml", "must-not-reapply-first"),
                    new ArchiveFile("second.xml", "must-not-reapply-second")
                });
            fixture.Store.WriteReceipt(RestoreResultReceipt.FromMarker(
                fixture.Marker,
                RestoreExecutionStage.Prepared));
            var operationId = fixture.Marker.JobSnapshot.JobId.ToString("N");
            var firstSafety = Path.Combine(
                directories.Configuration,
                $".restore-{operationId}-first.xml.safety");
            var secondSafety = Path.Combine(
                directories.Configuration,
                $".restore-{operationId}-second.xml.safety");
            File.WriteAllText(firstSafety, "old-first");
            File.WriteAllText(secondSafety, "old-second");

            using (var lockSecond = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var error = Assert.Throws<RestoreStateException>(() => fixture.Applier.ApplyPending());

                Assert.Equal(PendingRestoreApplier.RollbackFailedError, error.ErrorCode);
                Assert.Equal("old-first", File.ReadAllText(first));
                Assert.Equal("new-second", ReadAllText(lockSecond));
                Assert.Equal("old-first", File.ReadAllText(firstSafety));
                Assert.Equal("old-second", File.ReadAllText(secondSafety));
                Assert.NotNull(fixture.Store.ReadMarker());
                Assert.Equal(RestoreExecutionStage.Prepared, fixture.Store.ReadReceipt()!.Stage);
            }

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ReplaceFailedError, result.ErrorCode);
            Assert.Equal("old-first", File.ReadAllText(first));
            Assert.Equal("old-second", File.ReadAllText(second));
            Assert.Null(fixture.Store.ReadMarker());
            Assert.Equal(RestoreExecutionStage.RolledBack, fixture.Store.ReadReceipt()!.Stage);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Configuration, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(".restore-", StringComparison.Ordinal));
        }

        [Fact]
        public void Prepared_restart_rolls_back_only_files_replaced_before_the_interruption()
        {
            using var directories = new TestDirectories();
            var first = Path.Combine(directories.Configuration, "first.xml");
            var second = Path.Combine(directories.Configuration, "second.xml");
            File.WriteAllText(first, "new-first");
            File.WriteAllText(second, "old-second");
            var fixture = CreateFixture(
                directories,
                BackupKind.ServerConfiguration,
                new[]
                {
                    new ArchiveFile("first.xml", "new-first"),
                    new ArchiveFile("second.xml", "new-second")
                });
            fixture.Store.WriteReceipt(RestoreResultReceipt.FromMarker(
                fixture.Marker,
                RestoreExecutionStage.Prepared));
            var operationId = fixture.Marker.JobSnapshot.JobId.ToString("N");
            var stagingRoot = Path.Combine(
                directories.Configuration,
                $".restore-{operationId}.staging");
            Directory.CreateDirectory(stagingRoot);
            File.WriteAllText(Path.Combine(stagingRoot, "second.xml"), "new-second");
            File.WriteAllText(
                Path.Combine(directories.Configuration, $".restore-{operationId}-first.xml.safety"),
                "old-first");
            File.WriteAllText(
                Path.Combine(directories.Configuration, $".restore-{operationId}-second.xml.safety"),
                "old-second");

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ReplaceFailedError, result.ErrorCode);
            Assert.Equal("old-first", File.ReadAllText(first));
            Assert.Equal("old-second", File.ReadAllText(second));
            Assert.Null(fixture.Store.ReadMarker());
            Assert.Equal(RestoreExecutionStage.RolledBack, fixture.Store.ReadReceipt()!.Stage);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Configuration, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).StartsWith(".restore-", StringComparison.Ordinal));
        }

        [Fact]
        public void Occupied_archive_during_prepared_recovery_preserves_retry_state_until_next_startup()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "new-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") });
            fixture.Store.WriteReceipt(RestoreResultReceipt.FromMarker(
                fixture.Marker,
                RestoreExecutionStage.Prepared));
            var operationId = fixture.Marker.JobSnapshot.JobId.ToString("N");
            var safety = Path.Combine(
                directories.Panel,
                $".restore-{operationId}-7dpanel.sqlite.safety");
            File.WriteAllText(safety, "old-panel");

            using (File.Open(
                fixture.ArchivePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                var error = Assert.Throws<RestoreStateException>(() => fixture.Applier.ApplyPending());

                Assert.Equal(PendingRestoreApplier.RollbackFailedError, error.ErrorCode);
                Assert.Equal("new-panel", File.ReadAllText(target));
                Assert.NotNull(fixture.Store.ReadMarker());
                Assert.Equal(RestoreExecutionStage.Prepared, fixture.Store.ReadReceipt()!.Stage);
                Assert.Equal("old-panel", File.ReadAllText(safety));
            }

            var result = fixture.Applier.ApplyPending();

            Assert.Equal(RestoreExecutionStage.RolledBack, result!.Stage);
            Assert.Equal(PendingRestoreApplier.ReplaceFailedError, result.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
            Assert.Null(fixture.Store.ReadMarker());
            Assert.Equal(RestoreExecutionStage.RolledBack, fixture.Store.ReadReceipt()!.Stage);
            Assert.False(File.Exists(safety));
        }

        [Fact]
        public void Damaged_receipt_stably_blocks_restore_before_any_overwrite()
        {
            using var directories = new TestDirectories();
            var target = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(target, "old-panel");
            var fixture = CreateFixture(
                directories,
                BackupKind.PanelDatabase,
                new[] { new ArchiveFile("panel-database.sqlite", "new-panel") });
            var receiptPath = Path.Combine(
                directories.Panel,
                JsonPendingRestoreStore.StateDirectoryName,
                JsonPendingRestoreStore.ReceiptFileName);
            File.WriteAllText(receiptPath, "{damaged");

            var error = Assert.Throws<RestoreStateException>(() => fixture.Applier.ApplyPending());

            Assert.Equal(JsonPendingRestoreStore.ReceiptInvalidError, error.ErrorCode);
            Assert.Equal("old-panel", File.ReadAllText(target));
        }

        private static RestoreFixture CreateFixture(
            TestDirectories directories,
            BackupKind kind,
            IReadOnlyList<ArchiveFile> files,
            Func<BackupArtifact, BackupArtifact>? mutateCatalog = null,
            string? manifestKind = null,
            RestoreArchiveLimits? limits = null,
            Action? onCatalogGet = null)
        {
            var roots = directories.CreateRoots();
            var store = new JsonPendingRestoreStore(roots);
            var snapshot = JsonPendingRestoreStoreTests.CreateMarker().JobSnapshot;
            var artifactId = Guid.NewGuid();
            var relativeResourceId = "restore-source-" + artifactId.ToString("N") + ".zip";
            var archivePath = roots.ResolveBackupResource(relativeResourceId);
            var manifestEntries = files.Select(file => new BackupManifestEntry(
                file.Path.Replace('\\', '/'),
                Encoding.UTF8.GetByteCount(file.Content),
                ComputeContentSha256(file.Content))).ToArray();
            var manifest = new BackupManifest(
                BackupManifest.CurrentVersion,
                manifestKind ?? kind.ToString(),
                kind == BackupKind.World ? "Navezgane" : string.Empty,
                kind == BackupKind.World ? roots.GameVersion : string.Empty,
                new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero),
                snapshot.JobId,
                manifestEntries);
            using (var stream = File.Open(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in files)
                {
                    var entry = zip.CreateEntry(file.Path, CompressionLevel.Optimal);
                    using var output = entry.Open();
                    var bytes = Encoding.UTF8.GetBytes(file.Content);
                    output.Write(bytes, 0, bytes.Length);
                }
                var manifestEntry = zip.CreateEntry(BackupManifest.EntryName, CompressionLevel.Optimal);
                using var manifestOutput = manifestEntry.Open();
                using var writer = new StreamWriter(manifestOutput, new UTF8Encoding(false));
                writer.Write(manifest.ToJson());
            }

            var sha256 = FileSystemBackupArchiveStore.ComputeSha256(archivePath);
            var artifact = new BackupArtifact(
                artifactId,
                kind,
                roots.BackupRootId,
                relativeResourceId,
                new FileInfo(archivePath).Length,
                sha256,
                kind == BackupKind.World ? "Navezgane" : null,
                kind == BackupKind.World ? roots.GameVersion : null,
                "Verified",
                new DateTimeOffset(2026, 7, 27, 1, 0, 0, TimeSpan.Zero),
                snapshot.JobId,
                BackupManifest.CurrentVersion);
            var marker = new PendingRestoreMarker(
                PendingRestoreMarker.CurrentVersion,
                artifact.Id,
                kind,
                roots.BackupRootId,
                relativeResourceId,
                sha256,
                snapshot,
                RestoreExecutionStage.Prepared);
            store.CreateMarker(marker);
            var catalog = new SingleArtifactCatalog(
                mutateCatalog == null ? artifact : mutateCatalog(artifact),
                onCatalogGet);
            var applier = new PendingRestoreApplier(
                roots,
                catalog,
                store,
                new WorldRestoreTimingGate(),
                "7dpanel.sqlite",
                limits ?? RestoreArchiveLimits.Default);
            return new RestoreFixture(applier, store, marker, archivePath);
        }

        private static string ComputeContentSha256(string content)
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
                return FileSystemBackupArchiveStore.ComputeSha256(path);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string ReadAllText(FileStream stream)
        {
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
            return reader.ReadToEnd();
        }

        private sealed record ArchiveFile(string Path, string Content);

        private sealed record RestoreFixture(
            PendingRestoreApplier Applier,
            JsonPendingRestoreStore Store,
            PendingRestoreMarker Marker,
            string ArchivePath);

        private sealed class SingleArtifactCatalog : IBackupCatalog
        {
            private readonly BackupArtifact artifact;
            private readonly Action? onGet;

            public SingleArtifactCatalog(BackupArtifact artifact, Action? onGet = null)
            {
                this.artifact = artifact;
                this.onGet = onGet;
            }

            public BackupArtifact Add(CompletedBackup backup) => throw new NotSupportedException();

            public BackupArtifact Get(Guid backupId)
            {
                onGet?.Invoke();
                if (backupId != artifact.Id) throw new KeyNotFoundException();
                return artifact;
            }

            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                new PagedResult<BackupArtifact, BackupCursor>(new[] { artifact }, null);

            public bool Delete(Guid backupId) => throw new NotSupportedException();
        }
    }
}
