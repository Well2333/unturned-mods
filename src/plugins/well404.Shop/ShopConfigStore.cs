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
            if (ShopConfiguration.Normalize(m_Settings))
            {
                Save();
            }
        }

        public IReadOnlyList<ShopGroupConfig> Groups
        {
            get
            {
                lock (m_Lock)
                {
                    return new List<ShopGroupConfig>(m_Settings.Groups);
                }
            }
        }

        public string? ResolveGroupId(string group)
        {
            lock (m_Lock)
            {
                var match = m_Settings.Groups.Find(x =>
                    string.Equals(x.Id, group, StringComparison.OrdinalIgnoreCase));
                return match?.Id;
            }
        }

        public bool IsValidGroup(string group) => ResolveGroupId(group) != null;

        public void UpsertGroup(ShopGroupConfig group)
        {
            lock (m_Lock)
            {
                var index = m_Settings.Groups.FindIndex(x =>
                    string.Equals(x.Id, group.Id, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    group.Id = m_Settings.Groups[index].Id;
                    m_Settings.Groups[index] = group;
                }
                else
                {
                    m_Settings.Groups.Add(group);
                }
                Save();
            }
        }

        public bool RemoveGroup(string id)
        {
            if (string.Equals(id, ShopConfiguration.DefaultGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            lock (m_Lock)
            {
                var removed = m_Settings.Groups.RemoveAll(x =>
                    string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
                if (!removed)
                {
                    return false;
                }
                foreach (var item in m_Settings.Items)
                {
                    if (string.Equals(item.Group, id, StringComparison.OrdinalIgnoreCase))
                        item.Group = ShopConfiguration.DefaultGroupId;
                }
                Save();
                return true;
            }
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
                if (item.Order < 1)
                {
                    item.Order = index >= 0 ? m_Settings.Items[index].Order : NextOrder();
                }
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

        public int GetCatalogOrder(string? key)
        {
            if (key == null) return 0;
            lock (m_Lock)
            {
                if (key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(key.Substring(5), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var itemId))
                {
                    var item = m_Settings.Items.Find(x => x.ItemId == itemId);
                    return item?.Order ?? 0;
                }
                return 0;
            }
        }

        // ----- discounts -----


        public bool ContainsCatalogKey(string key)
        {
            lock (m_Lock)
            {
                return FindOrderSetter(key, null) != null;
            }
        }

        public bool ReorderCatalog(string group, IReadOnlyList<string> keys)
        {
            lock (m_Lock)
            {
                var slots = new List<int>();
                foreach (var item in m_Settings.Items)
                {
                    if (string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase))
                        slots.Add(item.Order);
                }
                slots.Sort();
                if (slots.Count != keys.Count) return false;

                var setters = new List<Action<int>>();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in keys)
                {
                    if (!used.Add(key)) return false;
                    var setter = FindOrderSetter(key, group);
                    if (setter == null) return false;
                    setters.Add(setter);
                }

                for (var i = 0; i < setters.Count; i++) setters[i](slots[i]);
                Save();
                return true;
            }
        }

        private Action<int>? FindOrderSetter(string key, string? group)
        {
            if (key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)
                && ushort.TryParse(key.Substring(5), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var itemId))
            {
                var item = m_Settings.Items.Find(x => x.ItemId == itemId
                    && (group == null || string.Equals(x.Group, group, StringComparison.OrdinalIgnoreCase)));
                if (item != null) return value => item.Order = value;
            }
            return null;
        }

        private int NextOrder()
        {
            var max = 0;
            foreach (var item in m_Settings.Items) if (item.Order > max) max = item.Order;
            return max + 1;
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
