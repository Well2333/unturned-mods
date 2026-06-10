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

        public IReadOnlyList<ShopEntry> Items => Settings.Items;

        /// <summary>Finds an entry by its id (case-insensitive), or null.</summary>
        public ShopEntry? Find(string id)
        {
            foreach (var entry in Settings.Items)
            {
                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
