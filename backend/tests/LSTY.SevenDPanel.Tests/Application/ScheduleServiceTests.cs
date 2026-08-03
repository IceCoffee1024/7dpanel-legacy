using System;
using System.Collections.Generic;
using System.Linq;
using LSTY.SevenDPanel.Application.Schedules;
using LSTY.SevenDPanel.Domain.Schedules;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Application
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Application")]
    public sealed class ScheduleServiceTests
    {
        [Fact]
        public void Create_normalizes_name_and_console_command()
        {
            var id = Guid.NewGuid();
            var store = new RecordingScheduleStore();
            var service = new ScheduleService(store, () => id);

            var created = service.Create(new CreateScheduleRequest(
                "  daily command  ",
                "* * * * *",
                "UTC",
                true,
                ScheduleConcurrencyPolicy.QueueOne,
                new ScheduledConsoleCommandAction("  say hello  ")));

            Assert.Equal(id, created.Id);
            Assert.Equal("daily command", created.Name);
            Assert.Equal(
                new ScheduledConsoleCommandAction("say hello"),
                created.Action);
            Assert.Equal(0, created.RowVersion);
        }

        [Fact]
        public void Create_uses_domain_validation_for_cron_and_time_zone()
        {
            var service = Service();

            Assert.Equal(
                "cron_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(cronExpression: "not cron"))).Code);
            Assert.Equal(
                "time_zone_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(timeZoneId: "Missing/Zone"))).Code);
        }

        [Fact]
        public void Create_rejects_invalid_name_and_typed_action_values()
        {
            var service = Service();

            Assert.Equal(
                "schedule_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(name: "   "))).Code);
            Assert.Equal(
                "schedule_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledConsoleCommandAction("")))).Code);
            Assert.Equal(
                "schedule_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledConsoleCommandAction("say hello\nshutdown")))).Code);
            Assert.Equal(
                "schedule_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledRestartAction(-1)))).Code);
            Assert.Equal(
                "schedule_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledRestartAction(86401)))).Code);
            Assert.Equal(
                "announcement_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledAnnouncementAction("")))).Code);
            Assert.Equal(
                "announcement_invalid",
                Assert.Throws<ScheduleValidationException>(() =>
                    service.Create(Request(action: new ScheduledAnnouncementAction(new string('a', 501))))).Code);
        }

        [Fact]
        public void Update_requires_existing_schedule_and_uses_expected_row_version()
        {
            var id = Guid.NewGuid();
            var store = new RecordingScheduleStore();
            var service = new ScheduleService(store, Guid.NewGuid);

            Assert.Throws<ScheduleNotFoundException>(() =>
                service.Update(id, UpdateRequest(rowVersion: 3)));

            store.Seed(Record(id, rowVersion: 3));
            var updated = service.Update(
                id,
                UpdateRequest(
                    rowVersion: 3,
                    action: new ScheduledRestartAction(86400)));

            Assert.Equal(new ScheduledRestartAction(86400), updated.Action);
            Assert.Equal(4, updated.RowVersion);
            Assert.Equal(3, store.LastUpsert!.RowVersion);
        }

        [Fact]
        public void Row_version_store_failures_map_to_stable_schedule_conflict()
        {
            var id = Guid.NewGuid();
            var store = new RecordingScheduleStore();
            store.Seed(Record(id));
            store.ConflictOnUpsert = true;
            var service = new ScheduleService(store, Guid.NewGuid);

            var exception = Assert.Throws<ScheduleConflictException>(() =>
                service.Update(id, UpdateRequest(rowVersion: 0)));

            Assert.Equal("schedule_conflict", exception.Code);
        }

        [Fact]
        public void Enable_and_disable_preserve_the_typed_action_and_use_cas()
        {
            var id = Guid.NewGuid();
            var action = new ScheduledAnnouncementAction("hello survivors");
            var store = new RecordingScheduleStore();
            store.Seed(Record(id, enabled: false, rowVersion: 2, action: action));
            var service = new ScheduleService(store, Guid.NewGuid);

            var enabled = service.Enable(id, 2);
            var disabled = service.Disable(id, enabled.RowVersion);

            Assert.True(enabled.Enabled);
            Assert.False(disabled.Enabled);
            Assert.Equal(action, enabled.Action);
            Assert.Equal(action, disabled.Action);
            Assert.Equal(4, disabled.RowVersion);
        }

        [Fact]
        public void List_and_get_return_the_store_records()
        {
            var first = Record(Guid.NewGuid(), name: "first");
            var second = Record(Guid.NewGuid(), name: "second");
            var store = new RecordingScheduleStore();
            store.Seed(first);
            store.Seed(second);
            var service = new ScheduleService(store, Guid.NewGuid);

            Assert.Same(first, service.Get(first.Id));
            Assert.Equal(
                new[] { first.Id, second.Id },
                service.List().Select(schedule => schedule.Id));
            Assert.Throws<ScheduleNotFoundException>(() => service.Get(Guid.NewGuid()));
        }

        [Fact]
        public void Delete_maps_a_stale_row_version_to_schedule_conflict()
        {
            var id = Guid.NewGuid();
            var store = new RecordingScheduleStore();
            store.Seed(Record(id, rowVersion: 5));
            var service = new ScheduleService(store, Guid.NewGuid);

            var conflict = Assert.Throws<ScheduleConflictException>(() =>
                service.Delete(id, 4));
            Assert.Equal("schedule_conflict", conflict.Code);

            service.Delete(id, 5);
            Assert.Throws<ScheduleNotFoundException>(() => service.Get(id));
        }

        private static ScheduleService Service() =>
            new ScheduleService(new RecordingScheduleStore(), Guid.NewGuid);

        private static CreateScheduleRequest Request(
            string name = "schedule",
            string cronExpression = "* * * * *",
            string timeZoneId = "UTC",
            ScheduleAction? action = null) =>
            new CreateScheduleRequest(
                name,
                cronExpression,
                timeZoneId,
                true,
                ScheduleConcurrencyPolicy.QueueOne,
                action ?? new ScheduledAnnouncementAction("hello survivors"));

        private static UpdateScheduleRequest UpdateRequest(
            long rowVersion,
            ScheduleAction? action = null) =>
            new UpdateScheduleRequest(
                "updated",
                "* * * * *",
                "UTC",
                true,
                ScheduleConcurrencyPolicy.SkipIfRunning,
                action ?? new ScheduledConsoleCommandAction("say updated"),
                rowVersion);

        private static ScheduleRecord Record(
            Guid id,
            string name = "schedule",
            bool enabled = true,
            long rowVersion = 0,
            ScheduleAction? action = null) =>
            new ScheduleRecord(
                id,
                name,
                "* * * * *",
                "UTC",
                enabled,
                ScheduleConcurrencyPolicy.QueueOne,
                action ?? new ScheduledAnnouncementAction("hello survivors"),
                null,
                null,
                rowVersion);

        [Trait("Capability", "Operations")]

        [Trait("Boundary", "Application")]

        private sealed class RecordingScheduleStore : IScheduleStore
        {
            private readonly List<ScheduleRecord> records = new();

            public ScheduleDefinition? LastUpsert { get; private set; }
            public bool ConflictOnUpsert { get; set; }

            public void Seed(ScheduleRecord record)
            {
                records.RemoveAll(candidate => candidate.Id == record.Id);
                records.Add(record);
            }

            public IReadOnlyList<ScheduleRecord> List() => records.ToArray();

            public ScheduleRecord? Get(Guid scheduleId) =>
                records.SingleOrDefault(record => record.Id == scheduleId);

            public ScheduleRecord Upsert(ScheduleDefinition definition)
            {
                if (ConflictOnUpsert)
                    throw new InvalidOperationException("schedule_row_version_conflict");

                LastUpsert = definition;
                var existing = Get(definition.Id);
                var record = new ScheduleRecord(
                    definition.Id,
                    definition.Name,
                    definition.CronExpression,
                    definition.TimeZoneId,
                    definition.Enabled,
                    definition.ConcurrencyPolicy,
                    definition.Action,
                    null,
                    null,
                    existing == null ? 0 : existing.RowVersion + 1);
                Seed(record);
                return record;
            }

            public bool Delete(Guid scheduleId, long expectedRowVersion)
            {
                var current = Get(scheduleId);
                if (current == null || current.RowVersion != expectedRowVersion) return false;
                records.Remove(current);
                return true;
            }

            public IReadOnlyList<ScheduleRecord> ClaimDue(DateTimeOffset now, string ownerId) =>
                Array.Empty<ScheduleRecord>();

            public void RecordOutcome(ScheduleRunOutcome outcome)
            {
            }
        }
    }
}
