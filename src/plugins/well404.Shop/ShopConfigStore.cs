using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace well404.Shop
{
    /// <summary>
    /// The web panel's authoritative, in-memory view of the shop catalog, persisted by
    /// rewriting the working-directory <c>config.yaml</c>. After a write, OpenMod's
    /// file-watch reloads the game-side configuration, so <c>/buy</c> / <c>/sell</c> pick
    /// up the change (eventually consistent, sub-second).
    /// <para>
    /// It seeds its copy once from <see cref="IConfiguration"/> at construction and never
    /// re-reads it afterwards. This is deliberate: OpenMod's config reload is asynchronous
    /// and debounced, so re-reading <c>IConfiguration</c> before each edit would race —
    /// a burst of edits would read stale state and clobber each other. A fresh store is
    /// built on every plugin (re)load, so it re-syncs with the file then.
    /// </para>
    /// </summary>
    public sealed class ShopConfigStore
    {
        private readonly string m_ConfigPath;
        private readonly object m_Lock = new object();
        private readonly ShopSettings m_Settings;

        public ShopConfigStore(IConfiguration configuration, string workingDirectory)
        {
            m_ConfigPath = Path.Combine(workingDirectory, "config.yaml");
            m_Settings = configuration.Get<ShopSettings>() ?? new ShopSettings();
        }

        /// <summary>A snapshot copy of the current catalog entries.</summary>
        public IReadOnlyList<ShopEntry> Items
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<ShopEntry>(m_Settings.Items);
                }
            }
        }

        public ShopEntry? Find(string id)
        {
            lock (m_Lock)
            {
                return m_Settings.Items.Find(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Adds the entry, or replaces an existing one with the same id (case-insensitive).</summary>
        public void Upsert(ShopEntry entry)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Items.FindIndex(
                    e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    m_Settings.Items[index] = entry;
                }
                else
                {
                    m_Settings.Items.Add(entry);
                }

                Save();
            }
        }

        /// <summary>Removes the entry with <paramref name="id"/>; returns whether one was removed.</summary>
        public bool Remove(string id)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Items.FindIndex(
                    e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return false;
                }

                m_Settings.Items.RemoveAt(index);
                Save();
                return true;
            }
        }

        /// <summary>Runs <paramref name="reader"/> against the discount settings under the lock.</summary>
        public T ReadDiscounts<T>(Func<DiscountSettings, T> reader)
        {
            lock (m_Lock)
            {
                return reader(m_Settings.Discounts);
            }
        }

        /// <summary>Applies <paramref name="mutate"/> to the discount settings and rewrites the config.</summary>
        public void UpdateDiscounts(Action<DiscountSettings> mutate)
        {
            lock (m_Lock)
            {
                mutate(m_Settings.Discounts);
                Save();
            }
        }

        // Caller holds m_Lock.
        private void Save()
            => File.WriteAllText(m_ConfigPath, ShopYaml.Serialize(m_Settings), new UTF8Encoding(false));
    }
}
