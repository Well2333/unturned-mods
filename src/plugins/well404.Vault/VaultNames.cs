using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.Items;

namespace well404.Vault
{
    /// <summary>
    /// Resolves game item-id → display-name via OpenMod's <see cref="IItemDirectory"/>. Used instead
    /// of <c>SDG.Unturned.Assets.find</c> because the directory is safe to read from the web/command
    /// threads the menu and commands run on, whereas <c>Assets.find</c> only resolves on the main
    /// thread.
    /// </summary>
    internal static class VaultNames
    {
        public static Task<IReadOnlyDictionary<string, LocalizedItemInfo>> BuildMapAsync(IItemDirectory itemDirectory)
            => LocalizedItemCatalog.BuildAsync(itemDirectory);

        public static string NameOf(ushort itemId, IReadOnlyDictionary<string, LocalizedItemInfo> names, string language = "en")
            => LocalizedItemCatalog.DisplayName(itemId, names, language);

        public static bool ShowsQuality(ushort itemId, IReadOnlyDictionary<string, LocalizedItemInfo> names)
            => LocalizedItemCatalog.Get(itemId, names).ShowsQuality;
    }
}
