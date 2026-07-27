using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Application.Backups
{
    public sealed record BackupPolicyDefinition
    {
        public BackupPolicyDefinition(
            BackupKind kind,
            bool enabled,
            string cronExpression,
            string timeZoneId,
            string backupRootId,
            int retentionCount,
            int retentionDays,
            bool compressionEnabled,
            long rowVersion)
        {
            if (!Enum.IsDefined(typeof(BackupKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (retentionCount < 0) throw new ArgumentOutOfRangeException(nameof(retentionCount));
            if (retentionDays < 0) throw new ArgumentOutOfRangeException(nameof(retentionDays));
            if (rowVersion < 0) throw new ArgumentOutOfRangeException(nameof(rowVersion));

            CronSchedule schedule;
            try
            {
                schedule = CronSchedule.Create(cronExpression, timeZoneId);
            }
            catch (CronScheduleValidationException exception)
            {
                throw new ArgumentException(exception.Code, nameof(cronExpression), exception);
            }

            Kind = kind;
            Enabled = enabled;
            CronExpression = schedule.Expression;
            TimeZoneId = schedule.TimeZoneId;
            BackupRootId = RequireRootId(backupRootId);
            RetentionCount = retentionCount;
            RetentionDays = retentionDays;
            CompressionEnabled = compressionEnabled;
            RowVersion = rowVersion;
        }

        public BackupKind Kind { get; }
        public bool Enabled { get; }
        public string CronExpression { get; }
        public string TimeZoneId { get; }
        public string BackupRootId { get; }
        public int RetentionCount { get; }
        public int RetentionDays { get; }
        public bool CompressionEnabled { get; }
        public long RowVersion { get; }

        private static string RequireRootId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("backup_root_id_required", nameof(value));
            var normalized = value.Trim();
            if (normalized.IndexOf('/') >= 0 ||
                normalized.IndexOf('\\') >= 0 ||
                normalized.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("backup_root_id_invalid", nameof(value));
            }
            return normalized;
        }
    }

    public interface IBackupPolicyStore
    {
        IReadOnlyList<BackupPolicyDefinition> List();
        BackupPolicyDefinition? Get(BackupKind kind);
        BackupPolicyDefinition Upsert(BackupPolicyDefinition definition);
    }
}
