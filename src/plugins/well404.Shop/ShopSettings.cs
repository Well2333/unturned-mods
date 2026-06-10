using System.Collections.Generic;

namespace well404.Shop
{
    /// <summary>Strongly-typed view of the Shop <c>config.yaml</c>.</summary>
    public class ShopSettings
    {
        public DiscountSettings Discounts { get; set; } = new DiscountSettings();

        public List<ShopEntry> Items { get; set; } = new List<ShopEntry>();
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

    public class ShopEntry
    {
        /// <summary>The id used in <c>/buy</c> and <c>/sell</c>.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary><c>item</c> (a single Unturned item) or <c>bundle</c> (a custom combo pack).</summary>
        public string Type { get; set; } = "item";

        /// <summary>Unturned item asset id. Used when <see cref="Type"/> is <c>item</c>.</summary>
        public ushort ItemId { get; set; }

        /// <summary>How many items one purchase unit grants (for <c>item</c> entries).</summary>
        public int Amount { get; set; } = 1;

        /// <summary>Price to buy one unit. 0 makes the entry unbuyable.</summary>
        public decimal BuyPrice { get; set; }

        /// <summary>Payout to sell one unit. 0 makes the entry unsellable.</summary>
        public decimal SellPrice { get; set; }

        /// <summary>Bundle contents. Used when <see cref="Type"/> is <c>bundle</c>.</summary>
        public List<BundleItem> Contents { get; set; } = new List<BundleItem>();

        public bool IsBundle => string.Equals(Type, "bundle", System.StringComparison.OrdinalIgnoreCase);
    }

    public class BundleItem
    {
        public ushort ItemId { get; set; }
        public int Amount { get; set; } = 1;
    }
}
