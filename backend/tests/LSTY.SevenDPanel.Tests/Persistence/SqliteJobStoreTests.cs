using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Backups;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Application.Backups;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Domain.Backups;
using LSTY.SevenDPanel.Domain.Jobs;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Persistence")]
    public sealed class SqliteJobStoreTests
    {
        [Fact]
        public void Typed_payload_store_writes_each_fixed_kind_in_the_same_transaction()
        {
            using var database = new TemporaryDatabase();
            var payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            var schedules = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            InsertSchedule(database.ConnectionFactory, schedules[0], JobKind.ScheduledConsoleCommand);
            InsertSchedule(database.ConnectionFactory, schedules[1], JobKind.ScheduledRestart);
            InsertSchedule(database.ConnectionFactory, schedules[2], JobKind.ScheduledAnnouncement);

            var world = payloads.Enqueue(New(JobKind.WorldBackup, "world"), new WorldBackupPayload("Navezgane"));
            var panel = payloads.Enqueue(New(JobKind.PanelDatabaseBackup, "panel"), new PanelDatabaseBackupPayload());
            var configuration = payloads.Enqueue(New(JobKind.ServerConfigurationBackup, "config"), new ServerConfigurationBackupPayload());
            var backupId = Guid.NewGuid();
            new SqliteBackupCatalog(database.ConnectionFactory).Add(new CompletedBackup(
                backupId, BackupKind.PanelDatabase, "primary", "restore-source.zip", 42,
                new string('b', 64), null, null, "Verified", Utc(0), panel.Id, 1));
            var jobs = new[]
            {
                world,
                panel,
                configuration,
                payloads.Enqueue(New(JobKind.Restore, "restore"), new RestorePayload(backupId, BackupKind.PanelDatabase, true)),
                payloads.Enqueue(New(JobKind.ScheduledConsoleCommand, "command", schedules[0]), new ScheduledConsoleCommandPayload(schedules[0], "say hello")),
                payloads.Enqueue(New(JobKind.ScheduledRestart, "restart", schedules[1]), new ScheduledRestartPayload(schedules[1], 60)),
                payloads.Enqueue(New(JobKind.ScheduledAnnouncement, "announcement", schedules[2]), new ScheduledAnnouncementPayload(schedules[2], "hello survivors"))
            };

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(7, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM world_backup_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM panel_database_backup_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM server_configuration_backup_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM restore_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_console_command_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_restart_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_announcement_job_payloads;"));

            IJobPayloadReader reader = payloads;
            Assert.Equal(new WorldBackupPayload("Navezgane"), reader.GetWorldBackup(world.Id));
            Assert.Equal(new PanelDatabaseBackupPayload(), reader.GetPanelDatabaseBackup(panel.Id));
            Assert.Equal(new ServerConfigurationBackupPayload(), reader.GetServerConfigurationBackup(configuration.Id));
            Assert.Equal(new RestorePayload(backupId, BackupKind.PanelDatabase, true), reader.GetRestore(jobs[3].Id));
            Assert.Equal(new ScheduledConsoleCommandPayload(schedules[0], "say hello"), reader.GetScheduledConsoleCommand(jobs[4].Id));
            Assert.Equal(new ScheduledRestartPayload(schedules[1], 60), reader.GetScheduledRestart(jobs[5].Id));
            Assert.Equal(new ScheduledAnnouncementPayload(schedules[2], "hello survivors"), reader.GetScheduledAnnouncement(jobs[6].Id));

            Assert.Throws<SqliteException>(() => connection.Execute(
                "INSERT INTO restore_job_payloads (job_id, backup_id, backup_kind, restart_after_stage) VALUES (@JobId, @BackupId, 'World', 1);",
                new { JobId = jobs[0].Id.ToString("D"), BackupId = backupId.ToString("D") }));
        }

        [Fact]
        public void Reusing_an_idempotency_key_with_a_different_payload_is_rejected()
        {
            using var database = new TemporaryDatabase();
            IJobSubmissionStore payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            payloads.Enqueue(New(JobKind.WorldBackup, "same-key"), new WorldBackupPayload("Navezgane"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                payloads.Enqueue(New(JobKind.WorldBackup, "same-key"), new WorldBackupPayload("RWG")));

            Assert.Equal("job_idempotency_conflict", error.Message);
        }

        [Fact]
        public async Task Begin_immediate_allows_only_one_connection_to_claim_one_queued_job()
        {
            using var database = new TemporaryDatabase();
            var payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            var queued = payloads.Enqueue(New(JobKind.PanelDatabaseBackup, "claim"), new PanelDatabaseBackupPayload());
            var first = new SqliteJobStore(database.ConnectionFactory);
            var second = new SqliteJobStore(database.ConnectionFactory);
            using var gate = new ManualResetEventSlim(false);

            var claims = new[]
            {
                Task.Run(() => { gate.Wait(); return first.TryClaimNext("worker-a", Utc(1)); }),
                Task.Run(() => { gate.Wait(); return second.TryClaimNext("worker-b", Utc(1)); })
            };
            gate.Set();
            await Task.WhenAll(claims);

            var claimed = claims.Select(task => task.Result).Where(record => record != null).ToArray();
            Assert.Single(claimed);
            Assert.Equal(queued.Id, claimed[0]!.Id);
            Assert.Equal(JobStatus.Running, claimed[0]!.Status);
            Assert.Equal(1, claimed[0]!.RowVersion);
        }

        [Fact]
        public void Transition_rejects_stale_row_version_and_job_pages_carry_JobCursor()
        {
            using var database = new TemporaryDatabase();
            var payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            var store = new SqliteJobStore(database.ConnectionFactory);
            var first = payloads.Enqueue(New(JobKind.PanelDatabaseBackup, "first", createdAt: Utc(0)), new PanelDatabaseBackupPayload());
            payloads.Enqueue(New(JobKind.ServerConfigurationBackup, "second", createdAt: Utc(1)), new ServerConfigurationBackupPayload());
            var running = store.TryClaimNext("worker", Utc(2))!;

            Assert.False(store.TryTransition(
                running.Id, 0, JobStatus.Running, JobStatus.Succeeded,
                new JobCompletion(Utc(3), new JobProgress(1, 1), null)));
            Assert.True(store.TryTransition(
                running.Id, running.RowVersion, JobStatus.Running, JobStatus.Succeeded,
                new JobCompletion(Utc(3), new JobProgress(1, 1), null)));
            Assert.Equal(2, store.Get(first.Id).RowVersion);

            var page = store.List(new JobQuery(1, null, null, null, null, null));
            var cursor = Assert.IsType<JobCursor>(page.NextCursor);
            var next = store.List(new JobQuery(1, null, null, null, null, cursor));
            Assert.Single(next.Items);
            Assert.NotEqual(page.Items[0].Id, next.Items[0].Id);
        }

        [Fact]
        public void Backup_catalog_pages_carry_BackupCursor_and_never_accept_path_like_resource_ids()
        {
            using var database = new TemporaryDatabase();
            var payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            var catalog = new SqliteBackupCatalog(database.ConnectionFactory);
            var firstJob = payloads.Enqueue(New(JobKind.PanelDatabaseBackup, "backup-1", createdAt: Utc(0)), new PanelDatabaseBackupPayload());
            var secondJob = payloads.Enqueue(New(JobKind.PanelDatabaseBackup, "backup-2", createdAt: Utc(1)), new PanelDatabaseBackupPayload());
            catalog.Add(Backup(firstJob.Id, "resource-one.zip", Utc(0)));
            catalog.Add(Backup(secondJob.Id, "resource-two.zip", Utc(1)));

            var page = catalog.List(new BackupQuery(1, null, null));
            var cursor = Assert.IsType<BackupCursor>(page.NextCursor);
            Assert.Single(catalog.List(new BackupQuery(1, null, cursor)).Items);
            Assert.Throws<ArgumentException>(() => catalog.Add(Backup(Guid.NewGuid(), "../panel.db", Utc(2))));
        }

        private static NewJob New(JobKind kind, string key, Guid? scheduleId = null, DateTimeOffset? createdAt = null) =>
            new NewJob(kind, "owner", scheduleId, key, "corr-" + key, createdAt ?? Utc(0));

        private static CompletedBackup Backup(Guid jobId, string resourceId, DateTimeOffset at) =>
            new CompletedBackup(
                Guid.NewGuid(), BackupKind.PanelDatabase, "primary", resourceId, 42,
                new string('a', 64), null, null, "Verified", at, jobId, 1);

        private static void InsertSchedule(SqliteConnectionFactory factory, Guid id, JobKind kind)
        {
            using var connection = factory.Open();
            connection.Execute(
                @"INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, command_text, countdown_seconds, message_text,
                      next_occurrence_utc, row_version)
                  VALUES (@Id, @Kind, @Name, '* * * * *', 'UTC', 1, 'QueueOne',
                      @CommandText, @CountdownSeconds, @MessageText, @NextUtc, 0);",
                new
                {
                    Id = id.ToString("D"),
                    Kind = kind.ToString(),
                    Name = kind.ToString(),
                    CommandText = kind == JobKind.ScheduledConsoleCommand ? "say test" : null,
                    CountdownSeconds = kind == JobKind.ScheduledRestart ? (int?)60 : null,
                    MessageText = kind == JobKind.ScheduledAnnouncement ? "test announcement" : null,
                    NextUtc = Utc(1).ToUnixTimeMilliseconds()
                });
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Persistence")]

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-job-store-tests", Guid.NewGuid().ToString("N"));

            public TemporaryDatabase()
            {
                ConnectionFactory = new SqliteConnectionFactory(Path.Combine(directory, "panel.db"));
                new SqliteDatabaseBootstrapper(ConnectionFactory).Upgrade();
            }

            public SqliteConnectionFactory ConnectionFactory { get; }

            public void Dispose()
            {
                ConnectionFactory.Dispose();
                SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
