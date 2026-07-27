using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class ServerConfigurationBackupJobTests
    {
        private const string PayloadMissingError = "server_configuration_payload_missing";
        private const string EmptyFileListError = "server_configuration_file_list_empty";
        private const string SourceUnavailableError = "server_configuration_source_unavailable";
        private const string CatalogFailedError = "backup_catalog_failed";

        [Fact]
        public async System.Threading.Tasks.Task Approved_files_are_archived_with_their_relative_paths()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "<ServerSettings />");
            WriteConfigurationFile(directories, "profiles/admin.xml", "<AdminTools />");
            var fixture = CreateFixture(
                directories,
                new[] { "serverconfig.xml", "profiles/admin.xml" },
                seed: 201);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(
                new[] { BackupManifest.EntryName, "profiles/admin.xml", "serverconfig.xml" },
                ReadEntryNames(fixture.Roots.ResolveBackupResource(completed.RelativeResourceId)));
        }

        [Fact]
        public async System.Threading.Tasks.Task Catalog_resource_id_is_a_flat_server_generated_configuration_id()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            var fixture = CreateFixture(directories, new[] { "serverconfig.xml" }, seed: 211);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(
                "server-configuration-20260727-" + fixture.Running.Id.ToString("N") + ".zip",
                completed.RelativeResourceId);
            AssertOpaqueResourceId(completed.RelativeResourceId);
        }

        [Fact]
        public async System.Threading.Tasks.Task Unlisted_logs_world_database_backup_and_secret_files_are_excluded()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            WriteConfigurationFile(directories, "logs/latest.log", "log-secret");
            WriteConfigurationFile(directories, "secrets.env", "configuration-secret");
            WriteConfigurationFile(directories, "serverconfig.xml.bak", "spill-copy");
            File.WriteAllText(Path.Combine(directories.World, "main.ttw"), "world-secret");
            File.WriteAllText(Path.Combine(directories.Panel, "7dpanel.sqlite"), "database-secret");
            File.WriteAllText(Path.Combine(directories.Backups, "existing.zip"), "backup-secret");
            var fixture = CreateFixture(directories, new[] { "serverconfig.xml" }, seed: 202);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(
                new[] { BackupManifest.EntryName, "serverconfig.xml" },
                ReadEntryNames(fixture.Roots.ResolveBackupResource(completed.RelativeResourceId)));
        }

        [Theory]
        [InlineData("C:\\outside.xml")]
        [InlineData("/outside.xml")]
        [InlineData("../outside.xml")]
        [InlineData("profiles/../../outside.xml")]
        public void Bootstrap_file_list_rejects_absolute_and_parent_paths(string relativePath)
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));

            Assert.Throws<ArgumentException>(() =>
                new ServerConfigurationBackupJobHandler(roots, archives, new[] { relativePath }));
        }

        [Fact]
        public async System.Threading.Tasks.Task Reparse_escape_is_rejected_as_an_unavailable_configuration_source()
        {
            if (Path.DirectorySeparatorChar != '\\') return;

            using var directories = new TestDirectories();
            var outside = Path.Combine(directories.Root, "outside-configuration");
            var junction = Path.Combine(directories.Configuration, "escape");
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "secret.xml"), "must not escape");
            CreateJunction(junction, outside);
            try
            {
                var fixture = CreateFixture(
                    directories,
                    new[] { "escape/secret.xml" },
                    seed: 203);

                Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

                AssertFailure(fixture.Jobs, SourceUnavailableError);
                Assert.Empty(fixture.Catalog.Completed);
            }
            finally
            {
                if (Directory.Exists(junction)) Directory.Delete(junction);
            }
        }

        [Fact]
        public void Empty_bootstrap_file_list_has_a_stable_configuration_error()
        {
            using var directories = new TestDirectories();
            var roots = directories.CreateRoots();
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));

            var exception = Assert.Throws<ArgumentException>(() =>
                new ServerConfigurationBackupJobHandler(roots, archives, Array.Empty<string>()));

            Assert.StartsWith(EmptyFileListError, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async System.Threading.Tasks.Task Missing_required_file_maps_to_a_stable_source_error()
        {
            using var directories = new TestDirectories();
            var fixture = CreateFixture(
                directories,
                new[] { "serverconfig.xml", "profiles/admin.xml" },
                seed: 204);
            WriteConfigurationFile(directories, "serverconfig.xml", "present");

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture.Jobs, SourceUnavailableError);
            Assert.Empty(fixture.Catalog.Completed);
            Assert.Empty(FindPublishedArchives(directories));
        }

        [Fact]
        public async System.Threading.Tasks.Task Missing_typed_payload_fails_without_running_the_configuration_handler()
        {
            using var directories = new TestDirectories();
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.ServerConfigurationBackup, 205);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new ServerConfigurationPayloadReader(events);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var handler = new ServerConfigurationBackupJobHandler(
                roots,
                archives,
                new[] { "missing.xml" });
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
        public async System.Threading.Tasks.Task Zip_failure_maps_to_the_shared_stable_error()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            var fixture = CreateFixture(directories, new[] { "serverconfig.xml" }, seed: 206);
            Directory.Delete(directories.Backups, true);
            File.WriteAllText(directories.Backups, "blocks the approved backup directory");

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture.Jobs, FileSystemBackupArchiveStore.ZipFailedError);
            Assert.Empty(fixture.Catalog.Completed);
        }

        [Fact]
        public async System.Threading.Tasks.Task Zip_failure_cleans_the_unpublished_archive()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            var fixture = CreateFixture(directories, new[] { "serverconfig.xml" }, seed: 207);
            Directory.Delete(directories.Backups, true);
            File.WriteAllText(directories.Backups, "blocks the approved backup directory");

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Root, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(FindPublishedArchives(directories));
        }

        [Fact]
        public void Checksum_mismatch_uses_the_shared_stable_error()
        {
            using var directories = new TestDirectories();
            var archivePath = Path.Combine(directories.Backups, "server-configuration-corrupt.zip");
            File.WriteAllText(archivePath, "corrupt archive");

            var exception = Assert.Throws<BackupArchiveException>(() =>
                FileSystemBackupArchiveStore.VerifyChecksum(archivePath, new string('0', 64)));

            Assert.Equal(FileSystemBackupArchiveStore.ChecksumFailedError, exception.ErrorCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task Catalog_failure_maps_to_a_stable_error_without_a_succeeded_transition()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.ServerConfigurationBackup, 209);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new ServerConfigurationPayloadReader(events, running.Id);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var consumer = CreateConsumer(
                jobs,
                payloads,
                roots,
                new ServerConfigurationBackupJobHandler(roots, archives, new[] { "serverconfig.xml" }),
                new ThrowingBackupCatalog(events));

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(jobs, CatalogFailedError);
            Assert.DoesNotContain(jobs.Transitions, transition => transition.Next == JobStatus.Succeeded);
            Assert.DoesNotContain(
                Directory.EnumerateFiles(directories.Root, "*", SearchOption.AllDirectories),
                path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async System.Threading.Tasks.Task Successful_backup_catalogs_server_configuration_before_the_final_succeeded_cas()
        {
            using var directories = new TestDirectories();
            WriteConfigurationFile(directories, "serverconfig.xml", "approved");
            var fixture = CreateFixture(directories, new[] { "serverconfig.xml" }, seed: 210);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(fixture.Catalog.Completed);
            Assert.Equal(BackupKind.ServerConfiguration, completed.Kind);
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

        private static ServerConfigurationFixture CreateFixture(
            TestDirectories directories,
            IReadOnlyCollection<string> approvedRelativeFiles,
            int seed)
        {
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.ServerConfigurationBackup, seed);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var payloads = new ServerConfigurationPayloadReader(events, running.Id);
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var catalog = new RecordingBackupCatalog(events, roots);
            var consumer = CreateConsumer(
                jobs,
                payloads,
                roots,
                new ServerConfigurationBackupJobHandler(roots, archives, approvedRelativeFiles),
                catalog);
            return new ServerConfigurationFixture(consumer, jobs, catalog, roots, events, running);
        }

        private static BackgroundWorkConsumer CreateConsumer(
            IJobStore jobs,
            IJobPayloadReader payloads,
            ApprovedStorageRoots roots,
            ServerConfigurationBackupJobHandler serverConfigurationHandler,
            IBackupCatalog catalog)
        {
            var archives = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var unusedDatabase = Path.Combine(roots.PanelStateRoot, "unused.sqlite");
            File.WriteAllBytes(unusedDatabase, Array.Empty<byte>());
            return new BackgroundWorkConsumer(
                jobs,
                payloads,
                new RecordingWorldSaveGateway(new RecordingEvents()),
                archives,
                new PanelDatabaseBackupJobHandler(roots, archives, unusedDatabase),
                serverConfigurationHandler,
                catalog,
                "worker-1",
                () => Utc(1),
                TimeSpan.FromMilliseconds(1));
        }

        private static void WriteConfigurationFile(
            TestDirectories directories,
            string relativePath,
            string content)
        {
            var destination = Path.Combine(
                directories.Configuration,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, content);
        }

        private static string[] ReadEntryNames(string archivePath)
        {
            using var stream = File.OpenRead(archivePath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            return zip.Entries
                .Select(entry => entry.FullName.Replace('\\', '/'))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] FindPublishedArchives(TestDirectories directories) =>
            Directory.EnumerateFiles(directories.Root, "*.zip", SearchOption.AllDirectories).ToArray();

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

        private static void CreateJunction(string junction, string target)
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /c mklink /J \"" + junction + "\" \"" + target + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Unable to start mklink.");
            process.WaitForExit();
            Assert.True(
                process.ExitCode == 0,
                "mklink failed: " + process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd());
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 27, 0, minute, 0, TimeSpan.Zero);

        private sealed record ServerConfigurationFixture(
            BackgroundWorkConsumer Consumer,
            RecordingJobStore Jobs,
            RecordingBackupCatalog Catalog,
            ApprovedStorageRoots Roots,
            RecordingEvents Events,
            JobRecord Running);

        private sealed class ServerConfigurationPayloadReader : IJobPayloadReader
        {
            private readonly RecordingEvents events;
            private readonly HashSet<Guid> configurationJobs = new HashSet<Guid>();

            public ServerConfigurationPayloadReader(
                RecordingEvents events,
                params Guid[] configurationJobIds)
            {
                this.events = events;
                foreach (var jobId in configurationJobIds) configurationJobs.Add(jobId);
            }

            public WorldBackupPayload GetWorldBackup(Guid jobId) => throw new NotSupportedException();
            public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId) => throw new NotSupportedException();

            public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId)
            {
                events.Items.Add("payload");
                if (!configurationJobs.Contains(jobId)) throw new KeyNotFoundException();
                return new ServerConfigurationBackupPayload();
            }

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
