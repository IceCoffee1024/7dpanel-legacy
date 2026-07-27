using System;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed class CreateServerConfigurationBackup
    {
        private readonly IJobSubmissionStore submissions;

        public CreateServerConfigurationBackup(IJobSubmissionStore submissions) =>
            this.submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));

        public JobRecord Execute(CreateBackupRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return submissions.Enqueue(
                new NewJob(
                    JobKind.ServerConfigurationBackup,
                    request.ActorSubject,
                    null,
                    request.IdempotencyKey,
                    request.CorrelationId,
                    request.RequestedAtUtc),
                new ServerConfigurationBackupPayload());
        }
    }
}
