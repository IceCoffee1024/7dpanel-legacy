using System;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed record CreateBackupRequest(
        string? ActorSubject,
        string IdempotencyKey,
        string? CorrelationId,
        DateTimeOffset RequestedAtUtc);

    public sealed class CreatePanelDatabaseBackup
    {
        private readonly IJobSubmissionStore submissions;

        public CreatePanelDatabaseBackup(IJobSubmissionStore submissions) =>
            this.submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));

        public JobRecord Execute(CreateBackupRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return submissions.Enqueue(
                new NewJob(
                    JobKind.PanelDatabaseBackup,
                    request.ActorSubject,
                    null,
                    request.IdempotencyKey,
                    request.CorrelationId,
                    request.RequestedAtUtc),
                new PanelDatabaseBackupPayload());
        }
    }
}
