using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Application
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class BackupCatalogServiceTests
    {
        [Fact]
        public void List_filters_by_kind_and_created_utc_with_a_stable_keyset_cursor()
        {
            using var fixture = new Fixture();
            fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "panel-0");
            var expectedSecond = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(1), "panel-1");
            var expectedFirst = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(2), "panel-2");
            fixture.AddArtifact(BackupKind.World, Utc(2), "world-2");

            var first = fixture.Service.List(new BackupQuery(
                1, BackupKind.PanelDatabase, Utc(1), Utc(2), null));
            var second = fixture.Service.List(new BackupQuery(
                1, BackupKind.PanelDatabase, Utc(1), Utc(2), first.NextCursor));

            Assert.Equal(expectedFirst.Id, Assert.Single(first.Items).Id);
            Assert.NotNull(first.NextCursor);
            Assert.Equal(expectedSecond.Id, Assert.Single(second.Items).Id);
            Assert.Null(second.NextCursor);
            Assert.All(first.Items.Concat(second.Items), artifact =>
            {
                Assert.DoesNotContain("/", artifact.BackupRootId);
                Assert.DoesNotContain("\\", artifact.BackupRootId);
                Assert.DoesNotContain("..", artifact.BackupRootId);
                Assert.DoesNotContain("/", artifact.RelativeResourceId);
                Assert.DoesNotContain("\\", artifact.RelativeResourceId);
                Assert.DoesNotContain("..", artifact.RelativeResourceId);
            });
        }

        [Fact]
        public void PrepareDownload_returns_verified_content_with_a_generated_safe_attachment_name()
        {
            using var fixture = new Fixture();
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var artifact = fixture.AddArtifact(
                BackupKind.ServerConfiguration,
                Utc(2),
                "configuration-content",
                id);

            using var download = fixture.Service.PrepareDownload(artifact.Id);
            using var reader = new StreamReader(download.Content);

            Assert.Equal(
                "server-configuration-20260728-11111111222233334444555555555555.zip",
                download.AttachmentFileName);
            Assert.Equal(artifact.SizeBytes, download.ContentLength);
            Assert.Equal("configuration-content", reader.ReadToEnd());
        }

        [Fact]
        public void PrepareDownload_maps_a_missing_catalog_file_to_backup_integrity_failed()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");
            File.Delete(fixture.PathFor(artifact));

            var error = Assert.Throws<BackupCatalogException>(() =>
                fixture.Service.PrepareDownload(artifact.Id));

            Assert.Equal(BackupCatalogService.IntegrityFailedError, error.ErrorCode);
        }

        [Fact]
        public void PrepareDownload_maps_a_size_change_to_backup_integrity_failed()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");
            File.AppendAllText(fixture.PathFor(artifact), "-larger");

            var error = Assert.Throws<BackupCatalogException>(() =>
                fixture.Service.PrepareDownload(artifact.Id));

            Assert.Equal(BackupCatalogService.IntegrityFailedError, error.ErrorCode);
        }

        [Fact]
        public void PrepareDownload_maps_a_checksum_change_to_backup_integrity_failed()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");
            File.WriteAllText(fixture.PathFor(artifact), "changed");

            var error = Assert.Throws<BackupCatalogException>(() =>
                fixture.Service.PrepareDownload(artifact.Id));

            Assert.Equal(BackupCatalogService.IntegrityFailedError, error.ErrorCode);
        }

        [Fact]
        public void Delete_rejects_an_artifact_referenced_by_a_queued_restore_job()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");
            fixture.EnqueueRestore(artifact);

            var error = Assert.Throws<BackupCatalogException>(() =>
                fixture.Service.Delete(artifact.Id));

            Assert.Equal(BackupCatalogService.BackupInUseError, error.ErrorCode);
            Assert.True(File.Exists(fixture.PathFor(artifact)));
            Assert.Equal(artifact, fixture.Catalog.Get(artifact.Id));
        }

        [Fact]
        public void Delete_removes_the_catalog_managed_file_before_the_catalog_row()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");

            fixture.Service.Delete(artifact.Id);

            Assert.False(File.Exists(fixture.PathFor(artifact)));
            Assert.Throws<KeyNotFoundException>(() => fixture.Catalog.Get(artifact.Id));
        }

        [Fact]
        public void Delete_reports_catalog_failure_after_file_removal_without_claiming_success()
        {
            using var fixture = new Fixture();
            var artifact = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "content");
            var catalog = new DeleteFailingCatalog(artifact);
            var service = new BackupCatalogService(
                catalog,
                fixture.Archives,
                fixture.Jobs,
                fixture.Payloads);

            var error = Assert.Throws<BackupCatalogException>(() => service.Delete(artifact.Id));

            Assert.Equal(BackupCatalogService.DeleteFailedError, error.ErrorCode);
            Assert.False(File.Exists(fixture.PathFor(artifact)));
            Assert.Equal(artifact, catalog.Get(artifact.Id));
        }

        [Fact]
        public void Retention_applies_count_and_age_per_kind_while_skipping_in_use_and_retained_artifacts()
        {
            using var fixture = new Fixture();
            var oldRetained = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "retained");
            var oldInUse = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(1), "in-use");
            var oldEligible = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(2), "old");
            var countEligible = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(9), "count");
            var newest = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(10), "newest");
            var otherKind = fixture.AddArtifact(BackupKind.World, Utc(0), "world");
            fixture.EnqueueRestore(oldInUse);
            var retention = new BackupRetentionService(fixture.Service);

            var result = retention.Apply(
                new BackupRetentionPolicy(
                    BackupKind.PanelDatabase,
                    1,
                    5,
                    new[] { oldRetained.Id }),
                Utc(10));

            Assert.Equal(
                new[] { countEligible.Id, oldEligible.Id }.OrderBy(id => id),
                result.DeletedBackupIds.OrderBy(id => id));
            Assert.Empty(result.Errors);
            Assert.Equal(oldRetained, fixture.Catalog.Get(oldRetained.Id));
            Assert.Equal(oldInUse, fixture.Catalog.Get(oldInUse.Id));
            Assert.Equal(newest, fixture.Catalog.Get(newest.Id));
            Assert.Equal(otherKind, fixture.Catalog.Get(otherKind.Id));
        }

        [Fact]
        public void Retention_returns_cleanup_errors_without_deleting_the_just_completed_backup()
        {
            using var fixture = new Fixture();
            var old = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(0), "old");
            var justCompleted = fixture.AddArtifact(BackupKind.PanelDatabase, Utc(10), "new");
            var retention = new BackupRetentionService(fixture.Service);
            using var locked = File.Open(
                fixture.PathFor(old),
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);

            var result = retention.Apply(
                new BackupRetentionPolicy(
                    BackupKind.PanelDatabase,
                    1,
                    0,
                    Array.Empty<Guid>()),
                Utc(10));

            var error = Assert.Single(result.Errors);
            Assert.Equal(old.Id, error.BackupId);
            Assert.Equal(BackupCatalogService.DeleteFailedError, error.ErrorCode);
            Assert.Equal(justCompleted, fixture.Catalog.Get(justCompleted.Id));
            Assert.True(File.Exists(fixture.PathFor(justCompleted)));
        }

        private static DateTimeOffset Utc(int day) =>
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero).AddDays(day);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class Fixture : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-backup-catalog-tests",
                Guid.NewGuid().ToString("N"));
            private int sequence;

            public Fixture()
            {
                var worldRoot = Path.Combine(directory, "world");
                var panelRoot = Path.Combine(directory, "panel");
                var configurationRoot = Path.Combine(directory, "configuration");
                BackupRoot = Path.Combine(directory, "backups");
                Directory.CreateDirectory(worldRoot);
                Directory.CreateDirectory(panelRoot);
                Directory.CreateDirectory(configurationRoot);
                Directory.CreateDirectory(BackupRoot);

                Roots = new ApprovedStorageRoots(
                    "Navezgane",
                    worldRoot,
                    panelRoot,
                    configurationRoot,
                    "primary",
                    BackupRoot,
                    "V1");
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(panelRoot, "catalog.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
                Catalog = new SqliteBackupCatalog(ConnectionFactory);
                Jobs = new SqliteJobStore(ConnectionFactory);
                Payloads = new SqliteJobPayloadStore(ConnectionFactory);
                Archives = new FileSystemBackupArchiveStore(Roots, new AtomicFileWriter(Roots));
                Service = new BackupCatalogService(Catalog, Archives, Jobs, Payloads);
            }

            public string BackupRoot { get; }
            public ApprovedStorageRoots Roots { get; }
            public SqliteConnectionFactory ConnectionFactory { get; }
            public SqliteBackupCatalog Catalog { get; }
            public SqliteJobStore Jobs { get; }
            public SqliteJobPayloadStore Payloads { get; }
            public FileSystemBackupArchiveStore Archives { get; }
            public BackupCatalogService Service { get; }

            public BackupArtifact AddArtifact(
                BackupKind kind,
                DateTimeOffset createdAtUtc,
                string content,
                Guid? artifactId = null)
            {
                var sourceJob = EnqueueSource(kind);
                var id = artifactId ?? Guid.NewGuid();
                var resourceId = "artifact-" + id.ToString("N") + ".zip";
                var path = Path.Combine(BackupRoot, resourceId);
                File.WriteAllText(path, content);
                var bytes = File.ReadAllBytes(path);
                var checksum = ComputeSha256(bytes);
                return Catalog.Add(new CompletedBackup(
                    id,
                    kind,
                    Roots.BackupRootId,
                    resourceId,
                    bytes.LongLength,
                    checksum,
                    kind == BackupKind.World ? "Navezgane" : null,
                    kind == BackupKind.World ? Roots.GameVersion : null,
                    "Verified",
                    createdAtUtc,
                    sourceJob.Id,
                    1));
            }

            public string PathFor(BackupArtifact artifact) =>
                Path.Combine(BackupRoot, artifact.RelativeResourceId);

            public void EnqueueRestore(BackupArtifact artifact)
            {
                Payloads.Enqueue(
                    NewJob(JobKind.Restore, "restore-" + sequence++),
                    new RestorePayload(artifact.Id, artifact.Kind, false));
            }

            private JobRecord EnqueueSource(BackupKind kind)
            {
                var jobKind = kind switch
                {
                    BackupKind.World => JobKind.WorldBackup,
                    BackupKind.PanelDatabase => JobKind.PanelDatabaseBackup,
                    BackupKind.ServerConfiguration => JobKind.ServerConfigurationBackup,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
                var job = NewJob(jobKind, "source-" + sequence++);
                return kind switch
                {
                    BackupKind.World => Payloads.Enqueue(job, new WorldBackupPayload("Navezgane")),
                    BackupKind.PanelDatabase => Payloads.Enqueue(job, new PanelDatabaseBackupPayload()),
                    BackupKind.ServerConfiguration => Payloads.Enqueue(job, new ServerConfigurationBackupPayload()),
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
            }

            private static NewJob NewJob(JobKind kind, string key) =>
                new NewJob(kind, "owner", null, key, "corr-" + key, Utc(0));

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class DeleteFailingCatalog : IBackupCatalog
        {
            private readonly BackupArtifact artifact;

            public DeleteFailingCatalog(BackupArtifact artifact) => this.artifact = artifact;

            public BackupArtifact Add(CompletedBackup backup) => throw new NotSupportedException();

            public BackupArtifact Get(Guid backupId) =>
                backupId == artifact.Id
                    ? artifact
                    : throw new KeyNotFoundException();

            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                new PagedResult<BackupArtifact, BackupCursor>(new[] { artifact }, null);

            public bool Delete(Guid backupId) => throw new IOException("catalog unavailable");
        }

        private static string ComputeSha256(byte[] content)
        {
            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(content).Select(value => value.ToString("x2")));
        }
    }
}
