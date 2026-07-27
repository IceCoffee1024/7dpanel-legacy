using System;
using System.Collections.Generic;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Schedules;

namespace LSTY.SevenDPanel.Adapters.Local.Backups
{
    public sealed class BackupPolicyScheduler
    {
        private const string SystemActor = "system:backup-policy";

        private readonly IBackupPolicyStore policies;
        private readonly CreateWorldBackup createWorld;
        private readonly CreatePanelDatabaseBackup createPanelDatabase;
        private readonly CreateServerConfigurationBackup createServerConfiguration;
        private readonly ApprovedStorageRoots roots;
        private readonly Action<string> log;
        private readonly object sync = new object();
        private readonly Dictionary<BackupKind, long> lastQueuedOccurrence =
            new Dictionary<BackupKind, long>();

        public BackupPolicyScheduler(
            IBackupPolicyStore policies,
            CreateWorldBackup createWorld,
            CreatePanelDatabaseBackup createPanelDatabase,
            CreateServerConfigurationBackup createServerConfiguration,
            ApprovedStorageRoots roots,
            Action<string>? log = null)
        {
            this.policies = policies ?? throw new ArgumentNullException(nameof(policies));
            this.createWorld = createWorld ?? throw new ArgumentNullException(nameof(createWorld));
            this.createPanelDatabase = createPanelDatabase ??
                throw new ArgumentNullException(nameof(createPanelDatabase));
            this.createServerConfiguration = createServerConfiguration ??
                throw new ArgumentNullException(nameof(createServerConfiguration));
            this.roots = roots ?? throw new ArgumentNullException(nameof(roots));
            this.log = log ?? (_ => { });
        }

        public int Tick(DateTimeOffset nowUtc)
        {
            if (nowUtc.Offset != TimeSpan.Zero)
                throw new ArgumentException("A UTC timestamp is required.", nameof(nowUtc));

            lock (sync)
            {
                var enqueued = 0;
                foreach (var policy in policies.List())
                {
                    if (!policy.Enabled ||
                        !string.Equals(
                            policy.BackupRootId,
                            roots.BackupRootId,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var due = CronSchedule.Create(policy.CronExpression, policy.TimeZoneId)
                        .GetPreviousOccurrence(nowUtc, inclusive: true);
                    if (!due.HasValue) continue;
                    var occurrence = due.Value.ToUnixTimeMilliseconds();
                    if (lastQueuedOccurrence.TryGetValue(policy.Kind, out var previous) &&
                        previous >= occurrence)
                    {
                        continue;
                    }

                    var key = "backup-policy:" + policy.Kind + ":" + occurrence;
                    try
                    {
                        Enqueue(policy.Kind, due.Value, key);
                        lastQueuedOccurrence[policy.Kind] = occurrence;
                        enqueued++;
                    }
                    catch (Exception exception)
                    {
                        log("backup_policy_enqueue_failed kind=" + policy.Kind +
                            " error=" + exception.GetType().Name);
                    }
                }
                return enqueued;
            }
        }

        private void Enqueue(BackupKind kind, DateTimeOffset scheduledAtUtc, string key)
        {
            switch (kind)
            {
                case BackupKind.World:
                    createWorld.Execute(SystemActor, roots.CurrentWorldName, key, key);
                    break;
                case BackupKind.PanelDatabase:
                    createPanelDatabase.Execute(new CreateBackupRequest(
                        SystemActor,
                        key,
                        key,
                        scheduledAtUtc));
                    break;
                case BackupKind.ServerConfiguration:
                    createServerConfiguration.Execute(new CreateBackupRequest(
                        SystemActor,
                        key,
                        key,
                        scheduledAtUtc));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
