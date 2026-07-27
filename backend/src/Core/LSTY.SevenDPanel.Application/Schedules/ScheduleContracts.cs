using System;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Application.Schedules
{
    public sealed record CreateScheduleRequest(
        string Name,
        string CronExpression,
        string TimeZoneId,
        bool Enabled,
        ScheduleConcurrencyPolicy ConcurrencyPolicy,
        ScheduleAction Action);

    public sealed record UpdateScheduleRequest(
        string Name,
        string CronExpression,
        string TimeZoneId,
        bool Enabled,
        ScheduleConcurrencyPolicy ConcurrencyPolicy,
        ScheduleAction Action,
        long RowVersion);

    public class ScheduleServiceException : Exception
    {
        protected ScheduleServiceException(
            string code,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }

    public sealed class ScheduleValidationException : ScheduleServiceException
    {
        public ScheduleValidationException(
            string code,
            Exception? innerException = null)
            : base(code, "The schedule is invalid.", innerException)
        {
        }
    }

    public sealed class ScheduleNotFoundException : ScheduleServiceException
    {
        public ScheduleNotFoundException()
            : base("schedule_not_found", "The schedule does not exist.")
        {
        }
    }

    public sealed class ScheduleConflictException : ScheduleServiceException
    {
        public ScheduleConflictException(Exception? innerException = null)
            : base(
                "schedule_conflict",
                "The schedule changed after it was read.",
                innerException)
        {
        }
    }
}
