using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.SevenDays.Outbound.Backups;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class WorldBackupJobTests
    {
        [Fact]
        public void Create_world_backup_submits_job_and_typed_payload_atomically()
        {
            var submissions = new RecordingSubmissionStore();
            var at = Utc(0);
            var useCase = new CreateWorldBackup(submissions, () => at);

            var result = useCase.Execute("owner", "Navezgane", "backup-1", "corr-1");

            Assert.Same(submissions.Returned, result);
            Assert.Equal(JobKind.WorldBackup, submissions.Job!.Kind);
            Assert.Equal("owner", submissions.Job.ActorSubject);
            Assert.Equal("backup-1", submissions.Job.IdempotencyKey);
            Assert.Equal("corr-1", submissions.Job.CorrelationId);
            Assert.Equal(at, submissions.Job.CreatedAtUtc);
            Assert.Equal("Navezgane", submissions.WorldPayload!.WorldName);
            Assert.Equal(1, submissions.WorldCalls);
        }

        [Fact]
        public async Task World_backup_confirms_game_save_then_publishes_manifest_checksum_catalog_and_cas_terminal_state()
        {
            using var directories = new TestDirectories();
            Directory.CreateDirectory(Path.Combine(directories.World, "Region"));
            File.WriteAllText(Path.Combine(directories.World, "main.ttw"), "world-state");
            File.WriteAllText(Path.Combine(directories.World, "Region", "r.0.0.7rg"), "region-state");
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.WorldBackup, 1);
            var jobs = new RecordingJobStore(events, running);
            var payloads = new RecordingPayloadReader(events, running.Id, "Navezgane");
            var save = new RecordingWorldSaveGateway(events);
            var roots = directories.CreateRoots();
            var catalog = new RecordingBackupCatalog(events, roots);
            var archive = new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots));
            var consumer = new BackgroundWorkConsumer(
                jobs, payloads, save, archive, catalog, "worker-1", () => Utc(1), TimeSpan.FromMilliseconds(1));

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var completed = Assert.Single(catalog.Completed);
            Assert.DoesNotContain("/", completed.RelativeResourceId);
            Assert.DoesNotContain("\\", completed.RelativeResourceId);
            var archivePath = roots.ResolveBackupResource(completed.RelativeResourceId);
            Assert.True(File.Exists(archivePath));
            Assert.Equal(BackupKind.World, completed.Kind);
            Assert.Equal("primary", completed.BackupRootId);
            Assert.Equal("Navezgane", completed.WorldId);
            Assert.Equal("v3.0.1-b4", completed.GameVersion);
            Assert.Equal("Verified", completed.ValidationStatus);
            Assert.Equal(FileSystemBackupArchiveStore.ComputeSha256(archivePath), completed.Sha256);
            Assert.Equal(new[] { "claim", "payload", "save", "catalog", "transition:Succeeded" }, events.Items);
            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(running.RowVersion, transition.ExpectedRowVersion);
            Assert.Equal(JobStatus.Running, transition.Expected);
            Assert.Equal(JobStatus.Succeeded, transition.Next);
            Assert.Null(transition.Completion.ErrorCode);

            using var stream = File.OpenRead(archivePath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            Assert.NotNull(zip.GetEntry(BackupManifest.EntryName));
            Assert.Contains(zip.Entries, entry => entry.FullName == "main.ttw");
            Assert.Contains(zip.Entries, entry => entry.FullName == "Region/r.0.0.7rg");
        }

        [Fact]
        public async Task Save_failure_marks_job_failed_without_publishing_or_cataloging()
        {
            using var directories = new TestDirectories();
            File.WriteAllText(Path.Combine(directories.World, "main.ttw"), "world-state");
            var fixture = CreateFixture(directories, new ThrowingWorldSaveGateway());

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture, "world_save_failed");
        }

        [Fact]
        public async Task Missing_world_source_marks_job_failed_without_an_unpublished_archive()
        {
            using var directories = new TestDirectories();
            var fixture = CreateFixture(directories, new RecordingWorldSaveGateway(new RecordingEvents()));
            Directory.Delete(directories.World, true);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture, "world_source_unavailable");
        }

        [Fact]
        public async Task Non_current_world_payload_is_a_source_failure_and_does_not_trigger_save()
        {
            using var directories = new TestDirectories();
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.WorldBackup, 1);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var save = new RecordingWorldSaveGateway(events);
            var catalog = new RecordingBackupCatalog(events, roots);
            var consumer = new BackgroundWorkConsumer(
                jobs,
                new RecordingPayloadReader(events, running.Id, "RWG"),
                save,
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                catalog,
                "worker-1",
                () => Utc(1),
                TimeSpan.FromMilliseconds(1));

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(JobStatus.Failed, transition.Next);
            Assert.Equal("world_source_unavailable", transition.Completion.ErrorCode);
            Assert.Equal(0, save.CallCount);
            Assert.Empty(catalog.Completed);
        }

        [Fact]
        public async Task Zip_read_failure_has_a_stable_error_and_cleans_temporary_output()
        {
            using var directories = new TestDirectories();
            var source = Path.Combine(directories.World, "main.ttw");
            File.WriteAllText(source, "world-state");
            var fixture = CreateFixture(directories, new RecordingWorldSaveGateway(new RecordingEvents()));
            using var locked = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            AssertFailure(fixture, "backup_zip_failed");
            Assert.Empty(Directory.EnumerateFiles(directories.Backups, "*.tmp", SearchOption.AllDirectories));
        }

        [Fact]
        public void Checksum_mismatch_has_a_stable_error()
        {
            using var directories = new TestDirectories();
            var archive = Path.Combine(directories.Backups, "archive.zip");
            File.WriteAllText(archive, "archive");

            var exception = Assert.Throws<BackupArchiveException>(() =>
                FileSystemBackupArchiveStore.VerifyChecksum(archive, new string('0', 64)));

            Assert.Equal("backup_checksum_failed", exception.ErrorCode);
        }

        [Fact]
        public async Task Seven_days_gateway_dispatches_direct_save_and_commit_confirmation_without_console_output()
        {
            var calls = new System.Collections.Generic.List<string>();
            var gateway = new SevenDaysWorldSaveGateway(
                (operation, action, timeout, token) =>
                {
                    Assert.Equal("7DPanel.Backups.SaveWorld", operation);
                    Assert.Equal(TimeSpan.FromSeconds(5), timeout);
                    calls.Add("dispatch");
                    action();
                    return Task.CompletedTask;
                },
                () => calls.Add("save"),
                () => calls.Add("commit"));

            await gateway.SaveCurrentWorldAsync(TestContext.Current.CancellationToken);

            Assert.Equal(new[] { "dispatch", "save", "commit" }, calls);
            var method = Assert.Single(typeof(IWorldSaveGateway).GetMethods());
            var parameter = Assert.Single(method.GetParameters());
            Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
        }

        private static WorldFixture CreateFixture(TestDirectories directories, IWorldSaveGateway save)
        {
            var events = new RecordingEvents();
            var running = TestJobs.Running(JobKind.WorldBackup, 1);
            var jobs = new RecordingJobStore(events, running);
            var roots = directories.CreateRoots();
            var catalog = new RecordingBackupCatalog(events, roots);
            var consumer = new BackgroundWorkConsumer(
                jobs,
                new RecordingPayloadReader(events, running.Id, "Navezgane"),
                save,
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                catalog,
                "worker-1",
                () => Utc(1),
                TimeSpan.FromMilliseconds(1));
            return new WorldFixture(consumer, jobs, catalog, directories.Backups);
        }

        private static void AssertFailure(WorldFixture fixture, string errorCode)
        {
            var transition = Assert.Single(fixture.Jobs.Transitions);
            Assert.Equal(JobStatus.Failed, transition.Next);
            Assert.Equal(errorCode, transition.Completion.ErrorCode);
            Assert.Empty(fixture.Catalog.Completed);
            Assert.Empty(Directory.EnumerateFiles(fixture.BackupRoot, "*.zip", SearchOption.AllDirectories));
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private sealed record WorldFixture(
            BackgroundWorkConsumer Consumer,
            RecordingJobStore Jobs,
            RecordingBackupCatalog Catalog,
            string BackupRoot);
    }
}
