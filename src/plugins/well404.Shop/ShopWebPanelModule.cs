using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.WebPanel;

namespace well404.Shop
{
    /// <summary>
    /// Builds the shop's <see cref="WebPanelModule"/>: list the catalog, add/edit an entry,
    /// remove an entry, and search the game's item assets (id + name) to fill in an item id.
    /// <para>
    /// A single entry form handles both kinds: enter one <c>itemId×amount</c> and it saves a
    /// plain <c>item</c>; enter several (comma-separated) and it saves a <c>bundle</c> whose
    /// contents are those items. Catalog edits are written back to <c>config.yaml</c> via
    /// <see cref="ShopConfigStore"/>. The search runs on the main thread.
    /// </para>
    /// </summary>
    internal static class ShopWebPanelModule
    {
        public const string ModuleId = "well404.shop";

        private const int SearchLimit = 100;

        public static WebPanelModule Create(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var items = new WebPanelAction(
                id: "items",
                label: "Items",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(Save(store, request)),
                fields: new[]
                {
                    new WebField("id", "Shop ID", WebFieldType.Text, required: true, placeholder: "Unique ID used by /buy /sell"),
                    new WebField("name", "Display name", WebFieldType.Text, required: true),
                    new WebField("items", "Contents", WebFieldType.Text, required: true,
                        placeholder: "itemId\u00d7amount, comma-separated. One = item, several = bundle. e.g. 15x2 or 15x2, 81x1"),
                    new WebField("buyPrice", "Buy price", WebFieldType.Number, required: false, placeholder: "0 = not buyable"),
                    new WebField("sellPrice", "Sell price", WebFieldType.Number, required: false, placeholder: "0 = not sellable")
                },
                description: "Click an item to edit, Add to create. \u00abItems\u00bb: one entry = plain item, several (comma-separated) = bundle; format itemId\u00d7amount (id only = amount 1), e.g. 15x2 or 15x2, 81x1. Look up item IDs with the search below.",
                recordsLoader: () => LoadItemRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(Remove(store, request)),
                keyField: "id");

            var search = new WebPanelAction(
                id: "search",
                label: "Search game items",
                kind: WebActionKind.Search,
                handler: request => SearchAsync(itemDirectory, request),
                fields: new[]
                {
                    new WebField("query", "Item name or ID", WebFieldType.Text, placeholder: "Type a keyword or numeric ID\u2026")
                },
                description: "Fuzzy-search all game items by name or ID; take the item ID into the form above.");

            var discount = new WebPanelAction(
                id: "discount",
                label: "VIP discount",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveDiscount(store, request)),
                fields: new[]
                {
                    new WebField("enabled", "Discount master switch", WebFieldType.Boolean),
                    new WebField("tiers", "Tiers", WebFieldType.Text,
                        placeholder: "permission=multiplier, comma-separated: e.g. well404.shop.vip=0.9, well404.shop.mvp=0.8")
                },
                description: "Discounts apply to buy prices; a player gets the lowest (best) multiplier among their granted permissions. Tiers format: permission=multiplier (0<m\u22641, comma-separated); empty clears all tiers.",
                loader: () => Task.FromResult(store.ReadDiscounts(d =>
                {
                    var parts = new List<string>();
                    foreach (var tier in d.Tiers)
                    {
                        parts.Add(tier.Key + "=" + Num(tier.Value));
                    }

                    return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    {
                        ["enabled"] = d.Enabled ? "true" : "false",
                        ["tiers"] = string.Join(", ", parts)
                    };
                })));

            return new WebPanelModule(
                ModuleId, "Shop / Items",
                new[] { items, search, discount },
                icon: "🛒");
        }

        private static WebActionResult SaveDiscount(ShopConfigStore store, WebActionRequest request)
        {
            var enabledRaw = request.Get("enabled");
            bool? enabled = enabledRaw == "true" ? true : enabledRaw == "false" ? (bool?)false : null;

            // Settings form is pre-filled, so the submitted "tiers" value is authoritative:
            // parse it as the full set (empty = clear all tiers).
            var tiers = ParseTiers(request.Get("tiers") ?? string.Empty, out var error);
            if (tiers == null)
            {
                return WebActionResult.Fail(error!);
            }

            store.UpdateDiscounts(d =>
            {
                if (enabled != null)
                {
                    d.Enabled = enabled.Value;
                }

                d.Tiers = tiers;
            });

            return WebActionResult.Ok("Saved discount settings.");
        }

        /// <summary>Parses "perm=mult, perm:mult" tier text into a map. Returns null + error on a bad entry.</summary>
        private static Dictionary<string, decimal>? ParseTiers(string raw, out string? error)
        {
            error = null;
            var result = new Dictionary<string, decimal>();
            raw = NormalizeFullWidth(raw);
            var tokens = raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tokenRaw in tokens)
            {
                var token = tokenRaw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split(new[] { '=', ':' }, 2);
                if (parts.Length != 2)
                {
                    error = $"Bad tier format: {token} (expected permission=multiplier)";
                    return null;
                }

                var perm = parts[0].Trim();
                if (perm.Length == 0
                    || !decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var mult)
                    || mult <= 0m || mult > 1m)
                {
                    error = $"Invalid tier multiplier: {token} (0<m\u22641)";
                    return null;
                }

                result[perm] = mult;
            }

            return result;
        }

        private static async Task<IReadOnlyList<WebRecord>> LoadItemRecordsAsync(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await BuildNameMapAsync(itemDirectory);

            var records = new List<WebRecord>();
            foreach (var entry in store.Items)
            {
                var rawParts = new List<string>();
                var prettyParts = new List<string>();
                if (entry.IsBundle)
                {
                    foreach (var content in entry.Contents)
                    {
                        rawParts.Add(Raw(content.ItemId, content.Amount));
                        prettyParts.Add(FormatItem(content.ItemId, content.Amount, names));
                    }
                }
                else
                {
                    rawParts.Add(Raw(entry.ItemId, entry.Amount));
                    prettyParts.Add(FormatItem(entry.ItemId, entry.Amount, names));
                }

                records.Add(new WebRecord(
                    entry.Id,
                    entry.Name,
                    new Dictionary<string, string>
                    {
                        ["id"] = entry.Id,
                        ["name"] = entry.Name,
                        ["items"] = string.Join(", ", rawParts),
                        ["buyPrice"] = Num(entry.BuyPrice),
                        ["sellPrice"] = Num(entry.SellPrice)
                    },
                    prettyParts));
            }

            return records;
        }

        /// <summary>The raw editable form of an item ref, e.g. <c>81x3</c>.</summary>
        private static string Raw(ushort itemId, int amount)
            => itemId.ToString(CultureInfo.InvariantCulture) + "x" + amount.ToString(CultureInfo.InvariantCulture);

        /// <summary>Formats one item ref for display as <c>Name(id)*amount</c> (or <c>id*amount</c> if name unknown).</summary>
        private static string FormatItem(ushort itemId, int amount, IReadOnlyDictionary<string, string> names)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            var qty = amount.ToString(CultureInfo.InvariantCulture);
            return names.TryGetValue(id, out var name) && name.Length > 0
                ? $"{name}({id})*{qty}"
                : $"{id}*{qty}";
        }

        /// <summary>Builds a game item-id → name lookup from the item directory (main thread).</summary>
        private static async Task<IReadOnlyDictionary<string, string>> BuildNameMapAsync(IItemDirectory itemDirectory)
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

        private static WebActionResult Save(ShopConfigStore store, WebActionRequest request)
        {
            var id = request.Get("id");
            var name = request.Get("name");
            var itemsRaw = request.Get("items");
            if (id == null || name == null || itemsRaw == null)
            {
                return WebActionResult.Fail("Enter the shop ID, display name and items.");
            }

            var parsed = ParseItems(itemsRaw, out var error);
            if (parsed == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (parsed.Count == 0)
            {
                return WebActionResult.Fail("Items cannot be empty, e.g. 15x2 or 15x2, 81x1.");
            }

            var buyPrice = request.GetDecimal("buyPrice") ?? 0m;
            var sellPrice = request.GetDecimal("sellPrice") ?? 0m;

            ShopEntry entry;
            string summary;
            if (parsed.Count == 1)
            {
                // A single item -> a plain "item" entry.
                entry = new ShopEntry
                {
                    Id = id,
                    Name = name,
                    Type = "item",
                    ItemId = parsed[0].ItemId,
                    Amount = parsed[0].Amount,
                    BuyPrice = buyPrice,
                    SellPrice = sellPrice
                };
                summary = $"item {entry.ItemId}\u00d7{entry.Amount}";
            }
            else
            {
                // Multiple items -> a "bundle" entry.
                entry = new ShopEntry
                {
                    Id = id,
                    Name = name,
                    Type = "bundle",
                    Contents = parsed,
                    BuyPrice = buyPrice,
                    SellPrice = sellPrice
                };
                summary = $"bundle of {parsed.Count} item(s)";
            }

            store.Upsert(entry);
            return WebActionResult.Ok(
                $"Saved {entry.Id} ({entry.Name}): {summary}, buy {Num(buyPrice)} / sell {Num(sellPrice)}.");
        }

        private static WebActionResult Remove(ShopConfigStore store, WebActionRequest request)
        {
            // The collection delete endpoint sends the record key as "key".
            var id = request.Get("key");
            if (id == null)
            {
                return WebActionResult.Fail("Missing shop ID.");
            }

            return store.Remove(id)
                ? WebActionResult.Ok($"Deleted item {id}.")
                : WebActionResult.Fail($"Item not found: {id}.");
        }

        private static async Task<WebActionResult> SearchAsync(IItemDirectory itemDirectory, WebActionRequest request)
        {
            var query = request.Get("query");
            if (query == null)
            {
                return WebActionResult.Table(new[] { "Item ID", "Name" }, new List<IReadOnlyList<string>>(), "Type an item name or ID to search.");
            }

            await UniTask.SwitchToMainThread();
            var assets = await itemDirectory.GetItemAssetsAsync();

            var rows = new List<IReadOnlyList<string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var truncated = false;

            // A pure-number query: surface the item whose ID is exactly that number first, so a
            // numeric lookup always shows the matching item even when many IDs contain those digits.
            var trimmed = query.Trim();
            if (trimmed.Length > 0 && trimmed.All(char.IsDigit))
            {
                foreach (var asset in assets)
                {
                    if (string.Equals(asset.ItemAssetId, trimmed, StringComparison.Ordinal))
                    {
                        rows.Add(new[] { asset.ItemAssetId ?? string.Empty, asset.ItemName ?? string.Empty });
                        seen.Add(trimmed);
                        break;
                    }
                }
            }

            foreach (var asset in assets)
            {
                var assetId = asset.ItemAssetId ?? string.Empty;
                var itemName = asset.ItemName ?? string.Empty;
                if (seen.Contains(assetId))
                {
                    continue;
                }

                var match = assetId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || itemName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match)
                {
                    continue;
                }

                if (rows.Count >= SearchLimit)
                {
                    truncated = true;
                    break;
                }

                rows.Add(new[] { assetId, itemName });
            }

            var message = rows.Count == 0
                ? "No matching items."
                : (truncated ? $"Too many results; showing the first {SearchLimit}. Refine your keyword." : null);
            return WebActionResult.Table(new[] { "Item ID", "Name" }, rows, message);
        }

        /// <summary>
        /// Parses an "items" string into item/amount pairs. Entries are comma/semicolon/newline
        /// separated; each is <c>itemId</c> or <c>itemId×amount</c> (× may be <c>x</c>, <c>X</c>,
        /// <c>×</c> or <c>*</c>; amount defaults to 1). Returns null with <paramref name="error"/>
        /// set on a malformed entry.
        /// </summary>
        private static List<BundleItem>? ParseItems(string raw, out string? error)
        {
            error = null;
            // Players often type full-width punctuation/digits (，；ｘ＊０-９) on a Chinese IME;
            // fold them to ASCII so the separators and numbers parse.
            raw = NormalizeFullWidth(raw);
            var result = new List<BundleItem>();
            var tokens = raw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tokenRaw in tokens)
            {
                var token = tokenRaw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                var parts = token.Split(new[] { 'x', 'X', '×', '*' }, 2);
                var idPart = parts[0].Trim();
                var amountPart = parts.Length > 1 ? parts[1].Trim() : "1";

                if (!ushort.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId) || itemId == 0)
                {
                    error = $"Invalid item ID: {token}";
                    return null;
                }

                if (!int.TryParse(amountPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1)
                {
                    error = $"Invalid amount: {token} (format itemId\u00d7amount, e.g. 15x2)";
                    return null;
                }

                result.Add(new BundleItem { ItemId = itemId, Amount = amount });
            }

            return result;
        }

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Folds full-width punctuation/digits (U+FF01–U+FF5E and the ideographic space) to ASCII.</summary>
        private static string NormalizeFullWidth(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == '　')
                {
                    sb.Append(' ');
                }
                else if (c >= '！' && c <= '～')
                {
                    sb.Append((char)(c - 0xFEE0));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
