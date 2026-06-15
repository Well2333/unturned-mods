using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Plugins;

namespace well404.Essentials
{
    /// <summary>
    /// The web panel's (and <c>/warp set|del</c>'s) authoritative, in-memory view of the config,
    /// persisted by rewriting the working-directory <c>config.yaml</c>. After a write, OpenMod's
    /// file-watch reloads the game-side configuration, so commands (which read
    /// <see cref="IConfiguration"/> live) pick up the change within a sub-second.
    /// <para>
    /// It seeds its copy once from <see cref="IConfiguration"/> at construction and never re-reads
    /// it (OpenMod's reload is async/debounced, so re-reading before each edit would race). A fresh
    /// store is built on every plugin (re)load, so it re-syncs with the file then. Mirrors
    /// <c>ShopConfigStore</c>.
    /// </para>
    /// </summary>
    public sealed class EssentialsConfigStore
    {
        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;
        private readonly object m_Lock = new object();
        private readonly EssentialsSettings m_Settings;

        public EssentialsConfigStore(IConfiguration configuration, IPluginAccessor<EssentialsPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
            m_Settings = configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
        }

        private string ConfigPath
        {
            get
            {
                var plugin = m_PluginAccessor.Instance
                    ?? throw new InvalidOperationException("The Essentials plugin is not loaded.");
                return Path.Combine(plugin.WorkingDirectory, "config.yaml");
            }
        }

        /// <summary>Runs <paramref name="reader"/> against the settings under the lock and returns its result.</summary>
        public T Read<T>(Func<EssentialsSettings, T> reader)
        {
            lock (m_Lock)
            {
                return reader(m_Settings);
            }
        }

        /// <summary>Applies <paramref name="mutate"/> to the settings and rewrites the config.</summary>
        public void Update(Action<EssentialsSettings> mutate)
        {
            lock (m_Lock)
            {
                mutate(m_Settings);
                Save();
            }
        }

        public IReadOnlyList<WarpEntry> Warps
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<WarpEntry>(m_Settings.Warps);
                }
            }
        }

        public WarpEntry? FindWarp(string name)
        {
            lock (m_Lock)
            {
                return m_Settings.Warps.Find(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Adds the warp, or replaces an existing one with the same name (case-insensitive).</summary>
        public void UpsertWarp(WarpEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(
                    w => string.Equals(w.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    m_Settings.Warps[index] = entry;
                }
                else
                {
                    m_Settings.Warps.Add(entry);
                }

                Save();
            }
        }

        public bool RemoveWarp(string name)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(
                    w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return false;
                }

                m_Settings.Warps.RemoveAt(index);
                Save();
                return true;
            }
        }

        public IReadOnlyList<GiftEntry> Gifts
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<GiftEntry>(m_Settings.Gifts);
                }
            }
        }

        public void UpsertGift(GiftEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(
                    g => string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    m_Settings.Gifts[index] = entry;
                }
                else
                {
                    m_Settings.Gifts.Add(entry);
                }

                Save();
            }
        }

        public bool RemoveGift(string id)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(
                    g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return false;
                }

                m_Settings.Gifts.RemoveAt(index);
                Save();
                return true;
            }
        }

        // Caller holds m_Lock.
        private void Save()
            => File.WriteAllText(ConfigPath, EssentialsYaml.Serialize(m_Settings), new UTF8Encoding(false));
    }
}
