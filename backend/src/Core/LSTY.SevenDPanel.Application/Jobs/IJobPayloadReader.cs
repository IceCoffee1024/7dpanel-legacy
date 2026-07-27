using System;

namespace LSTY.SevenDPanel.Application.Jobs
{
    public interface IJobPayloadReader
    {
        WorldBackupPayload GetWorldBackup(Guid jobId);
        PanelDatabaseBackupPayload GetPanelDatabaseBackup(Guid jobId);
        ServerConfigurationBackupPayload GetServerConfigurationBackup(Guid jobId);
        RestorePayload GetRestore(Guid jobId);
        ScheduledConsoleCommandPayload GetScheduledConsoleCommand(Guid jobId);
        ScheduledRestartPayload GetScheduledRestart(Guid jobId);
        ScheduledAnnouncementPayload GetScheduledAnnouncement(Guid jobId);
    }
}
