using System;
using System.Collections.Concurrent;

namespace well404.Essentials.Back
{
    /// <summary>
    /// Tracks short-lived damage immunity granted after <c>/back</c>. The damage listener
    /// cancels incoming damage while a player is protected. Registered as a plugin-scoped
    /// singleton.
    /// </summary>
    public sealed class InvincibilityService
    {
        private readonly ConcurrentDictionary<ulong, DateTime> m_Until =
            new ConcurrentDictionary<ulong, DateTime>();

        public void Protect(ulong steamId, int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            m_Until[steamId] = DateTime.UtcNow.AddSeconds(seconds);
        }

        public bool IsProtected(ulong steamId)
        {
            if (m_Until.TryGetValue(steamId, out var until))
            {
                if (until > DateTime.UtcNow)
                {
                    return true;
                }

                m_Until.TryRemove(steamId, out _);
            }

            return false;
        }
    }
}
