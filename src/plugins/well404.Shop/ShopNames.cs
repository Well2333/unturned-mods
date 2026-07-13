using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Items;

namespace well404.Shop
{
    /// <summary>
    /// Resolves game item-id → display-name once and formats item references. Plain items have no
    /// stored name (kept minimal: id + prices), so every surface — commands, the player menu and the
    /// admin records — resolves their names from the item directory through this single helper.
    /// </summary>
    internal static class ShopNames
    {
        private static readonly string[] s_ChineseFileNames =
        {
            "sChinese.dat",
            "SChinese.dat",
            "Chinese.dat",
            "SimplifiedChinese.dat",
            "zh-CN.dat",
            "zh_CN.dat"
        };

        private static readonly ConcurrentDictionary<string, string> s_DisplayNameCache =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
                    names[assetId] = ResolveDisplayName(asset);
                }
            }

            return names;
        }

        /// <summary>The resolved display name of a catalog entry.</summary>
        public static string DisplayName(ShopEntry entry, IReadOnlyDictionary<string, string> names)
            => NameOf(entry.ItemId, names);

        /// <summary>A game item's resolved name, or <c>#id</c> when unknown.</summary>
        public static string NameOf(ushort itemId, IReadOnlyDictionary<string, string> names)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            return names.TryGetValue(id, out var n) && n.Length > 0 ? n : "#" + id;
        }

        private static string ResolveDisplayName(IItemAsset asset)
        {
            var current = (asset.ItemName ?? string.Empty).Trim();
            if (!(asset is UnturnedItemAsset unturnedAsset))
            {
                return current;
            }

            var originPath = unturnedAsset.ItemAsset.absoluteOriginFilePath;
            if (string.IsNullOrWhiteSpace(originPath))
            {
                return current;
            }

            return s_DisplayNameCache.GetOrAdd(originPath,
                _ => ResolveFromAssetFolder(originPath, current));
        }

        private static string ResolveFromAssetFolder(string originPath, string current)
        {
            try
            {
                var directory = Path.GetDirectoryName(originPath);
                if (string.IsNullOrEmpty(directory))
                {
                    return current;
                }

                var english = ReadName(Path.Combine(directory, "English.dat")) ?? current;
                string? chinese = null;
                foreach (var fileName in s_ChineseFileNames)
                {
                    chinese = ReadName(Path.Combine(directory, fileName));
                    if (!string.IsNullOrWhiteSpace(chinese))
                    {
                        break;
                    }
                }

                return Combine(english, chinese, current);
            }
            catch (IOException)
            {
                return current;
            }
            catch (UnauthorizedAccessException)
            {
                return current;
            }
            catch (ArgumentException)
            {
                return current;
            }
        }

        private static string? ReadName(string path)
            => File.Exists(path) ? ParseName(File.ReadAllText(path)) : null;

        internal static string Combine(string? english, string? chinese, string? fallback)
        {
            english = string.IsNullOrWhiteSpace(english) ? fallback?.Trim() : english.Trim();
            chinese = chinese?.Trim();
            if (string.IsNullOrWhiteSpace(chinese))
            {
                return english ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(english)
                || string.Equals(chinese, english, StringComparison.OrdinalIgnoreCase))
            {
                return chinese;
            }
            return chinese + " (" + english + ")";
        }

        internal static string? ParseName(string contents)
        {
            using (var reader = new StringReader(contents))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim().TrimStart('\uFEFF');
                    if (!line.StartsWith("Name", StringComparison.Ordinal)
                        || line.Length == 4 || !char.IsWhiteSpace(line[4]))
                    {
                        continue;
                    }

                    var value = line.Substring(5).Trim();
                    if (value.Length == 0)
                    {
                        return null;
                    }
                    if (value[0] == '"')
                    {
                        var escaped = false;
                        var result = new System.Text.StringBuilder();
                        for (var i = 1; i < value.Length; i++)
                        {
                            var c = value[i];
                            if (escaped)
                            {
                                result.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c == 't' ? '\t' : c);
                                escaped = false;
                            }
                            else if (c == '\\')
                            {
                                escaped = true;
                            }
                            else if (c == '"')
                            {
                                return result.ToString().Trim();
                            }
                            else
                            {
                                result.Append(c);
                            }
                        }
                        return result.ToString().Trim();
                    }

                    var comment = value.IndexOf(" //", StringComparison.Ordinal);
                    return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
                }
            }
            return null;
        }

    }
}
