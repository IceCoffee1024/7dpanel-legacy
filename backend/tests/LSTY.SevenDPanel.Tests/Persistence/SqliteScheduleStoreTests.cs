using System;
using System.IO;
using System.Linq;
using Dapper;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Jobs;
using LSTY.SevenDPanel.Adapters.Persistence.Sqlite.Schedules;
using LSTY.SevenDPanel.Application.Jobs;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Jobs;
using LSTY.SevenDPanel.Domain.Schedules;
using Microsoft.Data.Sqlite;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Persistence
{
    public sealed class SqliteScheduleStoreTests
    {
        [Fact]
        public void List_and_get_return_typed_schedules_in_stable_name_order()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var second = store.Upsert(new ScheduleDefinition(
                Guid.NewGuid(), "zulu", "* * * * *", "UTC", true,
                ScheduleConcurrencyPolicy.QueueOne, new ScheduledRestartAction(60), 0));
            var first = store.Upsert(new ScheduleDefinition(
                Guid.NewGuid(), "alpha", "* * * * *", "UTC", false,
                ScheduleConcurrencyPolicy.SkipIfRunning,
                new ScheduledConsoleCommandAction("say hello"), 0));

            Assert.Equal(new[] { first, second }, store.List());
            Assert.Equal(first, store.Get(first.Id));
            Assert.Null(store.Get(Guid.NewGuid()));
        }

        [Fact]
        public void Delete_requires_the_expected_row_version()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));

            Assert.False(store.Delete(schedule.Id, schedule.RowVersion + 1));
            Assert.NotNull(store.Get(schedule.Id));
            Assert.True(store.Delete(schedule.Id, schedule.RowVersion));
            Assert.Null(store.Get(schedule.Id));
        }

        [Fact]
        public void Delete_preserves_terminal_scheduled_job_payload_and_run_history()
        {
            using var database = new TemporaryDatabase();
            var schedules = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var jobs = new SqliteJobStore(database.ConnectionFactory);
            var payloads = new SqliteJobPayloadStore(database.ConnectionFactory);
            var schedule = schedules.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));

            Assert.Single(schedules.ClaimDue(Utc(1), "scheduler"));
            var running = jobs.TryClaimNext("worker", Utc(2))!;
            Assert.True(jobs.TryTransition(
                running.Id,
                running.RowVersion,
                JobStatus.Running,
                JobStatus.Succeeded,
                new JobCompletion(Utc(3), new JobProgress(1, 1), null)));
            schedules.RecordOutcome(new ScheduleRunOutcome(
                Guid.NewGuid(), schedule.Id, Utc(1), running.Id, "Succeeded", Utc(3)));

            var current = schedules.Get(schedule.Id)!;
            Assert.True(schedules.Delete(current.Id, current.RowVersion));

            Assert.Null(schedules.Get(schedule.Id));
            Assert.DoesNotContain(schedules.List(), item => item.Id == schedule.Id);
            var completed = jobs.Get(running.Id);
            Assert.Equal(JobStatus.Succeeded, completed.Status);
            Assert.Equal(schedule.Id, completed.SourceScheduleId);
            Assert.Equal(
                new ScheduledAnnouncementPayload(schedule.Id, "hello survivors"),
                payloads.GetScheduledAnnouncement(running.Id));
            using var connection = database.ConnectionFactory.Open();
            var run = connection.QuerySingle<ScheduleRunRow>(
                @"SELECT schedule_id AS ScheduleId, job_id AS JobId, outcome AS Outcome
                  FROM schedule_runs WHERE schedule_id = @ScheduleId;",
                new { ScheduleId = schedule.Id.ToString("D") });
            Assert.Equal(schedule.Id.ToString("D"), run.ScheduleId);
            Assert.Equal(running.Id.ToString("D"), run.JobId);
            Assert.Equal("Succeeded", run.Outcome);
        }

        [Fact]
        public void Repeated_due_wakeup_is_idempotent_for_schedule_run_and_job()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));

            Assert.Single(store.ClaimDue(Utc(1), "scheduler-a"));
            Assert.Empty(store.ClaimDue(Utc(1), "scheduler-b"));

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM schedule_runs WHERE schedule_id = @Id;", new { Id = schedule.Id.ToString("D") }));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs WHERE source_schedule_id = @Id;", new { Id = schedule.Id.ToString("D") }));
        }

        [Fact]
        public void Historical_due_occurrences_compensate_only_the_latest_and_advance_past_now()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            var now = Utc(10).AddSeconds(30);

            var claimed = Assert.Single(store.ClaimDue(now, "scheduler"));

            Assert.Equal(Utc(10), claimed.LastOccurrenceUtc);
            Assert.Equal(Utc(11), claimed.NextOccurrenceUtc);
            Assert.Empty(store.ClaimDue(now, "scheduler"));
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(
                Utc(10).ToUnixTimeMilliseconds(),
                connection.ExecuteScalar<long>(
                    "SELECT scheduled_for_utc FROM schedule_runs WHERE schedule_id = @Id;",
                    new { Id = schedule.Id.ToString("D") }));
        }

        [Fact]
        public void SkipIfRunning_records_skip_without_enqueuing_a_successor()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.SkipIfRunning));
            EnqueueRunning(database.ConnectionFactory, schedule.Id, "running-skip");

            store.ClaimDue(Utc(1), "scheduler");

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs WHERE source_schedule_id = @Id;", new { Id = schedule.Id.ToString("D") }));
            Assert.Equal("SkippedRunning", connection.ExecuteScalar<string>("SELECT outcome FROM schedule_runs WHERE schedule_id = @Id;", new { Id = schedule.Id.ToString("D") }));
        }

        [Fact]
        public void QueueOne_keeps_at_most_one_unstarted_successor()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            EnqueueRunning(database.ConnectionFactory, schedule.Id, "running-queue-one");

            store.ClaimDue(Utc(1), "scheduler");
            store.ClaimDue(Utc(2), "scheduler");

            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs WHERE source_schedule_id = @Id;", new { Id = schedule.Id.ToString("D") }));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM jobs WHERE source_schedule_id = @Id AND status = 'Queued';", new { Id = schedule.Id.ToString("D") }));
            Assert.Equal(
                new[] { "Queued", "SkippedQueueOnePending" },
                connection.Query<string>("SELECT outcome FROM schedule_runs WHERE schedule_id = @Id ORDER BY scheduled_for_utc;", new { Id = schedule.Id.ToString("D") }));
        }

        [Fact]
        public void Upsert_uses_row_version_and_RecordOutcome_is_idempotent()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            var updated = store.Upsert(new ScheduleDefinition(
                schedule.Id, "updated", schedule.CronExpression, schedule.TimeZoneId,
                schedule.Enabled, schedule.ConcurrencyPolicy, schedule.Action, schedule.RowVersion));
            Assert.Equal(1, updated.RowVersion);
            Assert.Throws<InvalidOperationException>(() => store.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne, schedule.Id, rowVersion: 0)));

            var outcome = new ScheduleRunOutcome(Guid.NewGuid(), schedule.Id, Utc(1), null, "SkippedRunning", Utc(1));
            store.RecordOutcome(outcome);
            store.RecordOutcome(outcome);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM schedule_runs WHERE schedule_id = @Id AND scheduled_for_utc = @At;",
                new { Id = schedule.Id.ToString("D"), At = Utc(1).ToUnixTimeMilliseconds() }));
        }

        [Fact]
        public void Schedule_actions_round_trip_and_due_jobs_receive_typed_payloads()
        {
            using var database = new TemporaryDatabase();
            var store = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var definitions = new[]
            {
                Definition(
                    ScheduleConcurrencyPolicy.QueueOne,
                    action: new ScheduledConsoleCommandAction("say hello")),
                Definition(
                    ScheduleConcurrencyPolicy.QueueOne,
                    action: new ScheduledRestartAction(60)),
                Definition(
                    ScheduleConcurrencyPolicy.QueueOne,
                    action: new ScheduledAnnouncementAction("hello survivors"))
            };

            foreach (var definition in definitions)
            {
                var stored = store.Upsert(definition);
                Assert.Equal(definition.Action, stored.Action);
            }

            Assert.Equal(3, store.ClaimDue(Utc(1), "scheduler").Count);
            using var connection = database.ConnectionFactory.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_console_command_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_restart_job_payloads;"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM scheduled_announcement_job_payloads;"));
            Assert.Equal("say hello", connection.ExecuteScalar<string>("SELECT command_text FROM scheduled_console_command_job_payloads;"));
            Assert.Equal(60, connection.ExecuteScalar<int>("SELECT countdown_seconds FROM scheduled_restart_job_payloads;"));
            Assert.Equal("hello survivors", connection.ExecuteScalar<string>("SELECT message_text FROM scheduled_announcement_job_payloads;"));
        }

        [Fact]
        public void Schedule_schema_rejects_a_kind_without_its_required_action_value()
        {
            using var database = new TemporaryDatabase();
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO schedules (
                      id, kind, name, cron_expression, time_zone_id, enabled,
                      concurrency_policy, next_occurrence_utc, row_version)
                  VALUES (@Id, 'ScheduledAnnouncement', 'invalid', '* * * * *', 'UTC',
                      1, 'QueueOne', @NextUtc, 0);",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    NextUtc = Utc(1).ToUnixTimeMilliseconds()
                }));
        }

        [Fact]
        public void Scheduled_job_requires_a_live_schedule_with_the_same_kind()
        {
            using var database = new TemporaryDatabase();
            var schedules = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var schedule = schedules.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO jobs (
                      id, kind, status, source_schedule_id, idempotency_key,
                      created_at_utc, row_version)
                  VALUES (@JobId, 'ScheduledRestart', 'Queued', @ScheduleId,
                      @IdempotencyKey, @CreatedAtUtc, 0);",
                new
                {
                    JobId = Guid.NewGuid().ToString("D"),
                    ScheduleId = schedule.Id.ToString("D"),
                    IdempotencyKey = "mismatched-schedule-kind",
                    CreatedAtUtc = Utc(0).ToUnixTimeMilliseconds()
                }));
        }

        [Fact]
        public void Schedule_run_job_must_belong_to_the_same_live_schedule()
        {
            using var database = new TemporaryDatabase();
            var schedules = new SqliteScheduleStore(database.ConnectionFactory, () => Utc(0));
            var first = schedules.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            var second = schedules.Upsert(Definition(ScheduleConcurrencyPolicy.QueueOne));
            var job = new SqliteJobStore(database.ConnectionFactory).Enqueue(new NewJob(
                JobKind.ScheduledAnnouncement,
                "owner",
                first.Id,
                "first-schedule-job",
                "corr-first-schedule-job",
                Utc(0)));
            using var connection = database.ConnectionFactory.Open();

            Assert.Throws<SqliteException>(() => connection.Execute(
                @"INSERT INTO schedule_runs (
                      id, schedule_id, scheduled_for_utc, job_id, outcome, created_at_utc)
                  VALUES (@Id, @ScheduleId, @ScheduledForUtc, @JobId, 'Queued', @CreatedAtUtc);",
                new
                {
                    Id = Guid.NewGuid().ToString("D"),
                    ScheduleId = second.Id.ToString("D"),
                    ScheduledForUtc = Utc(1).ToUnixTimeMilliseconds(),
                    JobId = job.Id.ToString("D"),
                    CreatedAtUtc = Utc(1).ToUnixTimeMilliseconds()
                }));
        }

        private static ScheduleDefinition Definition(
            ScheduleConcurrencyPolicy policy,
            Guid? id = null,
            long rowVersion = 0,
            ScheduleAction? action = null) =>
            new ScheduleDefinition(
                id ?? Guid.NewGuid(), "schedule", "* * * * *", "UTC",
                true, policy, action ?? new ScheduledAnnouncementAction("hello survivors"), rowVersion);

        private static void EnqueueRunning(SqliteConnectionFactory factory, Guid scheduleId, string key)
        {
            var jobs = new SqliteJobStore(factory);
            jobs.Enqueue(new NewJob(
                JobKind.ScheduledAnnouncement, "owner", scheduleId, key, "corr-" + key, Utc(0)));
            Assert.NotNull(jobs.TryClaimNext("worker", Utc(0)));
        }

        private static DateTimeOffset Utc(int minute) =>
            new DateTimeOffset(2026, 7, 26, 0, minute, 0, TimeSpan.Zero);

        private sealed class ScheduleRunRow
        {
            public string ScheduleId { get; set; } = string.Empty;
            public string JobId { get; set; } = string.Empty;
            public string Outcome { get; set; } = string.Empty;
        }

        private sealed class TemporaryDatabase : IDisposable
        {
            private readonly string directory = Path.Combine(
                Path.GetTempPath(), "7dpanel-schedule-store-tests", Guid.NewGuid().ToString("N"));

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
