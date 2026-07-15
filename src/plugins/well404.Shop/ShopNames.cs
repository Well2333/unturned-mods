using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.Items;

namespace well404.Shop
{
    /// <summary>
    /// Resolves game item-id → display-name once and formats item references. Plain items have no
    /// stored name (kept minimal: id + prices), so every surface — commands, the player menu and the
    /// admin records — resolves their names from the item directory through this single helper.
    /// </summary>
    internal static class ShopNames
    {
        public static Task<IReadOnlyDictionary<string, LocalizedItemInfo>> BuildMapAsync(IItemDirectory itemDirectory)
            => LocalizedItemCatalog.BuildAsync(itemDirectory);

        public static string DisplayName(ShopEntry entry, IReadOnlyDictionary<string, LocalizedItemInfo> names, string language = "en")
            => NameOf(entry.ItemId, names, language);

        public static string NameOf(ushort itemId, IReadOnlyDictionary<string, LocalizedItemInfo> names, string language = "en")
            => LocalizedItemCatalog.DisplayName(itemId, names, language);

        internal static string? ParseName(string contents) => LocalizedItemCatalog.ParseName(contents);
    }
}
