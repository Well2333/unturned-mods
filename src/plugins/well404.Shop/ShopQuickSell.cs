using System.Collections.Generic;

namespace well404.Shop
{
    public static class ShopQuickSell
    {
        public static IReadOnlyDictionary<ushort, ShopEntry> EligibleEntries(IEnumerable<ShopEntry> entries)
        {
            var result = new Dictionary<ushort, ShopEntry>();
            foreach (var entry in entries)
            {
                if (entry.SellPrice > 0m && !result.ContainsKey(entry.ItemId))
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
