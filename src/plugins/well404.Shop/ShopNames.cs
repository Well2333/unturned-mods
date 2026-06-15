using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;

namespace well404.Shop
{
    /// <summary>
    /// Resolves game item-id → display-name once and formats item references. Plain items have no
    /// stored name (kept minimal: id + prices), so every surface — commands, the player menu and the
    /// admin records — resolves their names from the item directory through this single helper.
    /// </summary>
    internal static class ShopNames
    {
        /// <summary>Builds a game item-id → display-name lookup from the item directory (main thread).</summary>
        public static async Task<IReadOnlyDictionary<string, string>> BuildMapAsync(IItemDirectory itemDirectory)
        {
            await UniTask.SwitchToMainThread();
            var assets = await itemDirectory.GetItemAssetsAsync();
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in assets)
            {
                var assetId = asset.ItemAssetId;
                if (assetId != null && !names.ContainsKey(assetId))
                {
                    names[assetId] = asset.ItemName ?? string.Empty;
                }
            }

            return names;
        }

        /// <summary>The display name of an entry: a bundle's configured name, or a plain item's resolved name.</summary>
        public static string DisplayName(ShopEntry entry, IReadOnlyDictionary<string, string> names)
            => entry.IsBundle ? entry.BundleName : NameOf(entry.ItemId, names);

        /// <summary>A game item's resolved name, or <c>#id</c> when unknown.</summary>
        public static string NameOf(ushort itemId, IReadOnlyDictionary<string, string> names)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            return names.TryGetValue(id, out var n) && n.Length > 0 ? n : "#" + id;
        }

        /// <summary>One item reference for players: the item's name (× qty when &gt; 1).</summary>
        public static string Label(ushort itemId, int amount, IReadOnlyDictionary<string, string> names)
        {
            var name = NameOf(itemId, names);
            return amount > 1 ? name + " ×" + amount.ToString(CultureInfo.InvariantCulture) : name;
        }
    }
}
