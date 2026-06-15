using System;
using System.Collections.Concurrent;

namespace well404.Essentials.Teleport
{
    /// <summary>
    /// In-memory per-(player, key) cooldown tracker. Cooldowns reset on plugin reload,
    /// which is the accepted trade-off for staying dependency-free and simple.
    /// Registered as a plugin-scoped singleton.
    /// </summary>
    public sealed class CooldownManager
    {
        private readonly ConcurrentDictionary<string, DateTime> m_LastUse =
            new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        private static string Compose(string steamId, string key) => steamId + "|" + key;

        /// <summary>Seconds left on the cooldown, or 0 if ready (or the cooldown is disabled).</summary>
        public double GetRemainingSeconds(string steamId, string key, int cooldownSeconds)
        {
            if (cooldownSeconds <= 0)
            {
                return 0;
            }

            if (m_LastUse.TryGetValue(Compose(steamId, key), out var last))
            {
                var remaining = cooldownSeconds - (DateTime.UtcNow - last).TotalSeconds;
                return remaining > 0 ? remaining : 0;
            }

            return 0;
        }

        /// <summary>Stamps the cooldown as used now.</summary>
        public void Mark(string steamId, string key) => m_LastUse[Compose(steamId, key)] = DateTime.UtcNow;
    }
}
