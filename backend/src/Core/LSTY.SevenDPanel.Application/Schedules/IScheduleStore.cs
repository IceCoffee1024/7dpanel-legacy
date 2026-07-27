using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Application.Schedules
{
    public abstract record ScheduleAction(JobKind Kind);

    public sealed record ScheduledConsoleCommandAction(string CommandText)
        : ScheduleAction(JobKind.ScheduledConsoleCommand);

    public sealed record ScheduledRestartAction(int CountdownSeconds)
        : ScheduleAction(JobKind.ScheduledRestart);

    public sealed record ScheduledAnnouncementAction(string MessageText)
        : ScheduleAction(JobKind.ScheduledAnnouncement);

    public sealed record ScheduleDefinition(
        Guid Id,
        string Name,
        string CronExpression,
        string TimeZoneId,
        bool Enabled,
        ScheduleConcurrencyPolicy ConcurrencyPolicy,
        ScheduleAction Action,
        long RowVersion)
    {
        public JobKind Kind => Action.Kind;
    }

    public sealed record ScheduleRecord(
        Guid Id,
        string Name,
        string CronExpression,
        string TimeZoneId,
        bool Enabled,
        ScheduleConcurrencyPolicy ConcurrencyPolicy,
        ScheduleAction Action,
        DateTimeOffset? NextOccurrenceUtc,
        DateTimeOffset? LastOccurrenceUtc,
        long RowVersion)
    {
        public JobKind Kind => Action.Kind;
    }

    public sealed record ScheduleRunOutcome(
        Guid Id,
        Guid ScheduleId,
        DateTimeOffset ScheduledForUtc,
        Guid? JobId,
        string Outcome,
        DateTimeOffset CreatedAtUtc);

    public interface IScheduleStore
    {
        IReadOnlyList<ScheduleRecord> List();
        ScheduleRecord? Get(Guid scheduleId);
        ScheduleRecord Upsert(ScheduleDefinition definition);
        bool Delete(Guid scheduleId, long expectedRowVersion);
        IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId);
        void RecordOutcome(ScheduleRunOutcome outcome);
    }
}
