using System;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed record RestoreMergeJobSnapshot(
        Guid JobId,
        JobKind JobKind,
        JobStatus JobStatus,
        string? ActorSubject,
        string IdempotencyKey,
        string? CorrelationId,
        DateTimeOffset CreatedAtUtc);

    public interface IRestoreResultMergeStore
    {
        void MergeOnce(
            RestoreMergeJobSnapshot snapshot,
            RestorePayload payload,
            JobStatus status,
            JobCompletion completion);
    }
}
