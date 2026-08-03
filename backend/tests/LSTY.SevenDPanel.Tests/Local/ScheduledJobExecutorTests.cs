using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Local.Jobs;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Local")]
    public sealed class ScheduledJobExecutorTests
    {
        [Fact]
        public async Task Console_command_uses_stable_actor_exact_text_and_cas_success()
        {
            var job = Running(JobKind.ScheduledConsoleCommand, 1);
            var fixture = ScheduledFixture.Create(job);
            const string command = "teleportplayer owner 10 20 30";
            fixture.Payloads.Commands[job.Id] = new ScheduledConsoleCommandPayload(
                job.SourceScheduleId!.Value,
                command);
            fixture.Console.Output.Add("private command output");
            using var cancellation = new CancellationTokenSource();

            await fixture.Executor.ExecuteConsoleCommandAsync(job, cancellation.Token);

            var request = Assert.Single(fixture.Console.Requests);
            Assert.Equal("scheduler", request.ActorSubject);
            Assert.Equal(command, request.Command);
            Assert.Equal(cancellation.Token, Assert.Single(fixture.Console.Tokens));
            AssertTerminal(fixture.Jobs, job, JobStatus.Succeeded, null);
        }

        [Fact]
        public async Task Zero_countdown_starts_configured_restart_without_delay()
        {
            var job = Running(JobKind.ScheduledRestart, 2);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Restarts[job.Id] = new ScheduledRestartPayload(
                job.SourceScheduleId!.Value,
                0);

            await fixture.Executor.ExecuteRestartAsync(job, TestContext.Current.CancellationToken);

            Assert.Empty(fixture.Delays);
            Assert.Equal(1, fixture.Restarts.CallCount);
            AssertTerminal(fixture.Jobs, job, JobStatus.Succeeded, null);
        }

        [Fact]
        public async Task Positive_countdown_awaits_injected_delay_before_restart()
        {
            var job = Running(JobKind.ScheduledRestart, 3);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Restarts[job.Id] = new ScheduledRestartPayload(
                job.SourceScheduleId!.Value,
                17);
            using var cancellation = new CancellationTokenSource();

            await fixture.Executor.ExecuteRestartAsync(job, cancellation.Token);

            var delay = Assert.Single(fixture.Delays);
            Assert.Equal(TimeSpan.FromSeconds(17), delay.Duration);
            Assert.Equal(cancellation.Token, delay.CancellationToken);
            Assert.Equal(new[] { "delay", "restart" }, fixture.Events);
            AssertTerminal(fixture.Jobs, job, JobStatus.Succeeded, null);
        }

        [Fact]
        public async Task Announcement_gateway_receives_exact_plain_text_and_token()
        {
            var job = Running(JobKind.ScheduledAnnouncement, 4);
            var fixture = ScheduledFixture.Create(job);
            const string message = "Blood moon at 22:00 <plain>&unchanged";
            fixture.Payloads.Announcements[job.Id] = new ScheduledAnnouncementPayload(
                job.SourceScheduleId!.Value,
                message);
            using var cancellation = new CancellationTokenSource();

            await fixture.Executor.ExecuteAnnouncementAsync(job, cancellation.Token);

            Assert.Equal(message, Assert.Single(fixture.Announcements.Messages).MessageText);
            Assert.Equal(cancellation.Token, Assert.Single(fixture.Announcements.Tokens));
            AssertTerminal(fixture.Jobs, job, JobStatus.Succeeded, null);
        }

        [Fact]
        public async Task Countdown_cancellation_interrupts_job_without_starting_script()
        {
            var job = Running(JobKind.ScheduledRestart, 5);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Restarts[job.Id] = new ScheduledRestartPayload(
                job.SourceScheduleId!.Value,
                30);
            using var cancellation = new CancellationTokenSource();
            fixture.DelayOverride = (_, token) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(token);
            };

            await fixture.Executor.ExecuteRestartAsync(job, cancellation.Token);

            Assert.Equal(0, fixture.Restarts.CallCount);
            AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Interrupted,
                "scheduled_job_interrupted");
        }

        [Fact]
        public async Task Console_gateway_failure_uses_stable_error_without_command_or_output()
        {
            var job = Running(JobKind.ScheduledConsoleCommand, 6);
            var fixture = ScheduledFixture.Create(job);
            const string command = "secret command body";
            fixture.Payloads.Commands[job.Id] = new ScheduledConsoleCommandPayload(
                job.SourceScheduleId!.Value,
                command);
            fixture.Console.Failure = new InvalidOperationException(
                command + " private gateway output");

            await fixture.Executor.ExecuteConsoleCommandAsync(
                job,
                TestContext.Current.CancellationToken);

            var transition = AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Failed,
                "scheduled_command_failed");
            Assert.DoesNotContain(command, transition.Completion.ErrorCode!);
            Assert.DoesNotContain("output", transition.Completion.ErrorCode!);
        }

        [Fact]
        public async Task Announcement_gateway_failure_uses_stable_error_without_message()
        {
            var job = Running(JobKind.ScheduledAnnouncement, 7);
            var fixture = ScheduledFixture.Create(job);
            const string message = "private announcement body";
            fixture.Payloads.Announcements[job.Id] = new ScheduledAnnouncementPayload(
                job.SourceScheduleId!.Value,
                message);
            fixture.Announcements.Failure = new InvalidOperationException(message);

            await fixture.Executor.ExecuteAnnouncementAsync(
                job,
                TestContext.Current.CancellationToken);

            var transition = AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Failed,
                "scheduled_announcement_failed");
            Assert.DoesNotContain(message, transition.Completion.ErrorCode!);
        }

        [Fact]
        public async Task Restart_launcher_failure_uses_stable_error()
        {
            var job = Running(JobKind.ScheduledRestart, 8);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Restarts[job.Id] = new ScheduledRestartPayload(
                job.SourceScheduleId!.Value,
                0);
            fixture.Restarts.Failure = new InvalidOperationException(
                "private configured script failure");

            await fixture.Executor.ExecuteRestartAsync(
                job,
                TestContext.Current.CancellationToken);

            AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Failed,
                "scheduled_restart_failed");
        }

        [Theory]
        [InlineData(JobKind.ScheduledConsoleCommand)]
        [InlineData(JobKind.ScheduledRestart)]
        [InlineData(JobKind.ScheduledAnnouncement)]
        public async Task Missing_typed_payload_uses_stable_error_and_skips_side_effect(
            JobKind kind)
        {
            var job = Running(kind, 9 + (int)kind);
            var fixture = ScheduledFixture.Create(job);

            await Execute(fixture.Executor, job, TestContext.Current.CancellationToken);

            Assert.Equal(kind, Assert.Single(fixture.Payloads.ReadKinds));
            Assert.Empty(fixture.Console.Requests);
            Assert.Equal(0, fixture.Restarts.CallCount);
            Assert.Empty(fixture.Announcements.Messages);
            AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Failed,
                "scheduled_payload_missing");
        }

        [Fact]
        public async Task Invalid_typed_payload_uses_stable_payload_error()
        {
            var job = Running(JobKind.ScheduledConsoleCommand, 20);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Commands[job.Id] = new ScheduledConsoleCommandPayload(
                job.SourceScheduleId!.Value,
                "say first\nsay second");

            await fixture.Executor.ExecuteConsoleCommandAsync(
                job,
                TestContext.Current.CancellationToken);

            Assert.Empty(fixture.Console.Requests);
            AssertTerminal(
                fixture.Jobs,
                job,
                JobStatus.Failed,
                "scheduled_payload_missing");
        }

        [Fact]
        public async Task Cas_conflict_throws_with_claimed_row_version()
        {
            var job = Running(JobKind.ScheduledAnnouncement, 21);
            var fixture = ScheduledFixture.Create(job);
            fixture.Payloads.Announcements[job.Id] = new ScheduledAnnouncementPayload(
                job.SourceScheduleId!.Value,
                "exact message");
            fixture.Jobs.TransitionResult = false;

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.Executor.ExecuteAnnouncementAsync(
                    job,
                    TestContext.Current.CancellationToken));

            Assert.Equal("job_state_conflict", exception.Message);
            var transition = Assert.Single(fixture.Jobs.Transitions);
            Assert.Equal(job.RowVersion, transition.ExpectedRowVersion);
            Assert.Equal(JobStatus.Running, transition.Expected);
            Assert.Equal(JobStatus.Succeeded, transition.Next);
        }

        [Fact]
        public async Task Consumer_explicitly_routes_all_three_scheduled_kinds()
        {
            var commandJob = Running(JobKind.ScheduledConsoleCommand, 22);
            var restartJob = Running(JobKind.ScheduledRestart, 23);
            var announcementJob = Running(JobKind.ScheduledAnnouncement, 24);
            var fixture = ScheduledFixture.Create(commandJob, restartJob, announcementJob);
            fixture.Payloads.Commands[commandJob.Id] = new ScheduledConsoleCommandPayload(
                commandJob.SourceScheduleId!.Value,
                "status");
            fixture.Payloads.Restarts[restartJob.Id] = new ScheduledRestartPayload(
                restartJob.SourceScheduleId!.Value,
                0);
            fixture.Payloads.Announcements[announcementJob.Id] = new ScheduledAnnouncementPayload(
                announcementJob.SourceScheduleId!.Value,
                "server message");
            using var directories = new TestDirectories();
            var events = new RecordingEvents();
            var roots = directories.CreateRoots();
            using var consumer = new BackgroundWorkConsumer(
                fixture.Jobs,
                fixture.Payloads,
                new RecordingWorldSaveGateway(events),
                new FileSystemBackupArchiveStore(roots, new AtomicFileWriter(roots)),
                new RecordingBackupCatalog(events, roots),
                "worker-1",
                () => Utc(2),
                TimeSpan.FromMilliseconds(1),
                scheduledJobs: fixture.Executor);

            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));
            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));
            Assert.True(await consumer.ConsumeNextAsync(TestContext.Current.CancellationToken));

            Assert.Equal("status", Assert.Single(fixture.Console.Requests).Command);
            Assert.Equal(1, fixture.Restarts.CallCount);
            Assert.Equal(
                "server message",
                Assert.Single(fixture.Announcements.Messages).MessageText);
            Assert.Collection(
                fixture.Jobs.Transitions,
                transition => Assert.Equal(commandJob.Id, transition.JobId),
                transition => Assert.Equal(restartJob.Id, transition.JobId),
                transition => Assert.Equal(announcementJob.Id, transition.JobId));
        }

        private static Task Execute(
            ScheduledJobExecutor executor,
            JobRecord job,
            CancellationToken cancellationToken)
        {
            switch (job.Kind)
            {
                case JobKind.ScheduledConsoleCommand:
                    return executor.ExecuteConsoleCommandAsync(job, cancellationToken);
                case JobKind.ScheduledRestart:
                    return executor.ExecuteRestartAsync(job, cancellationToken);
                case JobKind.ScheduledAnnouncement:
                    return executor.ExecuteAnnouncementAsync(job, cancellationToken);
                default:
                    throw new ArgumentOutOfRangeException(nameof(job));
            }
        }

        private static RecordedScheduledTransition AssertTerminal(
            RecordingScheduledJobStore jobs,
            JobRecord job,
            JobStatus expectedStatus,
            string? expectedError)
        {
            var transition = Assert.Single(jobs.Transitions);
            Assert.Equal(job.Id, transition.JobId);
            Assert.Equal(job.RowVersion, transition.ExpectedRowVersion);
            Assert.Equal(JobStatus.Running, transition.Expected);
            Assert.Equal(expectedStatus, transition.Next);
            Assert.Equal(Utc(2), transition.Completion.CompletedAtUtc);
            Assert.Null(transition.Completion.Progress);
            Assert.Equal(expectedError, transition.Completion.ErrorCode);
            return transition;
        }

        private static JobRecord Running(JobKind kind, int seed)
        {
            var jobId = GuidFromSeed(seed);
            var scheduleId = GuidFromSeed(seed + 1000);
            return new JobRecord(
                jobId,
                kind,
                JobStatus.Running,
                null,
                scheduleId,
                "scheduled-job-" + seed,
                "scheduled-correlation-" + seed,
                Utc(0),
                Utc(1),
                null,
                null,
                null,
                "worker-1",
                seed + 10);
        }

        private static Guid GuidFromSeed(int seed)
        {
            var bytes = new byte[16];
            BitConverter.GetBytes(seed).CopyTo(bytes, 0);
            return new Guid(bytes);
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class ScheduledFixture
        {
            private ScheduledFixture(params JobRecord[] claims)
            {
                Jobs = new RecordingScheduledJobStore(claims);
                Payloads = new RecordingScheduledPayloadReader();
                Console = new RecordingConsoleCommandGateway(Events);
                Restarts = new RecordingRestartScriptLauncher(Events);
                Announcements = new RecordingAnnouncementGateway(Events);
                Executor = new ScheduledJobExecutor(
                    Jobs,
                    Payloads,
                    Console,
                    Restarts,
                    Announcements,
                    () => Utc(2),
                    DelayAsync);
            }

            public List<string> Events { get; } = new List<string>();
            public List<RecordedDelay> Delays { get; } = new List<RecordedDelay>();
            public Func<TimeSpan, CancellationToken, Task>? DelayOverride { get; set; }
            public RecordingScheduledJobStore Jobs { get; }
            public RecordingScheduledPayloadReader Payloads { get; }
            public RecordingConsoleCommandGateway Console { get; }
            public RecordingRestartScriptLauncher Restarts { get; }
            public RecordingAnnouncementGateway Announcements { get; }
            public ScheduledJobExecutor Executor { get; }

            public static ScheduledFixture Create(params JobRecord[] claims) =>
                new ScheduledFixture(claims);

            private Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
            {
                Delays.Add(new RecordedDelay(duration, cancellationToken));
                Events.Add("delay");
                return DelayOverride == null
                    ? Task.CompletedTask
                    : DelayOverride(duration, cancellationToken);
            }
        }

        private sealed record RecordedDelay(
            TimeSpan Duration,
            CancellationToken CancellationToken);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingScheduledJobStore : IJobStore
        {
            private readonly Queue<JobRecord> claims;

            public RecordingScheduledJobStore(IEnumerable<JobRecord> claims)
            {
                this.claims = new Queue<JobRecord>(claims);
            }

            public bool TransitionResult { get; set; } = true;
            public List<RecordedScheduledTransition> Transitions { get; } =
                new List<RecordedScheduledTransition>();

            public JobRecord Enqueue(NewJob job) => throw new NotSupportedException();

            public JobRecord? TryClaimNext(string workerId, DateTimeOffset now) =>
                claims.Count == 0 ? null : claims.Dequeue();

            public bool TryTransition(
                Guid jobId,
                long expectedRowVersion,
                JobStatus expected,
                JobStatus next,
                JobCompletion completion)
            {
                Transitions.Add(new RecordedScheduledTransition(
                    jobId,
                    expectedRowVersion,
                    expected,
                    next,
                    completion));
                return TransitionResult;
            }

            public JobRecord Get(Guid jobId) => throw new NotSupportedException();

            public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
                new PagedResult<JobRecord, JobCursor>(Array.Empty<JobRecord>(), null);
        }

        private sealed record RecordedScheduledTransition(
            Guid JobId,
            long ExpectedRowVersion,
            JobStatus Expected,
            JobStatus Next,
            JobCompletion Completion);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingScheduledPayloadReader : IJobPayloadReader
        {
            public Dictionary<Guid, ScheduledConsoleCommandPayload> Commands { get; } =
                new Dictionary<Guid, ScheduledConsoleCommandPayload>();
            public Dictionary<Guid, ScheduledRestartPayload> Restarts { get; } =
                new Dictionary<Guid, ScheduledRestartPayload>();
            public Dictionary<Guid, ScheduledAnnouncementPayload> Announcements { get; } =
                new Dictionary<Guid, ScheduledAnnouncementPayload>();
            public List<JobKind> ReadKinds { get; } = new List<JobKind>();

            public WorldBackupPayload GetWorldBackup(Guid jobId) =>
                throw new NotSupportedException();

            public PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId) =>
                throw new NotSupportedException();

            public ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId) =>
                throw new NotSupportedException();

            public RestorePayload GetRestore(Guid jobId) =>
                throw new NotSupportedException();

            public ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId)
            {
                ReadKinds.Add(JobKind.ScheduledConsoleCommand);
                return Commands[jobId];
            }

            public ScheduledRestartPayload GetScheduledRestart(Guid jobId)
            {
                ReadKinds.Add(JobKind.ScheduledRestart);
                return Restarts[jobId];
            }

            public ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId)
            {
                ReadKinds.Add(JobKind.ScheduledAnnouncement);
                return Announcements[jobId];
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingConsoleCommandGateway : IConsoleCommandGateway
        {
            private readonly List<string> events;

            public RecordingConsoleCommandGateway(List<string> events)
            {
                this.events = events;
            }

            public Exception? Failure { get; set; }
            public List<string> Output { get; } = new List<string>();
            public List<ConsoleCommandRequest> Requests { get; } =
                new List<ConsoleCommandRequest>();
            public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

            public Task<ConsoleCommandResult> ExecuteAsync(
                ConsoleCommandRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                Tokens.Add(cancellationToken);
                events.Add("command");
                return Failure == null
                    ? Task.FromResult(new ConsoleCommandResult(request.Command, Output))
                    : Task.FromException<ConsoleCommandResult>(Failure);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingRestartScriptLauncher : IRestartScriptLauncher
        {
            private readonly List<string> events;

            public RecordingRestartScriptLauncher(List<string> events)
            {
                this.events = events;
            }

            public Exception? Failure { get; set; }
            public int CallCount { get; private set; }

            public DateTimeOffset StartConfiguredScript()
            {
                CallCount++;
                events.Add("restart");
                if (Failure != null) throw Failure;
                return Utc(2);
            }
        }

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Local")]

        private sealed class RecordingAnnouncementGateway : IAnnouncementGateway
        {
            private readonly List<string> events;

            public RecordingAnnouncementGateway(List<string> events)
            {
                this.events = events;
            }

            public Exception? Failure { get; set; }
            public List<AnnouncementMessage> Messages { get; } =
                new List<AnnouncementMessage>();
            public List<CancellationToken> Tokens { get; } = new List<CancellationToken>();

            public Task SendAsync(
                AnnouncementMessage message,
                CancellationToken cancellationToken)
            {
                Messages.Add(message);
                Tokens.Add(cancellationToken);
                events.Add("announcement");
                return Failure == null
                    ? Task.CompletedTask
                    : Task.FromException(Failure);
            }
        }
    }
}
