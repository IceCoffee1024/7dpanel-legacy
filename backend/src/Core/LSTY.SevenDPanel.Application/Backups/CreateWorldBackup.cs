using System;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed class CreateWorldBackup
    {
        private readonly IJobSubmissionStore submissions;
        private readonly Func<DateTimeOffset> utcNow;

        public CreateWorldBackup(
            IJobSubmissionStore submissions,
            Func<DateTimeOffset> utcNow)
        {
            this.submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public JobRecord Execute(
            string actorSubject,
            string worldName,
            string idempotencyKey,
            string? correlationId)
        {
            var actor = RequireText(actorSubject, nameof(actorSubject));
            var world = RequireText(worldName, nameof(worldName));
            if (world.IndexOf('/') >= 0 || world.IndexOf('\\') >= 0 ||
                world == "." || world == "..")
            {
                throw new ArgumentException("world_name_invalid", nameof(worldName));
            }
            var key = RequireText(idempotencyKey, nameof(idempotencyKey));
            var now = utcNow();
            if (now.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("clock_not_utc");

            return submissions.Enqueue(
                new NewJob(
                    JobKind.WorldBackup,
                    actor,
                    null,
                    key,
                    Normalize(correlationId),
                    now),
                new WorldBackupPayload(world));
        }

        private static string RequireText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty value is required.", parameterName);
            return value.Trim();
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }
}
