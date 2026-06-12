using System;
using well404.Essentials.Gift;
using Xunit;

namespace well404.Essentials.Tests
{
    public class GiftEligibilityTests
    {
        private static long Unix(DateTime local) => new DateTimeOffset(local).ToUnixTimeSeconds();

        [Fact]
        public void NoCron_ClaimableOnceEver()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0);
            Assert.True(GiftEligibility.IsClaimable("", null, now, out _));
            Assert.False(GiftEligibility.IsClaimable("", Unix(now.AddDays(-3)), now, out var refresh));
            Assert.Equal(0, refresh);
        }

        [Fact]
        public void Daily_ClaimableWhenLastClaimBeforeTodaysBoundary()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0);
            var beforeMidnight = Unix(new DateTime(2024, 1, 1, 0, 0, 0).AddHours(-1));
            Assert.True(GiftEligibility.IsClaimable("0 0 * * *", beforeMidnight, now, out _));
        }

        [Fact]
        public void Daily_NotClaimableWhenAlreadyClaimedThisPeriod()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0);
            var afterMidnight = Unix(new DateTime(2024, 1, 1, 0, 0, 0).AddHours(1));
            var claimable = GiftEligibility.IsClaimable("0 0 * * *", afterMidnight, now, out var refresh);
            Assert.False(claimable);
            // Next refresh is tomorrow's midnight, ~12 hours away.
            Assert.InRange(refresh, 11 * 3600, 13 * 3600);
        }

        [Fact]
        public void Daily_NeverClaimed_IsClaimable()
        {
            var now = new DateTime(2024, 1, 1, 12, 0, 0);
            Assert.True(GiftEligibility.IsClaimable("0 0 * * *", null, now, out _));
        }
    }
}
