using System;
using System.IO;
using Dapper;
using LSTY.SevenDPanel.Adapters.Local.Backups;
using LSTY.SevenDPanel.Adapters.Local.Files;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Adapters.Local.Schedules;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Backups;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Local
{
    public sealed class BackupPolicySchedulerTests
    {
        [Fact]
        public void Tick_enqueues_only_the_latest_due_run_for_each_enabled_policy()
        {
            using var fixture = new Fixture();
            fixture.Policies.Upsert(Policy(BackupKind.World, enabled: true));
            fixture.Policies.Upsert(Policy(BackupKind.PanelDatabase, enabled: true));
            fixture.Policies.Upsert(Policy(BackupKind.ServerConfiguration, enabled: false));
            var now = Utc(17);
            var scheduler = fixture.CreateScheduler(now);

            Assert.Equal(2, scheduler.Tick(now));
            Assert.Equal(0, scheduler.Tick(now));

            using var connection = fixture.Database.ConnectionFactory.Open();
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM world_backup_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM panel_database_backup_job_payloads;"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM server_configuration_backup_job_payloads;"));
            Assert.Equal(
                2,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM jobs WHERE idempotency_key LIKE @Key;",
                    new { Key = "backup-policy:%:" + Utc(15).ToUnixTimeMilliseconds() }));
        }

        [Fact]
        public void Tick_rejects_a_persisted_policy_for_an_unapproved_backup_root()
        {
            using var fixture = new Fixture();
            fixture.Policies.Upsert(new BackupPolicyDefinition(
                BackupKind.World,
                true,
                "*/5 * * * *",
                "UTC",
                "secondary",
                3,
                7,
                true,
                0));
            var now = Utc(17);

            Assert.Equal(0, fixture.CreateScheduler(now).Tick(now));

            using var connection = fixture.Database.ConnectionFactory.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs;"));
        }

        [Fact]
        public void Background_scheduler_drives_backup_policies_from_its_production_tick()
        {
            using var fixture = new Fixture();
            fixture.Policies.Upsert(Policy(BackupKind.PanelDatabase, enabled: true));
            var now = Utc(17);
            var backupPolicies = fixture.CreateScheduler(now);
            var scheduler = new BackgroundScheduler(
                new EmptyScheduleStore(),
                "scheduler",
                () => now,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(30),
                backupPolicies: backupPolicies);

            scheduler.Tick(default);

            using var connection = fixture.Database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM jobs WHERE kind = 'PanelDatabaseBackup';"));
        }

        private static BackupPolicyDefinition Policy(BackupKind kind, bool enabled) =>
            new BackupPolicyDefinition(
                kind,
                enabled,
                "*/5 * * * *",
                "UTC",
                "primary",
                3,
                7,
                true,
                0);

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private sealed class Fixture : IDisposable
        {
            private readonly string root = Path.Combine(
                Path.GetTempPath(),
                "7dpanel-backup-policy-scheduler-tests",
                Guid.NewGuid().ToString("N"));

            public Fixture()
            {
                Database = new TemporaryDatabase(Path.Combine(root, "panel.db"));
                Policies = new SqliteBackupPolicyStore(Database.ConnectionFactory);
                Submissions = new SqliteJobPayloadStore(Database.ConnectionFactory);
                Roots = new ApprovedStorageRoots(
                    "world",
                    Path.Combine(root, "world"),
                    root,
                    Path.Combine(root, "configuration"),
                    "primary",
                    Path.Combine(root, "backups"),
                    "3.0.1-b4");
            }

            public TemporaryDatabase Database { get; }
            public SqliteBackupPolicyStore Policies { get; }
            public SqliteJobPayloadStore Submissions { get; }
            public ApprovedStorageRoots Roots { get; }

            public BackupPolicyScheduler CreateScheduler(DateTimeOffset now) =>
                new BackupPolicyScheduler(
                    Policies,
                    new CreateWorldBackup(Submissions, () => now),
                    new CreatePanelDatabaseBackup(Submissions),
                    new CreateServerConfigurationBackup(Submissions),
                    Roots);

            public void Dispose()
            {
                Database.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            public TemporaryDatabase(string path)
            {
                ConnectionFactory = new SqliteConnectionFactory(path);
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
            }
        }

        private sealed class EmptyScheduleStore : IScheduleStore
        {
            public IReadOnlyList<ScheduleRecord> List() => Array.Empty<ScheduleRecord>();
            public ScheduleRecord? Get(Guid scheduleId) => null;
            public ScheduleRecord Upsert(ScheduleDefinition definition) =>
                throw new NotSupportedException();
            public bool Delete(Guid scheduleId, long expectedRowVersion) => false;
            public IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId) =>
                Array.Empty<ScheduleRecord>();
            public void RecordOutcome(ScheduleRunOutcome outcome)
            {
            }
        }
    }
}
