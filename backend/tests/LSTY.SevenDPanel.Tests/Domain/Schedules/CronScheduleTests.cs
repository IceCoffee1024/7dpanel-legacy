using LSTY.SevenDPanel.Domain.Schedules;
using Xunit;

namespace LSTY.SevenDPanel.Tests.Domain.Schedules
{
    [Trait("Capability", "Operations")]
    [Trait("Boundary", "Domain")]
    public sealed class CronScheduleTests
    {
        [Fact]
        public void Standard_five_field_expression_uses_the_explicit_time_zone()
        {
            var schedule = CronSchedule.Create("0 9 * * *", "China Standard Time");

            var next = schedule.GetNextOccurrence(
                new DateTimeOffset(2026, 7, 26, 0, 30, 0, TimeSpan.Zero));

            Assert.Equal(
                new DateTimeOffset(2026, 7, 26, 1, 0, 0, TimeSpan.Zero),
                next);
            Assert.Equal("0 9 * * *", schedule.Expression);
            Assert.Equal("China Standard Time", schedule.TimeZoneId);
        }

        [Fact]
        public void Day_of_month_and_day_of_week_use_and_semantics()
        {
            var schedule = CronSchedule.Create("0 0 13 * 5", "UTC");

            var next = schedule.GetNextOccurrence(
                new DateTimeOffset(2023, 10, 1, 0, 0, 0, TimeSpan.Zero));

            Assert.Equal(
                new DateTimeOffset(2023, 10, 13, 0, 0, 0, TimeSpan.Zero),
                next);
        }

        [Fact]
        public void Daylight_saving_gap_is_resolved_by_cronos_for_the_selected_zone()
        {
            var schedule = CronSchedule.Create("0 2 * * *", "Eastern Standard Time");

            var next = schedule.GetNextOccurrence(
                new DateTimeOffset(2024, 3, 10, 6, 59, 0, TimeSpan.Zero));

            Assert.Equal(
                new DateTimeOffset(2024, 3, 10, 7, 0, 0, TimeSpan.Zero),
                next);
        }

        [Fact]
        public void Previous_occurrence_returns_the_latest_due_instant()
        {
            var schedule = CronSchedule.Create("*/5 * * * *", "UTC");
            var now = new DateTimeOffset(2026, 7, 26, 0, 17, 0, TimeSpan.Zero);

            Assert.Equal(
                new DateTimeOffset(2026, 7, 26, 0, 15, 0, TimeSpan.Zero),
                schedule.GetPreviousOccurrence(now, inclusive: true));
            Assert.Equal(
                new DateTimeOffset(2026, 7, 26, 0, 15, 0, TimeSpan.Zero),
                schedule.GetPreviousOccurrence(
                    new DateTimeOffset(2026, 7, 26, 0, 15, 0, TimeSpan.Zero),
                    inclusive: true));
        }

        [Fact]
        public void Invalid_cron_has_a_stable_error_code()
        {
            var error = Assert.Throws<CronScheduleValidationException>(() =>
                CronSchedule.Create("not a cron", "UTC"));

            Assert.Equal("cron_invalid", error.Code);
        }

        [Fact]
        public void Unknown_time_zone_has_a_stable_error_code()
        {
            var error = Assert.Throws<CronScheduleValidationException>(() =>
                CronSchedule.Create("0 9 * * *", "Mars/Olympus_Mons"));

            Assert.Equal("time_zone_invalid", error.Code);
        }

        [Fact]
        public void From_time_must_be_utc()
        {
            var schedule = CronSchedule.Create("0 9 * * *", "UTC");

            Assert.Throws<ArgumentException>(() =>
                schedule.GetNextOccurrence(
                    new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.FromHours(8))));
            Assert.Throws<ArgumentException>(() =>
                schedule.GetPreviousOccurrence(
                    new DateTimeOffset(2026, 7, 26, 8, 0, 0, TimeSpan.FromHours(8))));
        }
    }
}
