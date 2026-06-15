using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using OpenMod.API.Ioc;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// Global <see cref="IPlayerCommandRegistry"/> implementation — the source for the intro page's
    /// "commands you can use" list. Feature plugins register their command help on load (keyed by a
    /// source id so a reload replaces cleanly); a global singleton like the other registries.
    /// </summary>
    [ServiceImplementation(Lifetime = ServiceLifetime.Singleton)]
    public sealed class PlayerCommandRegistry : IPlayerCommandRegistry
    {
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, IReadOnlyList<PlayerCommandInfo>> m_Sources =
            new Dictionary<string, IReadOnlyList<PlayerCommandInfo>>(StringComparer.OrdinalIgnoreCase);

        public void Register(string sourceId, IReadOnlyList<PlayerCommandInfo> commands)
        {
            if (string.IsNullOrEmpty(sourceId) || commands == null)
            {
                return;
            }

            lock (m_Lock)
            {
                m_Sources[sourceId] = commands;
            }
        }

        public void Unregister(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            lock (m_Lock)
            {
                m_Sources.Remove(sourceId);
            }
        }

        public IReadOnlyList<PlayerCommandInfo> GetAll()
        {
            lock (m_Lock)
            {
                return m_Sources.Values.SelectMany(list => list).ToList();
            }
        }
    }
}
