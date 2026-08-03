using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.WorldOperations;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Local")]
    public sealed class BackgroundWorkConsumerTests
    {
        [Fact]
        public async Task Cancellation_before_claim_stops_without_claiming_new_work()
        {
            using var directories = new TestDirectories();
            var fixture = ConsumerFixture.Create(directories);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.False(await fixture.Consumer.ConsumeNextAsync(cancellation.Token));
            Assert.Equal(0, fixture.Jobs.ClaimCalls);
        }

        [Fact]
        public async Task Consumer_is_bounded_to_one_active_job()
        {
            using var directories = new TestDirectories();
            File.WriteAllText(Path.Combine(directories.World, "main.ttw"), "world-state");
            var events = new RecordingEvents();
            var first = TestJobs.Running(JobKind.WorldBackup, 1);
            var second = TestJobs.Running(JobKind.WorldBackup, 2);
            var jobs = new RecordingJobStore(events, first, second);
            var payloads = new RecordingPayloadReader(events, first.Id, "Navezgane");
            payloads.Worlds[second.Id] = new WorldBackupPayload("Navezgane");
            var save = new BlockingFirstWorldSaveGateway();
            var roots = directories.CreateRoots();
            var consumer = new BackgroundWorkConsumer(
                jobs,
                payloads,
                save,
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                new RecordingBackupCatalog(events, roots),
                "worker-1",
                () => Utc(1),
                TimeSpan.FromMilliseconds(1));

            var firstRun = consumer.ConsumeNextAsync(TestContext.Current.CancellationToken);
            await save.FirstEntered;
            var secondRun = consumer.ConsumeNextAsync(TestContext.Current.CancellationToken);
            await Task.Delay(50, TestContext.Current.CancellationToken);

            Assert.Equal(1, save.CallCount);
            save.ReleaseFirst();
            Assert.True(await firstRun);
            Assert.True(await secondRun);
            Assert.Equal(1, save.MaximumConcurrent);
        }

        [Theory]
        [InlineData(JobKind.PanelDatabaseBackup)]
        [InlineData(JobKind.ServerConfigurationBackup)]
        [InlineData(JobKind.Restore)]
        [InlineData(JobKind.ScheduledConsoleCommand)]
        [InlineData(JobKind.ScheduledRestart)]
        [InlineData(JobKind.ScheduledAnnouncement)]
        [InlineData(JobKind.WorldOperation)]
        public async Task Explicit_unwired_job_kinds_fail_without_dynamic_dispatch(JobKind kind)
        {
            using var directories = new TestDirectories();
            var fixture = ConsumerFixture.Create(directories, TestJobs.Running(kind, 1));

            Assert.True(await fixture.Consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var transition = Assert.Single(fixture.Jobs.Transitions);
            Assert.Equal(JobStatus.Failed, transition.Next);
            Assert.Equal("job_kind_not_wired", transition.Completion.ErrorCode);
            Assert.Equal(0, fixture.Payloads.WorldReads);
        }

        [Theory]
        [InlineData(WorldOperationStatus.Succeeded, JobStatus.Succeeded)]
        [InlineData(WorldOperationStatus.Failed, JobStatus.Failed)]
        [InlineData(WorldOperationStatus.Interrupted, JobStatus.Interrupted)]
        [InlineData(WorldOperationStatus.ResultUnknown, JobStatus.ResultUnknown)]
        public async Task World_operations_reuse_the_single_worker_and_persist_the_handler_terminal_status(
            WorldOperationStatus operationStatus,
            JobStatus jobStatus)
        {
            using var directories = new TestDirectories();
            var claimed = TestJobs.Running(JobKind.WorldOperation, 7);
            var events = new RecordingEvents();
            var jobs = new RecordingJobStore(events, claimed);
            var payloads = new RecordingPayloadReader(events);
            var roots = directories.CreateRoots();
            var handler = new RecordingWorldOperationJobHandler(
                new WorldOperationJobCompletion(
                    operationStatus,
                    operationStatus == WorldOperationStatus.Succeeded ? null : "world_operation_test",
                    new WorldOperationProgress(2, 5)));
            using var consumer = new BackgroundWorkConsumer(
                jobs,
                payloads,
                new RecordingWorldSaveGateway(events),
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                new RecordingBackupCatalog(events, roots),
                "worker-1",
                () => Utc(2),
                TimeSpan.FromMilliseconds(1),
                worldOperations: handler);

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            Assert.Equal(claimed.Id, handler.JobId);
            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(jobStatus, transition.Next);
            Assert.Equal(operationStatus == WorldOperationStatus.Succeeded ? null : "world_operation_test",
                transition.Completion.ErrorCode);
            Assert.Equal(new JobProgress(2, 5), transition.Completion.Progress);
            Assert.Equal(0, payloads.WorldReads);
        }

        [Fact]
        public async Task World_operation_handler_exceptions_become_a_stable_unknown_result()
        {
            using var directories = new TestDirectories();
            var claimed = TestJobs.Running(JobKind.WorldOperation, 8);
            var events = new RecordingEvents();
            var jobs = new RecordingJobStore(events, claimed);
            var payloads = new RecordingPayloadReader(events);
            var roots = directories.CreateRoots();
            using var consumer = new BackgroundWorkConsumer(
                jobs,
                payloads,
                new RecordingWorldSaveGateway(events),
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                new RecordingBackupCatalog(events, roots),
                "worker-1",
                () => Utc(3),
                TimeSpan.FromMilliseconds(1),
                worldOperations: new ThrowingWorldOperationJobHandler());

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(JobStatus.ResultUnknown, transition.Next);
            Assert.Equal("world_operation_result_unknown", transition.Completion.ErrorCode);
            Assert.Null(transition.Completion.Progress);
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingWorldOperationJobHandler : IWorldOperationJobHandler
    {
        private readonly WorldOperationJobCompletion completion;

        public RecordingWorldOperationJobHandler(WorldOperationJobCompletion completion) =>
            this.completion = completion;

        public Guid? JobId { get; private set; }

        public Task<WorldOperationJobCompletion> ExecuteAsync(
            Guid jobId,
            CancellationToken cancellationToken)
        {
            JobId = jobId;
            return Task.FromResult(completion);
        }
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class ThrowingWorldOperationJobHandler : IWorldOperationJobHandler
    {
        public Task<WorldOperationJobCompletion> ExecuteAsync(
            Guid jobId,
            CancellationToken cancellationToken) =>
            Task.FromException<WorldOperationJobCompletion>(
                new InvalidOperationException("raw world operation failure must not escape"));
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class ConsumerFixture
    {
        private ConsumerFixture(
            BackgroundWorkConsumer consumer,
            RecordingJobStore jobs,
            RecordingPayloadReader payloads)
        {
            Consumer = consumer;
            Jobs = jobs;
            Payloads = payloads;
        }

        public BackgroundWorkConsumer Consumer { get; }
        public RecordingJobStore Jobs { get; }
        public RecordingPayloadReader Payloads { get; }

        public static ConsumerFixture Create(TestDirectories directories, params JobRecord[] jobsToClaim)
        {
            var events = new RecordingEvents();
            var jobs = new RecordingJobStore(events, jobsToClaim);
            var payloads = new RecordingPayloadReader(events);
            foreach (var job in jobsToClaim.Where(job => job.Kind == JobKind.WorldBackup))
                payloads.Worlds[job.Id] = new WorldBackupPayload("Navezgane");
            var roots = directories.CreateRoots();
            return new ConsumerFixture(
                new BackgroundWorkConsumer(
                    jobs,
                    payloads,
                    new RecordingWorldSaveGateway(events),
                    new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                    new RecordingBackupCatalog(events, roots),
                    "worker-1",
                    () => new DateTimeOffset(2026, 7, 26, 0, 1, 0, TimeSpan.Zero),
                    TimeSpan.FromMilliseconds(1)),
                jobs,
                payloads);
        }
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class TestDirectories : IDisposable
    {
        public TestDirectories()
        {
            Root = Path.Combine(Path.GetTempPath(), "7dpanel-world-backup-tests", Guid.NewGuid().ToString("N"));
            World = Path.Combine(Root, "world");
            Panel = Path.Combine(Root, "panel");
            Configuration = Path.Combine(Root, "configuration");
            Backups = Path.Combine(Root, "backups");
            Directory.CreateDirectory(World);
            Directory.CreateDirectory(Panel);
            Directory.CreateDirectory(Configuration);
            Directory.CreateDirectory(Backups);
        }

        public string Root { get; }
        public string World { get; }
        public string Panel { get; }
        public string Configuration { get; }
        public string Backups { get; }

        public ApprovedStorageRoots CreateRoots() => new ApprovedStorageRoots(
            "Navezgane",
            World,
            Panel,
            Configuration,
            "primary",
            Backups,
            "v3.0.1-b4");

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal static class TestJobs
    {
        public static JobRecord Running(JobKind kind, int seed)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(seed).CopyTo(bytes, 0);
            return new JobRecord(
                new Guid(bytes),
                kind,
                JobStatus.Running,
                "owner",
                null,
                "job-" + seed,
                "corr-" + seed,
                new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 26, 0, 1, 0, TimeSpan.Zero),
                null,
                null,
                null,
                "worker-1",
                1);
        }
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingEvents
    {
        public List<string> Items { get; } = new List<string>();
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingJobStore : IJobStore
    {
        private readonly Queue<JobRecord> claims;
        private readonly RecordingEvents events;

        public RecordingJobStore(RecordingEvents events, params JobRecord[] claims)
        {
            this.events = events;
            this.claims = new Queue<JobRecord>(claims);
        }

        public int ClaimCalls { get; private set; }
        public List<RecordedTransition> Transitions { get; } = new List<RecordedTransition>();

        public JobRecord Enqueue(NewJob job) => throw new NotSupportedException();

        public JobRecord? TryClaimNext(string workerId, DateTimeOffset now)
        {
            ClaimCalls++;
            events.Items.Add("claim");
            return claims.Count == 0 ? null : claims.Dequeue();
        }

        public bool TryTransition(
            Guid jobId,
            long expectedRowVersion,
            JobStatus expected,
            JobStatus next,
            JobCompletion completion)
        {
            events.Items.Add("transition:" + next);
            Transitions.Add(new RecordedTransition(jobId, expectedRowVersion, expected, next, completion));
            return true;
        }

        public JobRecord Get(Guid jobId) => throw new NotSupportedException();

        public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
            new PagedResult<JobRecord, JobCursor>(Array.Empty<JobRecord>(), null);
    }

    internal sealed record RecordedTransition(
        Guid JobId,
        long ExpectedRowVersion,
        JobStatus Expected,
        JobStatus Next,
        JobCompletion Completion);

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingSubmissionStore : IJobSubmissionStore
    {
        public RecordingSubmissionStore()
        {
            Returned = new JobRecord(
                Guid.NewGuid(), JobKind.WorldBackup, JobStatus.Queued, "owner", null,
                "backup-1", "corr-1", DateTimeOffset.UtcNow, null, null, null, null, null, 0);
        }

        public JobRecord Returned { get; }
        public NewJob? Job { get; private set; }
        public WorldBackupPayload? WorldPayload { get; private set; }
        public int WorldCalls { get; private set; }

        public JobRecord Enqueue(NewJob job, WorldBackupPayload payload)
        {
            Job = job;
            WorldPayload = payload;
            WorldCalls++;
            return Returned;
        }

        public JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload) => throw new NotSupportedException();
        public JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload) => throw new NotSupportedException();
        public JobRecord Enqueue(NewJob job, RestorePayload payload) => throw new NotSupportedException();
        public JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload) => throw new NotSupportedException();
        public JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload) => throw new NotSupportedException();
        public JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload) => throw new NotSupportedException();
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingPayloadReader : IJobPayloadReader
    {
        private readonly RecordingEvents events;

        public RecordingPayloadReader(RecordingEvents events)
        {
            this.events = events;
        }

        public RecordingPayloadReader(RecordingEvents events, Guid jobId, string worldName)
            : this(events)
        {
            Worlds[jobId] = new WorldBackupPayload(worldName);
        }

        public Dictionary<Guid, WorldBackupPayload> Worlds { get; } = new Dictionary<Guid, WorldBackupPayload>();
        public int WorldReads { get; private set; }

        public WorldBackupPayload GetWorldBackup(Guid jobId)
        {
            WorldReads++;
            events.Items.Add("payload");
            return Worlds[jobId];
        }

        public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId) => throw new NotSupportedException();
        public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId) => throw new NotSupportedException();
        public RestorePayload GetRestore(Guid jobId) => throw new NotSupportedException();
        public ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId) => throw new NotSupportedException();
        public ScheduledRestartPayload GetScheduledRestart(Guid jobId) => throw new NotSupportedException();
        public ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId) => throw new NotSupportedException();
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal class RecordingWorldSaveGateway : IWorldSaveGateway
    {
        private readonly RecordingEvents events;

        public RecordingWorldSaveGateway(RecordingEvents events)
        {
            this.events = events;
        }

        public int CallCount { get; private set; }

        public virtual Task SaveCurrentWorldAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            events.Items.Add("save");
            return Task.CompletedTask;
        }
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class ThrowingWorldSaveGateway : IWorldSaveGateway
    {
        public Task SaveCurrentWorldAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("raw save failure must not escape"));
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class BlockingFirstWorldSaveGateway : IWorldSaveGateway
    {
        private readonly TaskCompletionSource<bool> firstEntered =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> releaseFirst =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        private int maximumConcurrent;

        public Task FirstEntered => firstEntered.Task;
        public int CallCount { get; private set; }
        public int MaximumConcurrent => maximumConcurrent;

        public async Task SaveCurrentWorldAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            var nowActive = Interlocked.Increment(ref active);
            if (nowActive > maximumConcurrent) maximumConcurrent = nowActive;
            try
            {
                if (CallCount == 1)
                {
                    firstEntered.TrySetResult(true);
                    await releaseFirst.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        public void ReleaseFirst() => releaseFirst.TrySetResult(true);
    }

    [Trait("Capability", "Operations")]

    [Trait("Boundary", "Local")]

    internal sealed class RecordingBackupCatalog : IBackupCatalog
    {
        private readonly RecordingEvents events;
        private readonly ApprovedStorageRoots roots;

        public RecordingBackupCatalog(RecordingEvents events, ApprovedStorageRoots roots)
        {
            this.events = events;
            this.roots = roots;
        }

        public List<CompletedBackup> Completed { get; } = new List<CompletedBackup>();

        public BackupArtifact Add(CompletedBackup backup)
        {
            Assert.True(File.Exists(roots.ResolveBackupResource(backup.RelativeResourceId)));
            events.Items.Add("catalog");
            Completed.Add(backup);
            return ToArtifact(backup);
        }

        public BackupArtifact Get(Guid backupId) =>
            ToArtifact(Completed.Single(backup => backup.Id == backupId));

        public PagedResult<BackupArtifact, BackupCursor> List(BackupQuery query) =>
            new PagedResult<BackupArtifact, BackupCursor>(Completed.Select(ToArtifact).ToArray(), null);

        public bool Delete(Guid backupId) => false;

        private static BackupArtifact ToArtifact(CompletedBackup backup) => new BackupArtifact(
            backup.Id,
            backup.Kind,
            backup.BackupRootId,
            backup.RelativeResourceId,
            backup.SizeBytes,
            backup.Sha256,
            backup.WorldId,
            backup.GameVersion,
            backup.ValidationStatus,
            backup.CreatedAtUtc,
            backup.SourceJobId,
            backup.ManifestVersion);
    }
}
