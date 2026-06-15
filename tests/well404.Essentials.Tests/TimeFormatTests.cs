using well404.Essentials.Util;
using Xunit;

namespace well404.Essentials.Tests
{
    public class TimeFormatTests
    {
        [Theory]
        [InlineData(0, "0s")]
        [InlineData(42, "42s")]
        [InlineData(59.4, "1m")]       // rounds up to 60s = exactly one minute
        [InlineData(90, "1m 30s")]
        [InlineData(120, "2m")]
        [InlineData(3600, "1h")]
        [InlineData(3660, "1h 1m")]
        [InlineData(86400, "1d")]
        [InlineData(90000, "1d 1h")]   // 25 hours
        public void Humanize_FormatsCompactly(double seconds, string expected)
        {
            Assert.Equal(expected, TimeFormat.Humanize(seconds));
        }

        [Fact]
        public void Humanize_NegativeIsZero()
        {
            Assert.Equal("0s", TimeFormat.Humanize(-5));
        }
    }
}
