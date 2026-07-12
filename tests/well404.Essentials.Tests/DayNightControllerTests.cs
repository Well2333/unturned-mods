using well404.Essentials.Sleep;
using Xunit;

namespace well404.Essentials.Tests
{
    public class DayNightControllerTests
    {
        [Fact]
        public void CalculateTargetTime_NightToDay_TargetsDawn()
        {
            Assert.Equal(0u, DayNightController.CalculateTargetTime(
                currentlyDay: false, cycle: 3600u, bias: 0.6f));
        }

        [Fact]
        public void CalculateTargetTime_DayToNight_TargetsDusk()
        {
            // Day occupies [0, 2160); dusk begins at 2160.
            Assert.Equal(2160u, DayNightController.CalculateTargetTime(
                currentlyDay: true, cycle: 3600u, bias: 0.6f));
        }

        [Fact]
        public void CalculateTargetTime_DegenerateBias_UsesSafeDuskFallback()
        {
            Assert.Equal(2160u, DayNightController.CalculateTargetTime(
                currentlyDay: true, cycle: 3600u, bias: 1f));
        }
    }
}
