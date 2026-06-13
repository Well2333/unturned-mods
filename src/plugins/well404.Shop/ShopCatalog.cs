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

        private ShopSettings Settings => m_Configuration.Get<ShopSettings>() ?? new ShopSettings();

        public DiscountSettings Discounts => Settings.Discounts;

        /// <summary>All purchasable entries — plain items first, then bundles — as a uniform list.</summary>
        public IReadOnlyList<ShopEntry> Entries
        {
            get
            {
                var settings = Settings;
                var list = new List<ShopEntry>(settings.Items.Count + settings.Bundles.Count);
                foreach (var item in settings.Items)
                {
                    list.Add(ShopEntry.FromItem(item));
                }

                foreach (var bundle in settings.Bundles)
                {
                    list.Add(ShopEntry.FromBundle(bundle));
                }

                return list;
            }
        }

        /// <summary>
        /// Finds an entry by reference id: a numeric id matching a plain item's game item id wins,
        /// otherwise a bundle whose id matches (case-insensitive). Returns null if neither matches.
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

            var bundle = settings.Bundles.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            return bundle != null ? ShopEntry.FromBundle(bundle) : null;
        }
    }
}
