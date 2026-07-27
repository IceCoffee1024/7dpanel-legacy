using System;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Application;
using LSTY.SevenDPanel.Application.Announcements;
using LSTY.SevenDPanel.Application.ConsoleCommands;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Jobs;

namespace LSTY.SevenDPanel.Adapters.Local.Schedules
{
    public sealed class ScheduledJobExecutor
    {
        private const string ScheduledActorSubject = "scheduler";
        private const string CommandFailedError = "scheduled_command_failed";
        private const string RestartFailedError = "scheduled_restart_failed";
        private const string AnnouncementFailedError = "scheduled_announcement_failed";
        private const string PayloadMissingError = "scheduled_payload_missing";
        private const string InterruptedError = "scheduled_job_interrupted";

        private readonly IJobStore jobs;
        private readonly IJobPayloadReader payloads;
        private readonly IConsoleCommandGateway consoleCommands;
        private readonly IRestartScriptLauncher restartScripts;
        private readonly IAnnouncementGateway announcements;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;

        public ScheduledJobExecutor(
            IJobStore jobs,
            IJobPayloadReader payloads,
            IConsoleCommandGateway consoleCommands,
            IRestartScriptLauncher restartScripts,
            IAnnouncementGateway announcements,
            Func<DateTimeOffset>? utcNow = null,
            Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
        {
            this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            this.payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
            this.consoleCommands = consoleCommands ??
                throw new ArgumentNullException(nameof(consoleCommands));
            this.restartScripts = restartScripts ??
                throw new ArgumentNullException(nameof(restartScripts));
            this.announcements = announcements ??
                throw new ArgumentNullException(nameof(announcements));
            this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            this.delayAsync = delayAsync ?? DefaultDelayAsync;
        }

        public async Task ExecuteConsoleCommandAsync(
            JobRecord claimed,
            CancellationToken cancellationToken)
        {
            if (claimed == null) throw new ArgumentNullException(nameof(claimed));
            if (CompleteInterruptedIfCancelled(claimed, cancellationToken)) return;

            ConsoleCommandRequest request;
            try
            {
                var payload = payloads.GetScheduledConsoleCommand(claimed.Id);
                ValidateScheduledPayload(claimed, payload.ScheduleId);
                if (string.IsNullOrWhiteSpace(payload.CommandText) ||
                    payload.CommandText.IndexOf('\r') >= 0 ||
                    payload.CommandText.IndexOf('\n') >= 0)
                {
                    throw new InvalidOperationException("scheduled_command_payload_invalid");
                }
                request = new ConsoleCommandRequest(
                    ScheduledActorSubject,
                    payload.CommandText);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, PayloadMissingError);
                return;
            }

            try
            {
                await consoleCommands.ExecuteAsync(request, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Complete(claimed, JobStatus.Interrupted, InterruptedError);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, CommandFailedError);
                return;
            }

            Complete(claimed, JobStatus.Succeeded, null);
        }

        public async Task ExecuteRestartAsync(
            JobRecord claimed,
            CancellationToken cancellationToken)
        {
            if (claimed == null) throw new ArgumentNullException(nameof(claimed));
            if (CompleteInterruptedIfCancelled(claimed, cancellationToken)) return;

            ScheduledRestartPayload payload;
            try
            {
                payload = payloads.GetScheduledRestart(claimed.Id);
                ValidateScheduledPayload(claimed, payload.ScheduleId);
                if (payload.CountdownSeconds < 0 || payload.CountdownSeconds > 86400)
                    throw new InvalidOperationException("scheduled_restart_payload_invalid");
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, PayloadMissingError);
                return;
            }

            try
            {
                if (payload.CountdownSeconds > 0)
                {
                    await delayAsync(
                            TimeSpan.FromSeconds(payload.CountdownSeconds),
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                restartScripts.StartConfiguredScript();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Complete(claimed, JobStatus.Interrupted, InterruptedError);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, RestartFailedError);
                return;
            }

            Complete(claimed, JobStatus.Succeeded, null);
        }

        public async Task ExecuteAnnouncementAsync(
            JobRecord claimed,
            CancellationToken cancellationToken)
        {
            if (claimed == null) throw new ArgumentNullException(nameof(claimed));
            if (CompleteInterruptedIfCancelled(claimed, cancellationToken)) return;

            AnnouncementMessage message;
            try
            {
                var payload = payloads.GetScheduledAnnouncement(claimed.Id);
                ValidateScheduledPayload(claimed, payload.ScheduleId);
                if (string.IsNullOrEmpty(payload.MessageText) ||
                    payload.MessageText.Length > 500)
                {
                    throw new InvalidOperationException(
                        "scheduled_announcement_payload_invalid");
                }
                message = new AnnouncementMessage(payload.MessageText);
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, PayloadMissingError);
                return;
            }

            try
            {
                await announcements.SendAsync(message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Complete(claimed, JobStatus.Interrupted, InterruptedError);
                return;
            }
            catch
            {
                Complete(claimed, JobStatus.Failed, AnnouncementFailedError);
                return;
            }

            Complete(claimed, JobStatus.Succeeded, null);
        }

        private bool CompleteInterruptedIfCancelled(
            JobRecord claimed,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested) return false;
            Complete(claimed, JobStatus.Interrupted, InterruptedError);
            return true;
        }

        private void Complete(
            JobRecord claimed,
            JobStatus next,
            string? errorCode)
        {
            var changed = jobs.TryTransition(
                claimed.Id,
                claimed.RowVersion,
                JobStatus.Running,
                next,
                new JobCompletion(RequireUtc(utcNow()), null, errorCode));
            if (!changed)
                throw new InvalidOperationException("job_state_conflict");
        }

        private static void ValidateScheduledPayload(
            JobRecord claimed,
            Guid scheduleId)
        {
            if (scheduleId == Guid.Empty ||
                !claimed.SourceScheduleId.HasValue ||
                claimed.SourceScheduleId.Value != scheduleId)
            {
                throw new InvalidOperationException("scheduled_payload_invalid");
            }
        }

        private static Task DefaultDelayAsync(
            TimeSpan duration,
            CancellationToken cancellationToken) =>
            Task.Delay(duration, cancellationToken);

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("worker_clock_not_utc");
            return value;
        }
    }
}
