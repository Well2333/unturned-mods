using System;
using System.Collections.Generic;
using System.Globalization;

namespace well404.Shop
{
    /// <summary>
    /// The resolved, kind-agnostic view of a buyable/sellable thing, built from either a
    /// <see cref="ShopItemConfig"/> or a <see cref="ShopBundleConfig"/>. Commands, the inventory
    /// service and the player menu work against this uniform shape and only branch on
    /// <see cref="IsBundle"/> where it matters (contents vs a single item).
    /// </summary>
    public sealed class ShopEntry
    {
        private ShopEntry(
            string id, bool isBundle, ushort itemId, string bundleName,
            decimal buyPrice, decimal sellPrice, IReadOnlyList<BundleItem> contents)
        {
            Id = id;
            IsBundle = isBundle;
            ItemId = itemId;
            BundleName = bundleName;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
            Contents = contents;
        }

        /// <summary>
        /// Reference key for <c>/buy</c>/<c>/sell</c> and card actions: a plain item's game item id
        /// (as text), or a bundle's own id.
        /// </summary>
        public string Id { get; }

        public bool IsBundle { get; }

        /// <summary>The game item id (plain items only; 0 for bundles).</summary>
        public ushort ItemId { get; }

        /// <summary>The configured display name (bundles only; empty for items — resolve from the directory).</summary>
        public string BundleName { get; }

        public decimal BuyPrice { get; }

        public decimal SellPrice { get; }

        /// <summary>Bundle contents (empty for plain items).</summary>
        public IReadOnlyList<BundleItem> Contents { get; }

        public static ShopEntry FromItem(ShopItemConfig c)
            => new ShopEntry(
                c.ItemId.ToString(CultureInfo.InvariantCulture), false, c.ItemId, string.Empty,
                c.BuyPrice, c.SellPrice, Array.Empty<BundleItem>());

        public static ShopEntry FromBundle(ShopBundleConfig c)
            => new ShopEntry(
                c.Id, true, 0, c.Name, c.BuyPrice, c.SellPrice,
                c.Contents ?? (IReadOnlyList<BundleItem>)Array.Empty<BundleItem>());
    }
}
