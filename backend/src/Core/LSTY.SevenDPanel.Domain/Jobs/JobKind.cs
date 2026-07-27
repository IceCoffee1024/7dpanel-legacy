namespace LSTY.SevenDPanel.Domain.Jobs
{
    public enum JobKind
    {
        WorldBackup,
        PanelDatabaseBackup,
        ServerConfigurationBackup,
        Restore,
        ScheduledConsoleCommand,
        ScheduledRestart,
        ScheduledAnnouncement,
        WorldOperation
    }
}
