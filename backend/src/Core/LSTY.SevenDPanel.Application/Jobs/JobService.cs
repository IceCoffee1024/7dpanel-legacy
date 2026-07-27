using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Jobs
{
    public sealed class JobService
    {
        private readonly IJobStore store;
        private readonly Func<DateTimeOffset> utcNow;

        public JobService(IJobStore store, Func<DateTimeOffset> utcNow)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public JobRecord Get(Guid jobId)
        {
            if (jobId == Guid.Empty) throw new JobNotFoundException();
            try { return store.Get(jobId); }
            catch (KeyNotFoundException exception) { throw new JobNotFoundException(exception); }
        }

        public PagedResult<JobRecord, JobCursor> List(JobQuery query) =>
            store.List(query ?? throw new ArgumentNullException(nameof(query)));

        public JobRecord Cancel(Guid jobId)
        {
            var current = Get(jobId);
            if (current.Status != JobStatus.Queued &&
                current.Status != JobStatus.PendingRestart)
            {
                throw new JobNotCancellableException();
            }

            var now = utcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("job_clock_not_utc");
            if (!store.TryTransition(
                    current.Id,
                    current.RowVersion,
                    current.Status,
                    JobStatus.Cancelled,
                    new JobCompletion(now, current.Progress, null)))
            {
                throw new JobNotCancellableException();
            }

            return Get(current.Id);
        }
    }

    public sealed class JobNotFoundException : Exception
    {
        public const string Code = "job_not_found";

        public JobNotFoundException()
            : base(Code)
        {
        }

        public JobNotFoundException(Exception innerException)
            : base(Code, innerException)
        {
        }

        public string ErrorCode => Code;
    }

    public sealed class JobNotCancellableException : Exception
    {
        public const string Code = "job_not_cancellable";

        public JobNotCancellableException()
            : base(Code)
        {
        }

        public string ErrorCode => Code;
    }
}
