using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Application.Schedules
{
    public sealed class ScheduleService
    {
        private const string StoreConflictCode = "schedule_row_version_conflict";

        private readonly IScheduleStore store;
        private readonly Func<Guid> idFactory;

        public ScheduleService(IScheduleStore store)
            : this(store, Guid.NewGuid)
        {
        }

        internal ScheduleService(IScheduleStore store, Func<Guid> idFactory)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));
        }

        public IReadOnlyList<ScheduleRecord> List() => store.List();

        public ScheduleRecord Get(Guid scheduleId)
        {
            RequireId(scheduleId);
            return store.Get(scheduleId) ?? throw new ScheduleNotFoundException();
        }

        public ScheduleRecord Create(CreateScheduleRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var id = idFactory();
            if (id == Guid.Empty)
                throw new InvalidOperationException("The schedule id factory returned an empty id.");

            return Save(Definition(
                id,
                request.Name,
                request.CronExpression,
                request.TimeZoneId,
                request.Enabled,
                request.ConcurrencyPolicy,
                request.Action,
                0));
        }

        public ScheduleRecord Update(Guid scheduleId, UpdateScheduleRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Get(scheduleId);
            return Save(Definition(
                scheduleId,
                request.Name,
                request.CronExpression,
                request.TimeZoneId,
                request.Enabled,
                request.ConcurrencyPolicy,
                request.Action,
                request.RowVersion));
        }

        public ScheduleRecord Enable(Guid scheduleId, long rowVersion) =>
            SetEnabled(scheduleId, rowVersion, true);

        public ScheduleRecord Disable(Guid scheduleId, long rowVersion) =>
            SetEnabled(scheduleId, rowVersion, false);

        public void Delete(Guid scheduleId, long rowVersion)
        {
            var current = Get(scheduleId);
            RequireRowVersion(rowVersion);
            if (current.RowVersion != rowVersion)
                throw new ScheduleConflictException();

            try
            {
                if (!store.Delete(scheduleId, rowVersion))
                    throw new ScheduleConflictException();
            }
            catch (InvalidOperationException exception) when (IsStoreConflict(exception))
            {
                throw new ScheduleConflictException(exception);
            }
        }

        private ScheduleRecord SetEnabled(
            Guid scheduleId,
            long rowVersion,
            bool enabled)
        {
            var current = Get(scheduleId);
            RequireRowVersion(rowVersion);
            if (current.RowVersion != rowVersion)
                throw new ScheduleConflictException();

            return Save(new ScheduleDefinition(
                current.Id,
                current.Name,
                current.CronExpression,
                current.TimeZoneId,
                enabled,
                current.ConcurrencyPolicy,
                current.Action,
                rowVersion));
        }

        private ScheduleRecord Save(ScheduleDefinition definition)
        {
            try
            {
                return store.Upsert(definition);
            }
            catch (InvalidOperationException exception) when (IsStoreConflict(exception))
            {
                throw new ScheduleConflictException(exception);
            }
        }

        private static ScheduleDefinition Definition(
            Guid id,
            string name,
            string cronExpression,
            string timeZoneId,
            bool enabled,
            ScheduleConcurrencyPolicy concurrencyPolicy,
            ScheduleAction action,
            long rowVersion)
        {
            var normalizedName = RequireText(name);
            RequireRowVersion(rowVersion);
            if (!Enum.IsDefined(typeof(ScheduleConcurrencyPolicy), concurrencyPolicy))
                throw new ScheduleValidationException("schedule_invalid");

            CronSchedule cron;
            try
            {
                cron = CronSchedule.Create(cronExpression, timeZoneId);
            }
            catch (CronScheduleValidationException exception)
            {
                throw new ScheduleValidationException(exception.Code, exception);
            }

            return new ScheduleDefinition(
                id,
                normalizedName,
                cron.Expression,
                cron.TimeZoneId,
                enabled,
                concurrencyPolicy,
                NormalizeAction(action),
                rowVersion);
        }

        private static ScheduleAction NormalizeAction(ScheduleAction action)
        {
            switch (action)
            {
                case ScheduledConsoleCommandAction command:
                    if (string.IsNullOrWhiteSpace(command.CommandText) ||
                        command.CommandText.IndexOf('\r') >= 0 ||
                        command.CommandText.IndexOf('\n') >= 0)
                    {
                        throw new ScheduleValidationException("schedule_invalid");
                    }
                    return new ScheduledConsoleCommandAction(command.CommandText.Trim());
                case ScheduledRestartAction restart
                    when restart.CountdownSeconds < 0 || restart.CountdownSeconds > 86400:
                    throw new ScheduleValidationException("schedule_invalid");
                case ScheduledRestartAction:
                    return action;
                case ScheduledAnnouncementAction announcement
                    when announcement.MessageText == null ||
                         announcement.MessageText.Length < 1 ||
                         announcement.MessageText.Length > 500:
                    throw new ScheduleValidationException("announcement_invalid");
                case ScheduledAnnouncementAction:
                    return action;
                default:
                    throw new ScheduleValidationException("schedule_invalid");
            }
        }

        private static string RequireText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ScheduleValidationException("schedule_invalid");
            return value.Trim();
        }

        private static void RequireId(Guid scheduleId)
        {
            if (scheduleId == Guid.Empty)
                throw new ScheduleValidationException("schedule_invalid");
        }

        private static void RequireRowVersion(long rowVersion)
        {
            if (rowVersion < 0)
                throw new ScheduleValidationException("schedule_invalid");
        }

        private static bool IsStoreConflict(InvalidOperationException exception) =>
            string.Equals(exception.Message, StoreConflictCode, StringComparison.Ordinal);
    }
}
