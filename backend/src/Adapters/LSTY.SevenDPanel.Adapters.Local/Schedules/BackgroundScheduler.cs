using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Application.Automations;
using LSTY.SevenDPanel.Application.Schedules;

namespace LSTY.SevenDPanel.Adapters.Local.Schedules
{
    public sealed class BackgroundScheduler
    {
        private readonly IScheduleStore schedules;
        private readonly string ownerId;
        private readonly Func<DateTimeOffset> utcNow;
        private readonly TimeSpan pollInterval;
        private readonly TimeSpan maxFailureBackoff;
        private readonly IAutomationTriggerIngress? automationIngress;
        private readonly BackupPolicyScheduler? backupPolicies;
        private int running;
        private int ticking;

        public BackgroundScheduler(
            IScheduleStore schedules,
            string ownerId,
            Func<DateTimeOffset> utcNow,
            TimeSpan pollInterval,
            TimeSpan maxFailureBackoff,
            IAutomationTriggerIngress? automationIngress = null,
            BackupPolicyScheduler? backupPolicies = null)
        {
            this.schedules = schedules ?? throw new ArgumentNullException(nameof(schedules));
            this.ownerId = string.IsNullOrWhiteSpace(ownerId)
                ? throw new ArgumentException("A scheduler owner id is required.", nameof(ownerId))
                : ownerId.Trim();
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            if (pollInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(pollInterval));
            if (maxFailureBackoff < pollInterval)
                throw new ArgumentOutOfRangeException(nameof(maxFailureBackoff));
            this.pollInterval = pollInterval;
            this.maxFailureBackoff = maxFailureBackoff;
            this.automationIngress = automationIngress;
            this.backupPolicies = backupPolicies;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
                throw new InvalidOperationException("scheduler_already_running");

            try
            {
                var failureBackoff = pollInterval;
                while (!stoppingToken.IsCancellationRequested)
                {
                    TimeSpan delay;
                    try
                    {
                        Tick(stoppingToken);
                        if (stoppingToken.IsCancellationRequested) return;
                        failureBackoff = pollInterval;
                        delay = pollInterval;
                    }
                    catch (Exception) when (!stoppingToken.IsCancellationRequested)
                    {
                        delay = failureBackoff;
                        failureBackoff = IncreaseBackoff(failureBackoff);
                    }

                    try
                    {
                        await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
            finally
            {
                Volatile.Write(ref running, 0);
            }
        }

        public IReadOnlyList<ScheduleRecord> Tick(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested)
                return Array.Empty<ScheduleRecord>();
            if (Interlocked.CompareExchange(ref ticking, 1, 0) != 0)
                throw new InvalidOperationException("scheduler_tick_in_progress");

            try
            {
                var now = RequireUtc(utcNow());
                if (stoppingToken.IsCancellationRequested)
                    return Array.Empty<ScheduleRecord>();
                backupPolicies?.Tick(now);
                var claimed = schedules.ClaimDue(now, ownerId);
                foreach (var schedule in claimed)
                    TryWriteAutomationTrigger(schedule, now);
                return claimed;
            }
            finally
            {
                Volatile.Write(ref ticking, 0);
            }
        }

        private TimeSpan IncreaseBackoff(TimeSpan current)
        {
            if (current >= maxFailureBackoff)
                return maxFailureBackoff;
            if (current.Ticks > maxFailureBackoff.Ticks / 2)
                return maxFailureBackoff;
            return TimeSpan.FromTicks(current.Ticks * 2);
        }

        private void TryWriteAutomationTrigger(ScheduleRecord schedule, DateTimeOffset observedAtUtc)
        {
            if (automationIngress == null || !schedule.LastOccurrenceUtc.HasValue) return;
            var scheduledForUtc = schedule.LastOccurrenceUtc.Value;
            var triggerId = "schedule:" + schedule.Id.ToString("D") + ":" +
                scheduledForUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            var trigger = new AutomationTriggerSnapshot(
                triggerId,
                AutomationTriggerType.Cron.ToString(),
                observedAtUtc,
                null,
                null,
                null,
                null,
                null,
                scheduledForUtc,
                null,
                Array.Empty<string>());
            try { automationIngress.TryWrite(trigger); }
            catch { }
        }

        private static DateTimeOffset RequireUtc(DateTimeOffset value)
        {
            if (value.Offset != TimeSpan.Zero)
                throw new InvalidOperationException("scheduler_clock_not_utc");
            return value;
        }
    }
}
