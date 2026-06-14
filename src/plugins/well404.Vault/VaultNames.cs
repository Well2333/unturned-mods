using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;

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

        public static string NameOf(ushort itemId, IReadOnlyDictionary<string, string> names)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            return names.TryGetValue(id, out var n) && n.Length > 0 ? n : "#" + id;
        }
    }
}
