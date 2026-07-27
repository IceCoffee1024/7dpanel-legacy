namespace LSTY.SevenDPanel.Application.Jobs
{
    public interface IJobSubmissionStore
    {
        JobRecord Enqueue(NewJob job, WorldBackupPayload payload);
        JobRecord Enqueue(NewJob job, PanelDatabaseBackupPayload payload);
        JobRecord Enqueue(NewJob job, ServerConfigurationBackupPayload payload);
        JobRecord Enqueue(NewJob job, RestorePayload payload);
        JobRecord Enqueue(NewJob job, ScheduledConsoleCommandPayload payload);
        JobRecord Enqueue(NewJob job, ScheduledRestartPayload payload);
        JobRecord Enqueue(NewJob job, ScheduledAnnouncementPayload payload);
    }
}
