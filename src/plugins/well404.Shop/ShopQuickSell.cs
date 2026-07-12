using System;
using System.Collections.Generic;
using UnturnedMods.Shared.WebPanel;

namespace well404.Shop
{
    public static class ShopQuickSell
    {
        public const string CardKey = "__quick_sell__";
        public const string ActionId = "sell_all";

        public static PlayerCard CreateCard(Func<string, string> translate)
        {
            return new PlayerCard(
                CardKey,
                translate("Quick sell"),
                new[]
                {
                    translate("Sell every sellable plain item in your inventory at once. Bundles are not included.")
                },
                null,
                new[] { new PlayerButton(ActionId, translate("Sell all"), "danger") },
                translate("Quick actions"));
        }

        public static IReadOnlyDictionary<ushort, ShopEntry> EligibleEntries(IEnumerable<ShopEntry> entries)
        {
            var result = new Dictionary<ushort, ShopEntry>();
            foreach (var entry in entries)
            {
                if (!entry.IsBundle && entry.SellPrice > 0m && !result.ContainsKey(entry.ItemId))
                {
                    result.Add(entry.ItemId, entry);
                }
            }
            return result;
        }

        public static decimal CalculateTotal(
            IReadOnlyDictionary<ushort, ShopEntry> entries,
            IReadOnlyDictionary<ushort, int> amounts)
        {
            decimal total = 0m;
            foreach (var amount in amounts)
            {
                if (amount.Value > 0 && entries.TryGetValue(amount.Key, out var entry))
                {
                    total += entry.SellPrice * amount.Value;
                }
            }
            return total;
        }
    }
}
