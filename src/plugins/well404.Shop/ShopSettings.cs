using System.Collections.Generic;

namespace well404.Shop
{
    /// <summary>Strongly-typed view of the Shop <c>config.yaml</c>.</summary>
    public class ShopSettings
    {
        public DiscountSettings Discounts { get; set; } = new DiscountSettings();

        public List<ShopGroupConfig> Groups { get; set; } = new List<ShopGroupConfig>();

        /// <summary>
        /// Plain items. A plain item is referenced by its game item id (both in <c>/buy</c>/<c>/sell</c>
        /// and in the panel); its display name is resolved from the game item directory, so it needs
        /// only a price pair. One purchase unit grants exactly one of the item.
        /// </summary>
        public List<ShopItemConfig> Items { get; set; } = new List<ShopItemConfig>();

    }

    public class ShopGroupConfig
    {
        public string Id { get; set; } = "default";
        public string Name { get; set; } = "default";
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

        public string Group { get; set; } = "default";

        public string Note { get; set; } = string.Empty;

        public int Order { get; set; }
    }

}
