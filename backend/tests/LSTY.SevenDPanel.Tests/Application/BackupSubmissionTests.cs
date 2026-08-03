using System;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Application
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class BackupSubmissionTests
    {
        [Fact]
        public void Panel_database_backup_submits_only_its_typed_payload()
        {
            var submissions = new RecordingSubmissionStore();
            var request = Request("panel-backup");

            var result = new CreatePanelDatabaseBackup(submissions).Execute(request);

            Assert.Same(submissions.Result, result);
            Assert.Equal(JobKind.PanelDatabaseBackup, submissions.Job!.Kind);
            Assert.Equal(request.ActorSubject, submissions.Job.ActorSubject);
            Assert.Equal(request.IdempotencyKey, submissions.Job.IdempotencyKey);
            Assert.Equal(request.CorrelationId, submissions.Job.CorrelationId);
            Assert.Equal(request.RequestedAtUtc, submissions.Job.CreatedAtUtc);
            Assert.IsType<PanelDatabaseBackupPayload>(submissions.Payload);
        }

        [Fact]
        public void Server_configuration_backup_submits_only_its_typed_payload()
        {
            var submissions = new RecordingSubmissionStore();
            var request = Request("configuration-backup");

            var result = new CreateServerConfigurationBackup(submissions).Execute(request);

            Assert.Same(submissions.Result, result);
            Assert.Equal(JobKind.ServerConfigurationBackup, submissions.Job!.Kind);
            Assert.Equal(request.ActorSubject, submissions.Job.ActorSubject);
            Assert.Equal(request.IdempotencyKey, submissions.Job.IdempotencyKey);
            Assert.Equal(request.CorrelationId, submissions.Job.CorrelationId);
            Assert.Equal(request.RequestedAtUtc, submissions.Job.CreatedAtUtc);
            Assert.IsType<ServerConfigurationBackupPayload>(submissions.Payload);
        }

        private static CreateBackupRequest Request(string key) => new CreateBackupRequest(
            "owner", key, "corr-" + key,
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero));

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingSubmissionStore : IJobSubmissionStore
        {
            public JobRecord Result { get; } = new JobRecord(
                Guid.NewGuid(), JobKind.PanelDatabaseBackup, JobStatus.Queued, "owner", null,
                "result", "corr-result", new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero),
                null, null, null, null, null, 0);

            public NewJob? Job { get; private set; }
            public object? Payload { get; private set; }

            public JobRecord Enqueue(NewJob job, WorldBackupPayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, RestorePayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload) => Record(job, payload);
            public JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload) => Record(job, payload);

            private JobRecord Record(NewJob job, object payload)
            {
                Job = job;
                Payload = payload;
                return Result;
            }
        }
    }
}
