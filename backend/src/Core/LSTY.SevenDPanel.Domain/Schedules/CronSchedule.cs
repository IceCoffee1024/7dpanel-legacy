using System;
using Cronos;

namespace LSTY.SevenDPanel.Domain.Schedules
{
    public sealed class CronSchedule
    {
        private readonly CronExpression cron;
        private readonly TimeZoneInfo timeZone;

        private CronSchedule(string expression, TimeZoneInfo timeZone, CronExpression cron)
        {
            Expression = expression;
            TimeZoneId = timeZone.Id;
            this.timeZone = timeZone;
            this.cron = cron;
        }

        public string Expression { get; }
        public string TimeZoneId { get; }

        public static CronSchedule Create(string expression, string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new CronScheduleValidationException("cron_invalid");
            if (string.IsNullOrWhiteSpace(timeZoneId))
                throw new CronScheduleValidationException("time_zone_invalid");

            CronExpression parsed;
            try
            {
                parsed = CronExpression.Parse(expression, CronFormat.Standard);
            }
            catch (CronFormatException exception)
            {
                throw new CronScheduleValidationException("cron_invalid", exception);
            }

            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception exception) when (
                exception is TimeZoneNotFoundException ||
                exception is InvalidTimeZoneException)
            {
                throw new CronScheduleValidationException("time_zone_invalid", exception);
            }

            return new CronSchedule(expression, zone, parsed);
        }

        public DateTimeOffset? GetNextOccurrence(DateTimeOffset fromUtc, bool inclusive = false)
        {
            if (fromUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("The starting time must use UTC.", nameof(fromUtc));

            var next = cron.GetNextOccurrence(fromUtc.UtcDateTime, timeZone, inclusive);
            return next.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(next.Value, DateTimeKind.Utc))
                : null;
        }

        public DateTimeOffset? GetPreviousOccurrence(
            DateTimeOffset fromUtc,
            bool inclusive = false)
        {
            if (fromUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("The starting time must use UTC.", nameof(fromUtc));

            var previous = cron.GetPreviousOccurrence(fromUtc.UtcDateTime, timeZone, inclusive);
            return previous.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(previous.Value, DateTimeKind.Utc))
                : null;
        }
    }

    public sealed class CronScheduleValidationException : Exception
    {
        public CronScheduleValidationException(string code, Exception? innerException = null)
            : base("The cron schedule is invalid.", innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
