using System;

namespace well404.Essentials.Gift
{
    /// <summary>
    /// Pure (game-runtime-free) gift claim-eligibility rules, split out so they can be unit-tested
    /// without the game assemblies. A player may claim once per cron period; an empty/invalid cron
    /// means the gift is claimable exactly once ever.
    /// </summary>
    public static class GiftEligibility
    {
        /// <summary>
        /// Whether a gift with the given cron can be claimed at <paramref name="now"/> (server local
        /// time) by a player whose last claim was <paramref name="lastClaimUnix"/> (Unix UTC seconds,
        /// null if never). When not claimable, <paramref name="refreshInSeconds"/> is the time until
        /// the next refresh (0 when claimable or when it never refreshes).
        /// </summary>
        public static bool IsClaimable(string? cronExpression, long? lastClaimUnix, DateTime now, out double refreshInSeconds)
        {
            refreshInSeconds = 0;

            var cron = CronSchedule.TryParse(cronExpression);
            if (cron == null)
            {
                // No (valid) schedule: claimable exactly once, ever.
                return lastClaimUnix == null;
            }

            var previous = cron.GetPreviousOccurrence(now);
            if (previous == null)
            {
                return lastClaimUnix == null;
            }

            var boundaryUnix = new DateTimeOffset(previous.Value).ToUnixTimeSeconds();
            var claimable = lastClaimUnix == null || lastClaimUnix.Value < boundaryUnix;
            if (!claimable)
            {
                var next = cron.GetNextOccurrence(now);
                if (next != null)
                {
                    refreshInSeconds = (next.Value - now).TotalSeconds;
                }
            }

            return claimable;
        }
    }
}
