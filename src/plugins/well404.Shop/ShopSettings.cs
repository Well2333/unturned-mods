using System.Collections.Generic;

namespace well404.Shop
{
    /// <summary>Strongly-typed view of the Shop <c>config.yaml</c>.</summary>
    public class ShopSettings
    {
        public DiscountSettings Discounts { get; set; } = new DiscountSettings();

        /// <summary>
        /// Plain items. A plain item is referenced by its game item id (both in <c>/buy</c>/<c>/sell</c>
        /// and in the panel); its display name is resolved from the game item directory, so it needs
        /// only a price pair. One purchase unit grants exactly one of the item.
        /// </summary>
        public List<ShopItemConfig> Items { get; set; } = new List<ShopItemConfig>();

        /// <summary>Custom bundles (a named combo of several items), referenced by their own id.</summary>
        public List<ShopBundleConfig> Bundles { get; set; } = new List<ShopBundleConfig>();
    }

    public class DiscountSettings
    {
        /// <summary>Master switch. When false, everyone pays full price (default).</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Maps a permission to a buy-price multiplier. e.g. <c>well404.shop.vip: 0.9</c>
        /// gives 10% off to anyone with that permission. The best (lowest) multiplier
        /// the actor is granted wins. Discounts apply to buying only.
        /// </summary>
        public Dictionary<string, decimal> Tiers { get; set; } = new Dictionary<string, decimal>();
    }

    /// <summary>A plain single-item shop entry: a game item sold/bought by its own id.</summary>
    public class ShopItemConfig
    {
        /// <summary>Unturned item asset id. Also the reference id used by <c>/buy</c>/<c>/sell</c>.</summary>
        public ushort ItemId { get; set; }

        /// <summary>Price to buy one. 0 makes it unbuyable.</summary>
        public decimal BuyPrice { get; set; }

        /// <summary>Payout to sell one. 0 makes it unsellable.</summary>
        public decimal SellPrice { get; set; }
    }

    /// <summary>A custom bundle: a named pack of several items, referenced by its own id.</summary>
    public class ShopBundleConfig
    {
        /// <summary>The id used in <c>/buy</c>/<c>/sell</c> (a name-like string; avoid pure numbers).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Price to buy one bundle. 0 makes it unbuyable.</summary>
        public decimal BuyPrice { get; set; }

        /// <summary>Payout to sell one bundle (requires the player to own all contents). 0 = unsellable.</summary>
        public decimal SellPrice { get; set; }

        /// <summary>The items one bundle grants (and that selling reclaims).</summary>
        public List<BundleItem> Contents { get; set; } = new List<BundleItem>();
    }

    public class BundleItem
    {
        public ushort ItemId { get; set; }
        public int Amount { get; set; } = 1;
    }
}
