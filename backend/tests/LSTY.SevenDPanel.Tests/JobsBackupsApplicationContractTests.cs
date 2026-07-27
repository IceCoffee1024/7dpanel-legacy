using System;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests
{
    public sealed class JobsBackupsApplicationContractTests
    {
        [Fact]
        public void Paged_results_preserve_their_cursor_type()
        {
            var jobCursor = new JobCursor(Utc(), Guid.NewGuid());
            var backupCursor = new BackupCursor(Utc(), Guid.NewGuid());

            var jobs = new PagedResult<JobRecord, JobCursor>(Array.Empty<JobRecord>(), jobCursor);
            var backups = new PagedResult<BackupArtifact, BackupCursor>(Array.Empty<BackupArtifact>(), backupCursor);

            Assert.Same(jobCursor, jobs.NextCursor);
            Assert.Same(backupCursor, backups.NextCursor);
            Assert.Equal(
                typeof(PagedResult<JobRecord, JobCursor>),
                typeof(IJobStore).GetMethod(nameof(IJobStore.List))!.ReturnType);
            Assert.Equal(
                typeof(PagedResult<BackupArtifact, BackupCursor>),
                typeof(IBackupCatalog).GetMethod(nameof(IBackupCatalog.List))!.ReturnType);
        }

        [Fact]
        public void Typed_submission_and_row_version_cas_are_application_ports()
        {
            Assert.Equal(7, typeof(IJobSubmissionStore).GetMethods().Length);
            Assert.All(
                typeof(IJobSubmissionStore).GetMethods(),
                method => Assert.Equal(typeof(JobRecord), method.ReturnType));
            Assert.Equal(7, typeof(IJobPayloadReader).GetMethods().Length);

            Assert.NotNull(typeof(IJobStore).GetMethod(
                nameof(IJobStore.TryTransition),
                new[]
                {
                    typeof(Guid), typeof(long), typeof(JobStatus), typeof(JobStatus),
                    typeof(JobCompletion)
                }));
            Assert.Null(typeof(IJobStore).GetMethod(
                nameof(IJobStore.TryTransition),
                new[]
                {
                    typeof(Guid), typeof(JobStatus), typeof(JobStatus),
                    typeof(JobCompletion)
                }));
        }

        private static DateTimeOffset Utc() =>
            new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);
    }
}
