using System;
using System.Collections.Generic;
using System.Globalization;
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

        // ----- plain items (keyed by game item id) -----

        public IReadOnlyList<ShopItemConfig> Items
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<ShopItemConfig>(m_Settings.Items);
                }
            }
        }

        /// <summary>Adds the item, or replaces an existing one with the same item id.</summary>
        public void UpsertItem(ShopItemConfig item)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Items.FindIndex(x => x.ItemId == item.ItemId);
                if (index >= 0)
                {
                    m_Settings.Items[index] = item;
                }
                else
                {
                    m_Settings.Items.Add(item);
                }

                Save();
            }
        }

        /// <summary>Removes the plain item with this item id (parsed from the record key).</summary>
        public bool RemoveItem(string itemIdKey)
        {
            if (!ushort.TryParse(itemIdKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId))
            {
                return false;
            }

            lock (m_Lock)
            {
                var index = m_Settings.Items.FindIndex(x => x.ItemId == itemId);
                if (index < 0)
                {
                    return false;
                }

                m_Settings.Items.RemoveAt(index);
                Save();
                return true;
            }
        }

        // ----- bundles (keyed by their own id) -----

        public IReadOnlyList<ShopBundleConfig> Bundles
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<ShopBundleConfig>(m_Settings.Bundles);
                }
            }
        }

        /// <summary>Adds the bundle, or replaces an existing one with the same id (case-insensitive).</summary>
        public void UpsertBundle(ShopBundleConfig bundle)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Bundles.FindIndex(
                    x => string.Equals(x.Id, bundle.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    m_Settings.Bundles[index] = bundle;
                }
                else
                {
                    m_Settings.Bundles.Add(bundle);
                }

                Save();
            }
        }

        /// <summary>Removes the bundle with this id; returns whether one was removed.</summary>
        public bool RemoveBundle(string id)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Bundles.FindIndex(
                    x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return false;
                }

                m_Settings.Bundles.RemoveAt(index);
                Save();
                return true;
            }
        }

        // ----- discounts -----

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
