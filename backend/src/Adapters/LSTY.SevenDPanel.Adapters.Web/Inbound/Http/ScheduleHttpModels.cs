using System;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Adapters.Web.Inbound.Http
{
    public sealed class ScheduleWriteHttpRequest
    {
        public string? Name { get; set; }
        public string? CronExpression { get; set; }
        public string? TimeZoneId { get; set; }
        public bool Enabled { get; set; }
        public string? ConcurrencyPolicy { get; set; }
        public string? Kind { get; set; }
        public string? CommandText { get; set; }
        public int? CountdownSeconds { get; set; }
        public string? MessageText { get; set; }
        public long RowVersion { get; set; }

        public ScheduleConcurrencyPolicy ParseConcurrencyPolicy()
        {
            if (!Enum.TryParse(ConcurrencyPolicy, false, out ScheduleConcurrencyPolicy value) ||
                !Enum.IsDefined(typeof(ScheduleConcurrencyPolicy), value))
            {
                throw new ScheduleValidationException("schedule_invalid");
            }
            return value;
        }

        public ScheduleAction ParseAction()
        {
            switch (Kind)
            {
                case "ScheduledConsoleCommand"
                    when CountdownSeconds == null && MessageText == null:
                    return new ScheduledConsoleCommandAction(CommandText ?? string.Empty);
                case "ScheduledRestart"
                    when CommandText == null && MessageText == null && CountdownSeconds.HasValue:
                    return new ScheduledRestartAction(CountdownSeconds.Value);
                case "ScheduledAnnouncement"
                    when CommandText == null && CountdownSeconds == null:
                    return new ScheduledAnnouncementAction(MessageText ?? string.Empty);
                default:
                    throw new ScheduleValidationException("schedule_invalid");
            }
        }
    }

    public sealed class ScheduleRowVersionHttpRequest
    {
        public long RowVersion { get; set; }
    }

    public sealed class ScheduleHttpResponse
    {
        public ScheduleHttpResponse(ScheduleRecord schedule)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            Id = schedule.Id;
            Name = schedule.Name;
            CronExpression = schedule.CronExpression;
            TimeZoneId = schedule.TimeZoneId;
            Enabled = schedule.Enabled;
            ConcurrencyPolicy = schedule.ConcurrencyPolicy.ToString();
            Kind = schedule.Kind.ToString();
            NextOccurrenceUtc = schedule.NextOccurrenceUtc;
            LastOccurrenceUtc = schedule.LastOccurrenceUtc;
            RowVersion = schedule.RowVersion;
            switch (schedule.Action)
            {
                case ScheduledConsoleCommandAction command:
                    CommandText = command.CommandText;
                    break;
                case ScheduledRestartAction restart:
                    CountdownSeconds = restart.CountdownSeconds;
                    break;
                case ScheduledAnnouncementAction announcement:
                    MessageText = announcement.MessageText;
                    break;
            }
        }

        public Guid Id { get; }
        public string Name { get; }
        public string CronExpression { get; }
        public string TimeZoneId { get; }
        public bool Enabled { get; }
        public string ConcurrencyPolicy { get; }
        public string Kind { get; }
        public string? CommandText { get; }
        public int? CountdownSeconds { get; }
        public string? MessageText { get; }
        public DateTimeOffset? NextOccurrenceUtc { get; }
        public DateTimeOffset? LastOccurrenceUtc { get; }
        public long RowVersion { get; }
    }

    public sealed class AnnouncementHttpRequest
    {
        public string? MessageText { get; set; }
    }
}
