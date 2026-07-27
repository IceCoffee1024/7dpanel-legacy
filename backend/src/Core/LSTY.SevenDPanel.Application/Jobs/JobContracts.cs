using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Jobs
{
    public sealed record NewJob(
        JobKind Kind,
        string? ActorSubject,
        Guid? SourceScheduleId,
        string IdempotencyKey,
        string? CorrelationId,
        DateTimeOffset CreatedAtUtc);

    public sealed record JobProgress(long? Current, long? Total);

    public sealed record JobCompletion(
        DateTimeOffset CompletedAtUtc,
        JobProgress? Progress,
        string? ErrorCode);

    public sealed record JobRecord(
        Guid Id,
        JobKind Kind,
        JobStatus Status,
        string? ActorSubject,
        Guid? SourceScheduleId,
        string IdempotencyKey,
        string? CorrelationId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? CompletedAtUtc,
        JobProgress? Progress,
        string? ErrorCode,
        string? WorkerId,
        long RowVersion);

    public sealed record JobCursor(DateTimeOffset CreatedAtUtc, Guid Id);

    public sealed record JobQuery(
        int PageSize,
        JobKind? Kind,
        JobStatus? Status,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        JobCursor? Cursor);

    public sealed record PagedResult<TItem, TCursor>(
        IReadOnlyList<TItem> Items,
        TCursor? NextCursor)
        where TCursor : class;
}
