using System;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Jobs
{
    public interface IJobStore
    {
        JobRecord Enqueue(NewJob job);
        JobRecord? TryClaimNext(string workerId, DateTimeOffset now);
        bool TryTransition(
            Guid jobId,
            long expectedRowVersion,
            JobStatus expected,
            JobStatus next,
            JobCompletion completion);
        JobRecord Get(Guid jobId);
        PagedResult<JobRecord, JobCursor> List(JobQuery query);
    }
}
