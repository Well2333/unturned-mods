using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Items;
using SDG.Unturned;

namespace UnturnedMods.Shared.Items
{
    /// <summary>Language-aware presentation metadata for one Unturned item asset.</summary>
    public sealed class LocalizedItemInfo
    {
        public LocalizedItemInfo(string englishName, string? chineseName, bool showsQuality)
            : this(englishName, chineseName, showsQuality, "OTHER", "common", 0, "other")
        {
        }

        public LocalizedItemInfo(
            string englishName,
            string? chineseName,
            bool showsQuality,
            string itemType,
            string rarity,
            int rarityRank,
            string category)
        {
            EnglishName = englishName?.Trim() ?? string.Empty;
            ChineseName = chineseName?.Trim() ?? string.Empty;
            ShowsQuality = showsQuality;
            ItemType = string.IsNullOrWhiteSpace(itemType) ? "OTHER" : itemType.Trim().ToUpperInvariant();
            Rarity = string.IsNullOrWhiteSpace(rarity) ? "common" : rarity.Trim().ToLowerInvariant();
            RarityRank = Math.Max(0, rarityRank);
            Category = string.IsNullOrWhiteSpace(category) ? "other" : category.Trim().ToLowerInvariant();
        }

        public string EnglishName { get; }

        public string ChineseName { get; }

        /// <summary>
        /// Uses Unturned's own <c>ItemAsset.showQuality</c> rule. Every <c>Item</c> carries a raw
        /// quality byte, but it is meaningful only when the asset opts into showing quality.
        /// </summary>
        public bool ShowsQuality { get; }

        /// <summary>Unturned's native item type, normalized to an upper-case stable token.</summary>
        public string ItemType { get; }

        /// <summary>Unturned rarity normalized to lower case (common, uncommon, rare, ...).</summary>
        public string Rarity { get; }

        /// <summary>Ascending rarity rank used by presentation layers for deterministic sorting.</summary>
        public int RarityRank { get; }

        /// <summary>Stable broad category derived from <see cref="ItemType"/> for UI filtering.</summary>
        public string Category { get; }

        /// <summary>
        /// English UI receives the English name only. Chinese UI receives the Chinese name first
        /// and the English reference on a second line; renderers style that second line as subdued.
        /// </summary>
        public string DisplayName(string language)
        {
            var english = EnglishName;
            var chinese = ChineseName;
            if (IsChinese(language) && chinese.Length > 0)
            {
                return english.Length > 0 && !string.Equals(chinese, english, StringComparison.OrdinalIgnoreCase)
                    ? chinese + "\n" + english
                    : chinese;
            }

            return english.Length > 0 ? english : chinese;
        }

        public bool Matches(string query)
            => EnglishName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
               || ChineseName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsChinese(string language)
            => string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)
               || language.StartsWith("zh-", StringComparison.OrdinalIgnoreCase)
               || language.StartsWith("zh_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves English and Simplified-Chinese item localization directly beside each asset. This
    /// handles vanilla and workshop assets uniformly and keeps every plugin's item UI consistent.
    /// </summary>
    public static class LocalizedItemCatalog
    {
        private static readonly string[] s_ChineseFileNames =
        {
            "sChinese.dat",
            "SChinese.dat",
            "Schinese.dat",
            "Chinese.dat",
            "SimplifiedChinese.dat",
            "zh-CN.dat",
            "zh_CN.dat"
        };

        private static readonly ConcurrentDictionary<string, LocalizedNames> s_NameCache =
            new ConcurrentDictionary<string, LocalizedNames>(StringComparer.OrdinalIgnoreCase);

        public static async Task<IReadOnlyDictionary<string, LocalizedItemInfo>> BuildAsync(
            IItemDirectory itemDirectory)
        {
            await UniTask.SwitchToMainThread();
            var assets = await itemDirectory.GetItemAssetsAsync();
            var result = new Dictionary<string, LocalizedItemInfo>(StringComparer.Ordinal);
            foreach (var group in assets
                         .Where(asset => ushort.TryParse(asset.ItemAssetId, NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out _))
                         .GroupBy(asset => ushort.Parse(asset.ItemAssetId!, CultureInfo.InvariantCulture))
                         .OrderBy(grouping => grouping.Key))
            {
                var authoritativeAsset = Assets.find(EAssetType.ITEM, group.Key) as ItemAsset;
                if (authoritativeAsset == null)
                {
                    var fallback = group.First();
                    result[group.Key.ToString(CultureInfo.InvariantCulture)] = Resolve(fallback);
                    continue;
                }

                // Workshop packs occasionally ship several different assets with the same legacy
                // ushort ID. IItemDirectory exposes every colliding definition, but an Item stored
                // by Unturned only retains that ushort ID. Therefore presentation metadata must use
                // the exact asset selected by Unturned's own lookup rather than whichever duplicate
                // happened to be enumerated first.
                var matchingWrapper = SelectAuthoritativeCandidate(
                    group.OfType<UnturnedItemAsset>(), authoritativeAsset, candidate => candidate.ItemAsset);
                var fallbackName = matchingWrapper?.ItemName
                                   ?? group.Select(candidate => candidate.ItemName)
                                       .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
                                   ?? "#" + group.Key.ToString(CultureInfo.InvariantCulture);
                result[group.Key.ToString(CultureInfo.InvariantCulture)] =
                    Resolve(authoritativeAsset, fallbackName);
            }

            return result;
        }

        public static LocalizedItemInfo Get(
            ushort itemId, IReadOnlyDictionary<string, LocalizedItemInfo> items)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            return items.TryGetValue(id, out var item)
                ? item
                : new LocalizedItemInfo("#" + id, null, false);
        }

        public static string DisplayName(
            ushort itemId, IReadOnlyDictionary<string, LocalizedItemInfo> items, string language)
            => Get(itemId, items).DisplayName(language);

        /// <summary>
        /// Maps Unturned's native item types to broad, stable player-facing filters. Workshop items
        /// use the same type enum as vanilla assets; unknown future values safely fall back to
        /// <c>other</c> rather than being hidden.
        /// </summary>
        public static string CategoryForType(string? itemType)
        {
            var normalizedType = (itemType ?? string.Empty).Trim().ToUpperInvariant();
            switch (normalizedType)
            {
                case "MAGAZINE":
                    return "ammunition";
                case "FOOD":
                case "WATER":
                    return "food";
                case "MEDICAL":
                    return "medical";
                case "GUN":
                case "MELEE":
                case "THROWABLE":
                case "CHARGE":
                case "DETONATOR":
                    return "weapons";
                case "SUPPLY":
                    return "materials";
                case "TOOL":
                case "FISHER":
                case "FUEL":
                case "REFILL":
                case "FILTER":
                case "MAP":
                case "KEY":
                case "VEHICLE_LOCKPICK_TOOL":
                case "VEHICLE_PAINT_TOOL":
                case "VEHICLE_REPAIR_TOOL":
                    return "tools";
                case "SHIRT":
                case "PANTS":
                case "VEST":
                case "HAT":
                case "BACKPACK":
                case "MASK":
                case "GLASSES":
                    return "clothing";
                case "OPTIC":
                case "SIGHT":
                case "GRIP":
                case "TACTICAL":
                case "BARREL":
                    return "attachments";
                case "BARRICADE":
                case "STRUCTURE":
                case "STORAGE":
                case "TRAP":
                case "SENTRY":
                case "GENERATOR":
                case "BEACON":
                case "FARM":
                case "GROWER":
                case "OIL_PUMP":
                    return "building";
                case "TIRE":
                case "TANK":
                    return "vehicles";
                default:
                    return "other";
            }
        }

        public static int RarityRankFor(string? rarity)
        {
            switch ((rarity ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "UNCOMMON": return 1;
                case "RARE": return 2;
                case "EPIC": return 3;
                case "LEGENDARY": return 4;
                case "MYTHICAL": return 5;
                default: return 0;
            }
        }

        internal static TCandidate? SelectAuthoritativeCandidate<TCandidate, TAsset>(
            IEnumerable<TCandidate> candidates, TAsset authoritativeAsset, Func<TCandidate, TAsset> assetSelector)
            where TCandidate : class
            where TAsset : class
            => candidates.FirstOrDefault(candidate => ReferenceEquals(assetSelector(candidate), authoritativeAsset));

        private static LocalizedItemInfo Resolve(IItemAsset asset)
        {
            var fallback = (asset.ItemName ?? string.Empty).Trim();
            if (!(asset is UnturnedItemAsset unturnedAsset))
            {
                return new LocalizedItemInfo(fallback, null, false);
            }

            return Resolve(unturnedAsset.ItemAsset, fallback);
        }

        private static LocalizedItemInfo Resolve(ItemAsset gameAsset, string fallback)
        {
            var itemType = gameAsset.type.ToString();
            var rarity = gameAsset.rarity.ToString();
            var rarityRank = RarityRankFor(rarity);
            var category = CategoryForType(itemType);
            var originPath = gameAsset.absoluteOriginFilePath;
            if (string.IsNullOrWhiteSpace(originPath))
            {
                return new LocalizedItemInfo(fallback, null, gameAsset.showQuality,
                    itemType, rarity, rarityRank, category);
            }

            var names = s_NameCache.GetOrAdd(originPath,
                path => ResolveFromAssetFolder(path, fallback));
            return new LocalizedItemInfo(names.English, names.Chinese, gameAsset.showQuality,
                itemType, rarity, rarityRank, category);
        }

        private static LocalizedNames ResolveFromAssetFolder(string originPath, string fallback)
        {
            try
            {
                var directory = Path.GetDirectoryName(originPath);
                if (string.IsNullOrEmpty(directory))
                {
                    return new LocalizedNames(fallback, null);
                }

                var english = ReadName(Path.Combine(directory, "English.dat")) ?? fallback;
                string? chinese = null;
                foreach (var fileName in s_ChineseFileNames)
                {
                    chinese = ReadName(Path.Combine(directory, fileName));
                    if (!string.IsNullOrWhiteSpace(chinese))
                    {
                        break;
                    }
                }

                return new LocalizedNames(english, chinese);
            }
            catch (IOException)
            {
                return new LocalizedNames(fallback, null);
            }
            catch (UnauthorizedAccessException)
            {
                return new LocalizedNames(fallback, null);
            }
            catch (ArgumentException)
            {
                return new LocalizedNames(fallback, null);
            }
        }

        private static string? ReadName(string path)
            => File.Exists(path) ? ParseName(File.ReadAllText(path)) : null;

        public static string? ParseName(string contents)
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
                        var parsed = new System.Text.StringBuilder();
                        for (var i = 1; i < value.Length; i++)
                        {
                            var c = value[i];
                            if (escaped)
                            {
                                parsed.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c == 't' ? '\t' : c);
                                escaped = false;
                            }
                            else if (c == '\\')
                            {
                                escaped = true;
                            }
                            else if (c == '"')
                            {
                                return parsed.ToString().Trim();
                            }
                            else
                            {
                                parsed.Append(c);
                            }
                        }

                        return parsed.ToString().Trim();
                    }

                    var comment = value.IndexOf(" //", StringComparison.Ordinal);
                    return (comment >= 0 ? value.Substring(0, comment) : value).Trim();
                }
            }

            return null;
        }

        private readonly struct LocalizedNames
        {
            public LocalizedNames(string english, string? chinese)
            {
                English = english?.Trim() ?? string.Empty;
                Chinese = chinese?.Trim() ?? string.Empty;
            }

            public string English { get; }
            public string Chinese { get; }
        }
    }
}
