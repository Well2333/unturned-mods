using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Plugins;

namespace well404.Essentials
{
    /// <summary>
    /// Authoritative in-memory config shared by WebPanel and in-game warp commands. Writes rewrite
    /// config.yaml atomically from the caller's perspective; a new store is created on plugin reload.
    /// </summary>
    public sealed class EssentialsConfigStore
    {
        private readonly IPluginAccessor<EssentialsPlugin> m_PluginAccessor;
        private readonly object m_Lock = new object();
        private readonly EssentialsSettings m_Settings;
        private bool m_WarpMigrationPending;

        public EssentialsConfigStore(IConfiguration configuration, IPluginAccessor<EssentialsPlugin> pluginAccessor)
        {
            m_PluginAccessor = pluginAccessor;
            m_Settings = configuration.Get<EssentialsSettings>() ?? new EssentialsSettings();
            m_WarpMigrationPending = NormalizeWarps(m_Settings.Warps);
        }

        /// <summary>
        /// Persists legacy warp category/order defaults after the plugin instance and working
        /// directory are available. The store can be constructed before that point.
        /// </summary>
        public void PersistMigrationIfNeeded(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException("A plugin working directory is required.", nameof(workingDirectory));
            }

            lock (m_Lock)
            {
                if (!m_WarpMigrationPending) return;
                var path = Path.Combine(workingDirectory, "config.yaml");
                File.WriteAllText(path, EssentialsYaml.Serialize(m_Settings), new UTF8Encoding(false));
                m_WarpMigrationPending = false;
            }
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

        public T Read<T>(Func<EssentialsSettings, T> reader)
        {
            lock (m_Lock) return reader(m_Settings);
        }

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
                lock (m_Lock) return m_Settings.Warps.OrderBy(w => w.Order).ToList();
            }
        }

        public WarpEntry? FindWarp(string name)
        {
            lock (m_Lock)
            {
                return m_Settings.Warps.Find(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        public void UpsertWarp(WarpEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(w => string.Equals(w.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    var existing = m_Settings.Warps[index];
                    if (string.IsNullOrWhiteSpace(entry.Category)) entry.Category = existing.Category;
                    if (entry.Order <= 0) entry.Order = existing.Order;
                    m_Settings.Warps[index] = entry;
                }
                else
                {
                    entry.Order = m_Settings.Warps.Count == 0 ? 1 : m_Settings.Warps.Max(w => w.Order) + 1;
                    m_Settings.Warps.Add(entry);
                }

                entry.Category = NormalizeCategory(entry.Category);
                NormalizeWarps(m_Settings.Warps);
                Save();
            }
        }

        /// <summary>Reorders every entry in exactly one category and rejects partial/stale lists.</summary>
        public bool ReorderWarps(string category, IReadOnlyList<string> names)
        {
            lock (m_Lock)
            {
                category = NormalizeCategory(category);
                var ordered = m_Settings.Warps.OrderBy(w => w.Order).ToList();
                var categoryEntries = ordered.Where(w => string.Equals(w.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
                if (categoryEntries.Count != names.Count
                    || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count
                    || names.Any(name => categoryEntries.All(w => !string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))))
                {
                    return false;
                }

                var byName = categoryEntries.ToDictionary(w => w.Name, StringComparer.OrdinalIgnoreCase);
                var replacements = names.Select(name => byName[name]).ToList();
                var replacementIndex = 0;
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (string.Equals(ordered[i].Category, category, StringComparison.OrdinalIgnoreCase))
                    {
                        ordered[i] = replacements[replacementIndex++];
                    }
                }

                m_Settings.Warps.Clear();
                m_Settings.Warps.AddRange(ordered);
                NormalizeWarps(m_Settings.Warps, true);
                Save();
                return true;
            }
        }

        public bool RemoveWarp(string name)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Warps.FindIndex(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return false;
                m_Settings.Warps.RemoveAt(index);
                NormalizeWarps(m_Settings.Warps, true);
                Save();
                return true;
            }
        }

        public IReadOnlyList<GiftEntry> Gifts
        {
            get
            {
                lock (m_Lock) return new List<GiftEntry>(m_Settings.Gifts);
            }
        }

        public void UpsertGift(GiftEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(g => string.Equals(g.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) m_Settings.Gifts[index] = entry;
                else m_Settings.Gifts.Add(entry);
                Save();
            }
        }

        public bool RemoveGift(string id)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Gifts.FindIndex(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0) return false;
                m_Settings.Gifts.RemoveAt(index);
                Save();
                return true;
            }
        }

        internal static string NormalizeCategory(string? category)
            => string.IsNullOrWhiteSpace(category) ? "default" : category.Trim();

        internal static bool NormalizeWarps(List<WarpEntry> warps, bool forceOrder = false)
        {
            var changed = false;
            var used = new HashSet<int>();
            var next = 1;
            foreach (var warp in warps)
            {
                var category = NormalizeCategory(warp.Category);
                if (!string.Equals(category, warp.Category, StringComparison.Ordinal)) changed = true;
                warp.Category = category;

                if (forceOrder || warp.Order <= 0 || used.Contains(warp.Order))
                {
                    while (used.Contains(next)) next++;
                    if (warp.Order != next) changed = true;
                    warp.Order = next;
                }
                used.Add(warp.Order);
                next = Math.Max(next, warp.Order + 1);
            }
            return changed;
        }

        private void Save()
            => File.WriteAllText(ConfigPath, EssentialsYaml.Serialize(m_Settings), new UTF8Encoding(false));
    }
}
