using System;
using well404.Essentials.Gift;
using Xunit;

namespace well404.Essentials.Tests
{
    public class CronScheduleTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("0 0 * *")]        // too few fields
        [InlineData("0 0 * * * *")]    // too many fields
        [InlineData("60 0 * * *")]     // minute out of range
        [InlineData("0 24 * * *")]     // hour out of range
        [InlineData("0 0 0 * *")]      // day-of-month out of range
        [InlineData("0 0 * 13 *")]     // month out of range
        [InlineData("0 0 * * 8")]      // day-of-week out of range
        [InlineData("a 0 * * *")]      // non-numeric
        public void TryParse_RejectsInvalid(string? expression)
        {
            Assert.Null(CronSchedule.TryParse(expression));
        }

        [Fact]
        public void Matches_DailyMidnight()
        {
            var cron = CronSchedule.TryParse("0 0 * * *")!;
            Assert.True(cron.Matches(new DateTime(2024, 1, 1, 0, 0, 0)));
            Assert.False(cron.Matches(new DateTime(2024, 1, 1, 0, 1, 0)));
            Assert.False(cron.Matches(new DateTime(2024, 1, 1, 12, 0, 0)));
        }

        [Fact]
        public void Matches_StepMinutes()
        {
            var cron = CronSchedule.TryParse("*/15 * * * *")!;
            Assert.True(cron.Matches(new DateTime(2024, 1, 1, 9, 0, 0)));
            Assert.True(cron.Matches(new DateTime(2024, 1, 1, 9, 15, 0)));
            Assert.True(cron.Matches(new DateTime(2024, 1, 1, 9, 45, 0)));
            Assert.False(cron.Matches(new DateTime(2024, 1, 1, 9, 10, 0)));
        }

        [Fact]
        public void Matches_DayOfWeek_MondayMidnight()
        {
            var cron = CronSchedule.TryParse("0 0 * * 1")!;
            // 2024-01-01 is a Monday; 2024-01-02 a Tuesday.
            Assert.True(cron.Matches(new DateTime(2024, 1, 1, 0, 0, 0)));
            Assert.False(cron.Matches(new DateTime(2024, 1, 2, 0, 0, 0)));
        }

        [Fact]
        public void Matches_SundayAcceptsBothZeroAndSeven()
        {
            var zero = CronSchedule.TryParse("0 0 * * 0")!;
            var seven = CronSchedule.TryParse("0 0 * * 7")!;
            // 2024-01-07 is a Sunday.
            var sunday = new DateTime(2024, 1, 7, 0, 0, 0);
            Assert.True(zero.Matches(sunday));
            Assert.True(seven.Matches(sunday));
        }

        [Fact]
        public void Matches_DomOrDowWhenBothRestricted()
        {
            // Vixie rule: with both DOM and DOW set, either matching is enough.
            var cron = CronSchedule.TryParse("0 0 13 * 5")!; // the 13th OR any Friday
            Assert.True(cron.Matches(new DateTime(2024, 1, 13, 0, 0, 0))); // 13th (a Saturday)
            Assert.True(cron.Matches(new DateTime(2024, 1, 5, 0, 0, 0)));  // a Friday
            Assert.False(cron.Matches(new DateTime(2024, 1, 6, 0, 0, 0))); // neither
        }

        [Fact]
        public void GetNextOccurrence_Daily()
        {
            var cron = CronSchedule.TryParse("0 0 * * *")!;
            var next = cron.GetNextOccurrence(new DateTime(2024, 1, 1, 12, 0, 0));
            Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0), next);
        }

        [Fact]
        public void GetPreviousOccurrence_Daily()
        {
            var cron = CronSchedule.TryParse("0 0 * * *")!;
            var previous = cron.GetPreviousOccurrence(new DateTime(2024, 1, 1, 12, 0, 0));
            Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0), previous);
        }

        [Fact]
        public void GetPreviousOccurrence_AtExactBoundary_ReturnsThatMinute()
        {
            var cron = CronSchedule.TryParse("0 0 * * *")!;
            var at = new DateTime(2024, 1, 1, 0, 0, 0);
            Assert.Equal(at, cron.GetPreviousOccurrence(at));
        }
    }
}
