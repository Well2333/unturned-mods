using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace well404.AdminTools
{
    /// <summary>
    /// Tracks which players currently have godmode. A plugin-scoped singleton; the damage listener
    /// consults it to cancel incoming damage. In-memory only (cleared on plugin reload / restart).
    /// </summary>
    public sealed class GodModeService
    {
        private readonly ConcurrentDictionary<ulong, byte> m_God = new ConcurrentDictionary<ulong, byte>();

        public bool IsGod(ulong steamId) => m_God.ContainsKey(steamId);

        /// <summary>Sets godmode on/off for a player.</summary>
        public void Set(ulong steamId, bool on)
        {
            if (on)
            {
                m_God[steamId] = 0;
            }
            else
            {
                m_God.TryRemove(steamId, out _);
            }
        }

        /// <summary>Flips godmode; returns the new state.</summary>
        public bool Toggle(ulong steamId)
        {
            if (m_God.TryRemove(steamId, out _))
            {
                return false;
            }

            m_God[steamId] = 0;
            return true;
        }

        public IReadOnlyCollection<ulong> All => m_God.Keys.ToList();
    }
}
