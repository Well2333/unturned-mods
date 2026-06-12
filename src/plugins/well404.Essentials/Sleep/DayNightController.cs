using SDG.Unturned;

namespace well404.Essentials.Sleep
{
    /// <summary>
    /// Flips the world between day and night. Unturned models time of day as
    /// <see cref="LightingManager.time"/> (a <c>uint</c> in <c>[0, cycle)</c>); the normalised
    /// fraction <c>time / cycle</c> is daytime while it is below <see cref="LevelLighting.bias"/>.
    /// Setting <c>time</c> automatically networks the change to clients. Call on the main thread.
    /// </summary>
    internal static class DayNightController
    {
        /// <summary>Switches day↔night. Returns true if it is now daytime, false if night.</summary>
        public static bool Toggle()
        {
            var cycle = LightingManager.cycle;
            var bias = LevelLighting.bias;
            if (bias <= 0.05f || bias >= 0.95f)
            {
                // Guard against degenerate map lighting so our target stays in a sane band.
                bias = 0.6f;
            }

            var currentlyDay = LightingManager.isDaytime;

            // Aim for the middle of the target band so we land unambiguously in day/night.
            var targetFraction = currentlyDay
                ? bias + (1f - bias) * 0.5f // middle of the night band
                : bias * 0.5f;              // middle of the day band

            LightingManager.time = (uint)(targetFraction * cycle);

            return !currentlyDay;
        }
    }
}
