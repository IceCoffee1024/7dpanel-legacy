using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class PanelDatabaseBackupJobTests
    {
        private const string PayloadMissingError = "panel_database_payload_missing";
        private const string OnlineBackupFailedError = "panel_database_backup_failed";
        private const string CatalogFailedError = "backup_catalog_failed";

        [Fact]
        public async System.Threading.Tasks.Task Wal_database_with_open_source_connection_archives_all_committed_rows_from_an_online_backup()
        {
            using var directories = new TestDirectories();
            var databasePath = Path.Combine(directories.Panel, "7dpanel.sqlite");
            using var source = Open(databasePath);
            Execute(source, "PRAGMA journal_mode=WAL;");
            Execute(source, "PRAGMA wal_autocheckpoint=0;");
            Execute(source, "CREATE TABLE evidence(id INTEGER PRIMARY KEY, value TEXT NOT NULL);");
            Execute(source, "INSERT INTO evidence(id, value) VALUES (1, 'checkpointed');");
            Execute(source, "PRAGMA wal_checkpoint(TRUNCATE);");
            Execute(source, "INSERT INTO evidence(id, value) VALUES (2, 'committed-in-wal');");

            Assert.True(File.Exists(databasePath + "-wal"));
            var rawDatabaseCopy = Path.Combine(directories.Root, "raw-file-copy.sqlite");
            File.Copy(databasePath, rawDatabaseCopy);
            Assert.Equal(new[] { "checkpointed" }, ReadValues(rawDatabaseCopy));

            var fixture = CreateFixture(directories, databasePath);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            var archivePath = fixture.Roots.ResolveBackupResource(completed.RelativeResourceId);
            var archivedDatabase = ExtractOnlyDatabase(archivePath, directories.Root);
            Assert.Equal(
                new[] { "checkpointed", "committed-in-wal" },
                ReadValues(archivedDatabase));

            using var archiveStream = File.OpenRead(archivePath);
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            Assert.DoesNotContain(zip.Entries, entry =>
                entry.FullName.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("-shm", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async System.Threading.Tasks.Task Catalog_resource_id_is_a_flat_server_generated_panel_database_id()
        {
            using var directories = new TestDirectories();
            var databasePath = CreateDatabase(directories);
            var fixture = CreateFixture(directories, databasePath, seed: 109);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(
                "panel-database-20260727-" + fixture.Running.Id.ToString("N") + ".zip",
                completed.RelativeResourceId);
            AssertOpaqueResourceId(completed.RelativeResourceId);
        }

        [Fact]
        public async System.Threading.Tasks.Task Missing_typed_payload_fails_without_running_the_database_handler()
        {
            using var directories = new TestDirectories();
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.PanelDatabaseBackup, 101);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new PanelPayloadReader(events);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var handler = new PanelDatabaseBackupJobHandler(
                roots,
                archives,
                Path.Combine(directories.Panel, "missing.sqlite"));
            var consumer = CreateConsumer(
                jobs,
                payloads,
                roots,
                handler,
                new RecordingBackupCatalog(events, roots));

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(jobs, PayloadMissingError);
            Assert.Empty(FindPublishedArchives(directories));
        }

        [Fact]
        public async System.Threading.Tasks.Task Invalid_source_database_maps_online_backup_failure_to_a_stable_error()
        {
            using var directories = new TestDirectories();
            var databasePath = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(databasePath, "not a SQLite database");
            var fixture = CreateFixture(directories, databasePath, seed: 102);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture.Jobs, OnlineBackupFailedError);
            Assert.Empty(fixture.Catalog.Completed);
            Assert.Empty(FindPublishedArchives(directories));
        }

        [Fact]
        public async System.Threading.Tasks.Task Online_backup_failure_cleans_the_temporary_consistent_copy()
        {
            using var directories = new TestDirectories();
            var databasePath = Path.Combine(directories.Panel, "7dpanel.sqlite");
            File.WriteAllText(databasePath, "not a SQLite database");
            var fixture = CreateFixture(directories, databasePath, seed: 103);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertNoTemporaryArtifacts(directories, databasePath);
        }

        [Fact]
        public async System.Threading.Tasks.Task Zip_failure_maps_to_the_shared_stable_error()
        {
            using var directories = new TestDirectories();
            var databasePath = CreateDatabase(directories);
            var fixture = CreateFixture(directories, databasePath, seed: 104);
            Directory.Delete(directories.Backups, true);
            File.WriteAllText(directories.Backups, "blocks the approved backup directory");

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture.Jobs, FileSystemBackupArchiveStore.ZipFailedError);
            Assert.Empty(fixture.Catalog.Completed);
        }

        [Fact]
        public async System.Threading.Tasks.Task Zip_failure_cleans_the_consistent_copy_and_unpublished_zip()
        {
            using var directories = new TestDirectories();
            var databasePath = CreateDatabase(directories);
            var fixture = CreateFixture(directories, databasePath, seed: 105);
            Directory.Delete(directories.Backups, true);
            File.WriteAllText(directories.Backups, "blocks the approved backup directory");

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertNoTemporaryArtifacts(directories, databasePath);
            Assert.Empty(FindPublishedArchives(directories));
        }

        [Fact]
        public void Checksum_mismatch_uses_the_shared_stable_error()
        {
            using var directories = new TestDirectories();
            var archivePath = Path.Combine(directories.Backups, "panel-database-corrupt.zip");
            File.WriteAllText(archivePath, "corrupt archive");

            var exception = Assert.Throws<BackupArchiveException>(() =>
                FileSystemBackupArchiveStore.VerifyChecksum(archivePath, new string('0', 64)));

            Assert.Equal(FileSystemBackupArchiveStore.ChecksumFailedError, exception.ErrorCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task Catalog_failure_maps_to_a_stable_error_without_a_succeeded_transition()
        {
            using var directories = new TestDirectories();
            var databasePath = CreateDatabase(directories);
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.PanelDatabaseBackup, 107);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new PanelPayloadReader(events, running.Id);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var consumer = CreateConsumer(
                jobs,
                payloads,
                roots,
                new PanelDatabaseBackupJobHandler(roots, archives, databasePath),
                new ThrowingBackupCatalog(events));

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(jobs, CatalogFailedError);
            Assert.DoesNotContain(jobs.Transitions, transition => transition.Next == JobStatus.Succeeded);
            AssertNoTemporaryArtifacts(directories, databasePath);
        }

        [Fact]
        public async System.Threading.Tasks.Task Successful_backup_catalogs_panel_database_before_the_final_succeeded_cas()
        {
            using var directories = new TestDirectories();
            var databasePath = CreateDatabase(directories);
            var fixture = CreateFixture(directories, databasePath, seed: 108);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(BackupKind.PanelDatabase, completed.Kind);
            Assert.Equal(fixture.Running.Id, completed.SourceJobId);
            Assert.Equal(
                new[] { "claim", "payload", "catalog", "transition:Succeeded" },
                fixture.Events.Items);
            var transition = Assert.Single(fixture.Jobs.Transitions);
            Assert.Equal(fixture.Running.RowVersion, transition.ExpectedRowVersion);
            Assert.Equal(JobStatus.Running, transition.Expected);
            Assert.Equal(JobStatus.Succeeded, transition.Next);
            Assert.Null(transition.Completion.ErrorCode);
        }

        private static PanelFixture CreateFixture(
            TestDirectories directories,
            string databasePath,
            int seed = 100)
        {
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.PanelDatabaseBackup, seed);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new PanelPayloadReader(events, running.Id);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var catalog = new RecordingBackupCatalog(events, roots);
            var consumer = CreateConsumer(
                jobs,
                payloads,
                roots,
                new PanelDatabaseBackupJobHandler(roots, archives, databasePath),
                catalog);
            return new PanelFixture(consumer, jobs, catalog, roots, events, running);
        }

        private static BackgroundWorkConsumer CreateConsumer(
            IJobStore jobs,
            IJobPayloadReader payloads,
            ApprovedStorageRoots roots,
            PanelDatabaseBackupJobHandler panelDatabaseHandler,
            IBackupCatalog catalog)
        {
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var unusedConfiguration = Path.Combine(roots.ServerConfigurationRoot, "unused.xml");
            File.WriteAllText(unusedConfiguration, "unused");
            return new BackgroundWorkConsumer(
                jobs,
                payloads,
                new RecordingWorldSaveGateway(new RecordingEvents()),
                archives,
                panelDatabaseHandler,
                new ServerConfigurationBackupJobHandler(
                    roots,
                    archives,
                    new[] { "unused.xml" }),
                catalog,
                "worker-1",
                () => Utc(1),
                TimeSpan.FromMilliseconds(1));
        }

        private static string CreateDatabase(TestDirectories directories)
        {
            var databasePath = Path.Combine(directories.Panel, "7dpanel.sqlite");
            using var connection = Open(databasePath);
            Execute(connection, "CREATE TABLE evidence(id INTEGER PRIMARY KEY, value TEXT NOT NULL);");
            Execute(connection, "INSERT INTO evidence(id, value) VALUES (1, 'committed');");
            return databasePath;
        }

        private static SqliteConnection Open(string databasePath)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            connection.Open();
            return connection;
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string[] ReadValues(string databasePath)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM evidence ORDER BY id;";
            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read()) values.Add(reader.GetString(0));
            return values.ToArray();
        }

        private static string ExtractOnlyDatabase(string archivePath, string destinationRoot)
        {
            using var archiveStream = File.OpenRead(archivePath);
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            Assert.NotNull(zip.GetEntry(BackupManifest.EntryName));
            var databaseEntry = Assert.Single(zip.Entries, entry =>
                !string.Equals(entry.FullName, BackupManifest.EntryName, StringComparison.OrdinalIgnoreCase));
            var destination = Path.Combine(destinationRoot, "archived-panel.sqlite");
            using var input = databaseEntry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
            return destination;
        }

        private static string[] FindPublishedArchives(TestDirectories directories) =>
            Directory.EnumerateFiles(directories.Root, "*.zip", SearchOption.AllDirectories).ToArray();

        private static void AssertNoTemporaryArtifacts(
            TestDirectories directories,
            string databasePath)
        {
            var allowedPanelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(databasePath),
                Path.GetFullPath(databasePath + "-wal"),
                Path.GetFullPath(databasePath + "-shm")
            };
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Panel, "*", SearchOption.AllDirectories),
                path => !allowedPanelFiles.Contains(Path.GetFullPath(path)));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Root, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }

        private static void AssertFailure(RecordingJobStore jobs, string errorCode)
        {
            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(JobStatus.Failed, transition.Next);
            Assert.Equal(errorCode, transition.Completion.ErrorCode);
        }

        private static void AssertOpaqueResourceId(string relativeResourceId)
        {
            Assert.DoesNotContain("/", relativeResourceId);
            Assert.DoesNotContain("\\", relativeResourceId);
            Assert.DoesNotContain("..", relativeResourceId);
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 0, minute, 0, TimeSpan.Zero);

        private sealed record PanelFixture(
            BackgroundWorkConsumer Consumer,
            RecordingJobStore Jobs,
            RecordingBackupCatalog Catalog,
            ApprovedStorageRoots Roots,
            RecordingEvents Events,
            JobRecord Running);

        private sealed class PanelPayloadReader : IJobPayloadReader
        {
            private readonly RecordingEvents events;
            private readonly HashSet<Guid> panelJobs = new HashSet<Guid>();

            public PanelPayloadReader(RecordingEvents events, params Guid[] panelJobIds)
            {
                this.events = events;
                foreach (var jobId in panelJobIds) panelJobs.Add(jobId);
            }

            public WorldBackupPayload GetWorldBackup(Guid jobId) => throw new NotSupportedException();

            public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId)
            {
                events.Items.Add("payload");
                if (!panelJobs.Contains(jobId)) throw new KeyNotFoundException();
                return new PanelDatabaseBackupPayload();
            }

            public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId) =>
                throw new NotSupportedException();

            public RestorePayload GetRestore(Guid jobId) => throw new NotSupportedException();
            public ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId) => throw new NotSupportedException();
            public ScheduledRestartPayload GetScheduledRestart(Guid jobId) => throw new NotSupportedException();
            public ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId) => throw new NotSupportedException();
        }

        private sealed class ThrowingBackupCatalog : IBackupCatalog
        {
            private readonly RecordingEvents events;

            public ThrowingBackupCatalog(RecordingEvents events)
            {
                this.events = events;
            }

            public BackupArtifact Add(CompletedBackup backup)
            {
                events.Items.Add("catalog");
                throw new InvalidOperationException("raw catalog failure must not escape");
            }

            public BackupArtifact Get(Guid backupId) => throw new NotSupportedException();

            public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
                new PagedResult<BackupArtifact, BackupCursor>(Array.Empty<BackupArtifact>(), null);

            public bool Delete(Guid backupId) => false;
        }
    }
}
