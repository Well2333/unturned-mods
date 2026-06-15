using System;
using well404.AutoSave;
using Xunit;

namespace well404.AutoSave.Tests
{
    public class CronScheduleTests
    {
        [Fact]
        public void EveryTenMinutes_RoundsUpToNextBoundary()
        {
            var schedule = new CronSchedule("*/10 * * * *", TimeZoneInfo.Utc);
            var from = new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Utc);

            var next = schedule.GetNextOccurrenceUtc(from);

            Assert.Equal(new DateTime(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc), next);
        }

        [Fact]
        public void OnABoundary_NextIsStrictlyAfter()
        {
            var schedule = new CronSchedule("*/10 * * * *", TimeZoneInfo.Utc);
            var from = new DateTime(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc);

            var next = schedule.GetNextOccurrenceUtc(from);

            Assert.Equal(new DateTime(2026, 1, 1, 0, 20, 0, DateTimeKind.Utc), next);
        }

        [Fact]
        public void Hourly_AdvancesToTopOfHour()
        {
            var schedule = new CronSchedule("0 * * * *", TimeZoneInfo.Utc);
            var from = new DateTime(2026, 6, 15, 9, 30, 0, DateTimeKind.Utc);

            var next = schedule.GetNextOccurrenceUtc(from);

            Assert.Equal(new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc), next);
        }

        [Fact]
        public void NonUtcInput_IsTreatedAsUtc()
        {
            var schedule = new CronSchedule("*/10 * * * *", TimeZoneInfo.Utc);
            var unspecified = new DateTime(2026, 1, 1, 0, 3, 0, DateTimeKind.Unspecified);

            var next = schedule.GetNextOccurrenceUtc(unspecified);

            Assert.Equal(new DateTime(2026, 1, 1, 0, 10, 0, DateTimeKind.Utc), next);
        }

        [Fact]
        public void Validate_AcceptsValidExpression()
        {
            Assert.Null(CronSchedule.Validate("*/10 * * * *"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not a cron")]
        [InlineData("*/10 * * *")]
        public void Validate_RejectsBadExpressions(string cron)
        {
            Assert.NotNull(CronSchedule.Validate(cron));
        }
    }
}
