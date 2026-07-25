using System;

namespace LSTY.SevenDPanel.Application
{
    public sealed class RestartPolicySummary
    {
        public RestartPolicySummary(AvailabilityState availability, bool isConfigured, string? scheduleDescription, DateTimeOffset? nextRestartAtUtc)
        {
            Availability = availability;
            IsConfigured = isConfigured;
            ScheduleDescription = scheduleDescription;
            NextRestartAtUtc = nextRestartAtUtc;
        }

        public AvailabilityState Availability { get; }
        public bool IsConfigured { get; }
        public string? ScheduleDescription { get; }
        public DateTimeOffset? NextRestartAtUtc { get; }
        public static RestartPolicySummary Unavailable() => new RestartPolicySummary(AvailabilityState.Unavailable, false, null, null);
    }
}
