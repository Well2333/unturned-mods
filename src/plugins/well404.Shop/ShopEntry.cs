using System.Globalization;

namespace well404.Shop
{
    /// <summary>
    /// The resolved view of one game item in the shop catalog.
    /// </summary>
    public sealed class ShopEntry
    {
        private ShopEntry(string id, ushort itemId, decimal buyPrice, decimal sellPrice,
            string group, string note, int order)
        {
            Id = id;
            ItemId = itemId;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
            Group = group;
            Note = note;
            Order = order;
        }

        /// <summary>
        /// Reference key for <c>/buy</c>/<c>/sell</c> and card actions: the game item id as text.
        /// </summary>
        public string Id { get; }

        /// <summary>The game item id.</summary>
        public ushort ItemId { get; }

        public decimal BuyPrice { get; }

        public decimal SellPrice { get; }

        public string Group { get; }
        public string Note { get; }
        public int Order { get; }
        public static ShopEntry FromItem(ShopItemConfig c)
            => new ShopEntry(
                c.ItemId.ToString(CultureInfo.InvariantCulture), c.ItemId, c.BuyPrice, c.SellPrice,
                c.Group, c.Note, c.Order);
    }
}
