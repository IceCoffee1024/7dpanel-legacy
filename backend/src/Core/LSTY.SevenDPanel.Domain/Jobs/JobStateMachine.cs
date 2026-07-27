using System;

namespace LSTY.SevenDPanel.Domain.Jobs
{
    public static class JobStateMachine
    {
        public static bool CanTransition(JobKind kind, JobStatus current, JobStatus next)
        {
            RequireDefined(kind, nameof(kind));
            RequireDefined(current, nameof(current));
            RequireDefined(next, nameof(next));

            if (current == JobStatus.Queued)
            {
                if (next == JobStatus.Cancelled)
                    return true;
                return kind == JobKind.Restore
                    ? next == JobStatus.PendingRestart
                    : next == JobStatus.Running;
            }

            if (kind == JobKind.Restore && current == JobStatus.PendingRestart)
                return next == JobStatus.Running || next == JobStatus.Cancelled;

            return current == JobStatus.Running &&
                (next == JobStatus.Succeeded ||
                 next == JobStatus.Failed ||
                 next == JobStatus.Interrupted ||
                 next == JobStatus.ResultUnknown);
        }

        private static void RequireDefined<T>(T value, string parameterName) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value))
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
