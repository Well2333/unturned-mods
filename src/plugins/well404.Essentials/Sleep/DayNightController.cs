using SDG.Unturned;

namespace well404.Essentials.Sleep
{
    /// <summary>
    /// Flips the world between day and night. Unturned models time of day as
    /// <see cref="LightingManager.time"/> (a <c>uint</c> in <c>[0, cycle)</c>); the normalised
    /// fraction <c>time / cycle</c> is daytime while it is below <see cref="LevelLighting.bias"/>.
    /// Setting <c>time</c> automatically networks the change to clients. Call on the main thread.
    /// </summary>
    public static class DayNightController
    {
        /// <summary>
        /// Calculates the target world time for a sleep-vote toggle. Day skips to the beginning of
        /// the night band (dusk); night skips to the beginning of the day band (dawn), rather
        /// than the middle of that band (noon).
        /// </summary>
        public static uint CalculateTargetTime(bool currentlyDay, uint cycle, float bias)
        {
            if (cycle == 0)
            {
                return 0;
            }

            if (bias <= 0.05f || bias >= 0.95f)
            {
                // Guard against degenerate map lighting so the dusk target stays sane.
                bias = 0.6f;
            }

            if (!currentlyDay)
            {
                // Unturned's day band begins at time zero: this is dawn, not midday.
                return 0;
            }

            // The night band begins at bias: this is dusk, not midnight.
            return (uint)(bias * cycle);
        }

        /// <summary>Switches day↔night. Returns true if it is now daytime, false if night.</summary>
        public static bool Toggle()
        {
            var cycle = LightingManager.cycle;
            var bias = LevelLighting.bias;
            var currentlyDay = LightingManager.isDaytime;
            LightingManager.time = CalculateTargetTime(currentlyDay, cycle, bias);

            return !currentlyDay;
        }
    }
}
