using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Jobs;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class JobProgressHttpResponse
    {
        public JobProgressHttpResponse(JobProgress progress)
        {
            Current = progress.Current;
            Total = progress.Total;
        }

        public long? Current { get; }
        public long? Total { get; }
    }

    public sealed class JobHttpResponse
    {
        public JobHttpResponse(JobRecord job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            Id = job.Id;
            Kind = job.Kind.ToString();
            Status = job.Status.ToString();
            ActorSubject = job.ActorSubject;
            SourceScheduleId = job.SourceScheduleId;
            CorrelationId = job.CorrelationId;
            CreatedAtUtc = job.CreatedAtUtc;
            StartedAtUtc = job.StartedAtUtc;
            CompletedAtUtc = job.CompletedAtUtc;
            Progress = job.Progress == null ? null : new JobProgressHttpResponse(job.Progress);
            ErrorCode = job.ErrorCode;
            RowVersion = job.RowVersion;
        }

        public Guid Id { get; }
        public string Kind { get; }
        public string Status { get; }
        public string? ActorSubject { get; }
        public Guid? SourceScheduleId { get; }
        public string? CorrelationId { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset? StartedAtUtc { get; }
        public DateTimeOffset? CompletedAtUtc { get; }
        public JobProgressHttpResponse? Progress { get; }
        public string? ErrorCode { get; }
        public long RowVersion { get; }
    }

    public sealed class JobPageHttpResponse
    {
        public JobPageHttpResponse(
            IReadOnlyList<JobRecord> items,
            string? nextCursor)
        {
            Items = (items ?? throw new ArgumentNullException(nameof(items)))
                .Select(item => new JobHttpResponse(item))
                .ToArray();
            NextCursor = nextCursor;
        }

        public IReadOnlyList<JobHttpResponse> Items { get; }
        public string? NextCursor { get; }
    }
}
