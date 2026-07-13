using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace well404.Shop
{
    /// <summary>
    /// Reads the shop catalog from <c>config.yaml</c>. Reads config on demand so
    /// edits picked up by OpenMod's config reload take effect live. Registered as a
    /// plugin-scoped singleton in <see cref="ShopContainerConfigurator"/>.
    /// </summary>
    public class ShopCatalog
    {
        private readonly IConfiguration m_Configuration;

        public ShopCatalog(IConfiguration configuration)
        {
            m_Configuration = configuration;
        }

        private ShopSettings Settings
        {
            get
            {
                var settings = m_Configuration.Get<ShopSettings>() ?? new ShopSettings();
                ShopConfiguration.Normalize(settings);
                return settings;
            }
        }

        public DiscountSettings Discounts => Settings.Discounts;

        public IReadOnlyList<ShopGroupConfig> Groups => Settings.Groups;

        /// <summary>All catalog items in configured display order.</summary>
        public IReadOnlyList<ShopEntry> Entries
        {
            get
            {
                var settings = Settings;
                var list = new List<ShopEntry>(settings.Items.Count);
                foreach (var item in settings.Items)
                {
                    list.Add(ShopEntry.FromItem(item));
                }

                list.Sort((left, right) => left.Order.CompareTo(right.Order));
                return list;
            }
        }

        /// <summary>
        /// Finds an entry by its numeric game item id.
        /// </summary>
        public ShopEntry? Find(string id)
        {
            var settings = Settings;

            if (ushort.TryParse(id, out var itemId))
            {
                var item = settings.Items.Find(x => x.ItemId == itemId);
                if (item != null)
                {
                    return ShopEntry.FromItem(item);
                }
            }

            return null;
        }
    }
}
