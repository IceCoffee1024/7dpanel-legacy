using System;
using LSTY.SevenDPanel.Domain.Backups;

namespace LSTY.SevenDPanel.Application.Jobs
{
    public sealed record WorldBackupPayload(string WorldName);
    public sealed record PanelDatabaseBackupPayload;
    public sealed record ServerConfigurationBackupPayload;
    public sealed record RestorePayload(Guid BackupId, BackupKind BackupKind, bool RestartAfterStage);
    public sealed record ScheduledConsoleCommandPayload(Guid ScheduleId, string CommandText);
    public sealed record ScheduledRestartPayload(Guid ScheduleId, int CountdownSeconds);
    public sealed record ScheduledAnnouncementPayload(Guid ScheduleId, string MessageText);
}
