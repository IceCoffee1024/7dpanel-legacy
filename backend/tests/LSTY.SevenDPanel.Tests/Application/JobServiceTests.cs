using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Application
{
    public sealed class JobServiceTests
    {
        [Theory]
        [InlineData(JobStatus.Queued)]
        [InlineData(JobStatus.PendingRestart)]
        public void Cancel_moves_only_cancellable_jobs_to_cancelled(JobStatus status)
        {
            var store = new Store(Job(status));
            var service = new JobService(store, () => Utc(1));

            var cancelled = service.Cancel(store.Current.Id);

            Assert.Equal(JobStatus.Cancelled, cancelled.Status);
            Assert.Equal(Utc(1), cancelled.CompletedAtUtc);
        }

        [Theory]
        [InlineData(JobStatus.Running)]
        [InlineData(JobStatus.Succeeded)]
        [InlineData(JobStatus.Failed)]
        public void Cancel_rejects_non_cancellable_jobs(JobStatus status)
        {
            var store = new Store(Job(status));
            var service = new JobService(store, () => Utc(1));

            var error = Assert.Throws<JobNotCancellableException>(
                () => service.Cancel(store.Current.Id));

            Assert.Equal("job_not_cancellable", error.ErrorCode);
            Assert.Equal(status, store.Current.Status);
        }

        [Fact]
        public void Cancel_maps_a_compare_and_set_loss_to_a_stable_conflict()
        {
            var store = new Store(Job(JobStatus.Queued)) { RejectTransition = true };
            var service = new JobService(store, () => Utc(1));

            var error = Assert.Throws<JobNotCancellableException>(
                () => service.Cancel(store.Current.Id));

            Assert.Equal("job_not_cancellable", error.ErrorCode);
        }

        private static JobRecord Job(JobStatus status) => new JobRecord(
            Guid.NewGuid(),
            JobKind.WorldBackup,
            status,
            "owner",
            null,
            "key",
            "correlation",
            Utc(0),
            status == JobStatus.Queued ? null : Utc(0),
            null,
            null,
            null,
            null,
            3);

        private static DateTimeOffset Utc(int day) =>
            new DateTimeOffset(2026, 7, 26, 0, 0, 0, TimeSpan.Zero).AddDays(day);

        private sealed class Store : IJobStore
        {
            public Store(JobRecord current) => Current = current;

            public JobRecord Current { get; private set; }
            public bool RejectTransition { get; set; }

            public JobRecord Enqueue(NewJob job) => throw new NotSupportedException();
            public JobRecord? TryClaimNext(string workerId, DateTimeOffset now) => null;

            public bool TryTransition(
                Guid jobId,
                long expectedRowVersion,
                JobStatus expected,
                JobStatus next,
                JobCompletion completion)
            {
                if (RejectTransition || jobId != Current.Id ||
                    expectedRowVersion != Current.RowVersion || Current.Status != expected)
                {
                    return false;
                }

                Current = Current with
                {
                    Status = next,
                    CompletedAtUtc = completion.CompletedAtUtc,
                    ErrorCode = completion.ErrorCode,
                    RowVersion = Current.RowVersion + 1
                };
                return true;
            }

            public JobRecord Get(Guid jobId) =>
                jobId == Current.Id ? Current : throw new KeyNotFoundException();

            public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
                new PagedResult<JobRecord, JobCursor>(new[] { Current }, null);
        }
    }
}
