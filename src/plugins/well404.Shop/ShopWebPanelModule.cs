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
    /// Builds the shop's <see cref="WebPanelModule"/>. Plain items and bundles are managed in two
    /// separate collections so each stays as simple as it can be: a plain item is just an item id
    /// plus a price pair (its name comes from the game); a bundle adds an id, name and contents. A
    /// game-item search lets the admin look up ids and one-click "quick add" any item as a plain
    /// item. Catalog edits are written back to <c>config.yaml</c> via <see cref="ShopConfigStore"/>.
    /// </summary>
    internal static class ShopWebPanelModule
    {
        public const string ModuleId = "well404.shop";

        private const int SearchLimit = 100;
        private const string QuickAddActionId = "additem";

        public static WebPanelModule Create(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var items = new WebPanelAction(
                id: "items",
                label: "Plain items",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveItem(store, request)),
                fields: new[]
                {
                    new WebField("itemId", "Item ID", WebFieldType.Number, required: true),
                    new WebField("buyPrice", "Buy price", WebFieldType.Number, required: false, placeholder: "0 = not buyable"),
                    new WebField("sellPrice", "Sell price", WebFieldType.Number, required: false, placeholder: "0 = not sellable")
                },
                description: "Click to edit, Add to create. A plain item is bought and sold by its game item id; its display name comes from the game. Look up ids with the search below.",
                recordsLoader: () => LoadItemRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(RemoveItem(store, request)),
                keyField: "itemId",
                summaryFields: new[] { "buyPrice", "sellPrice" });

            var bundles = new WebPanelAction(
                id: "bundles",
                label: "Bundles",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveBundle(store, request)),
                fields: new[]
                {
                    new WebField("id", "Bundle ID", WebFieldType.Text, required: true, placeholder: "Unique id used by /buy /sell"),
                    new WebField("name", "Display name", WebFieldType.Text, required: true),
                    new WebField("contents", "Contents", WebFieldType.Text, required: true,
                        placeholder: "itemId×amount, comma-separated, e.g. 15x2, 81x1"),
                    new WebField("buyPrice", "Buy price", WebFieldType.Number, required: false, placeholder: "0 = not buyable"),
                    new WebField("sellPrice", "Sell price", WebFieldType.Number, required: false, placeholder: "0 = not sellable")
                },
                description: "Click a bundle to edit, Add to create. A bundle is a named pack of items; contents format itemId×amount, comma-separated (id only = amount 1), e.g. 15x2, 81x1.",
                recordsLoader: () => LoadBundleRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(RemoveBundle(store, request)),
                keyField: "id",
                summaryFields: new[] { "buyPrice", "sellPrice" });

            var search = new WebPanelAction(
                id: "search",
                label: "Search game items",
                kind: WebActionKind.Search,
                handler: request => SearchAsync(itemDirectory, request),
                fields: new[]
                {
                    new WebField("query", "Item name or ID", WebFieldType.Text, placeholder: "Type a keyword or numeric ID…")
                },
                description: "Search any game item by name or ID, then click + to add it to the shop as a plain item (set its prices afterwards).");

            // Invoke-only target of the search's per-row "+" button: quick-adds the clicked item id
            // as a draft plain item (zero prices, ready to edit). Hidden so it has no card of its own.
            var quickAdd = new WebPanelAction(
                id: QuickAddActionId,
                label: "Add to shop",
                kind: WebActionKind.Form,
                handler: request => Task.FromResult(QuickAddItem(store, request)),
                hidden: true);

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
                description: "Discounts apply to buy prices; a player gets the lowest (best) multiplier among their granted permissions. Tiers format: permission=multiplier (0<m≤1, comma-separated); empty clears all tiers.",
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
                ModuleId, "Shop",
                new[] { items, bundles, search, quickAdd, discount },
                icon: "🛒");
        }

        // ----- plain items -----

        private static async Task<IReadOnlyList<WebRecord>> LoadItemRecordsAsync(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await ShopNames.BuildMapAsync(itemDirectory);
            var records = new List<WebRecord>();
            foreach (var item in store.Items)
            {
                var id = item.ItemId.ToString(CultureInfo.InvariantCulture);
                records.Add(new WebRecord(
                    id,
                    ShopNames.NameOf(item.ItemId, names),
                    new Dictionary<string, string>
                    {
                        ["itemId"] = id,
                        ["buyPrice"] = Num(item.BuyPrice),
                        ["sellPrice"] = Num(item.SellPrice)
                    },
                    new[] { "#" + id }));
            }

            return records;
        }

        private static WebActionResult SaveItem(ShopConfigStore store, WebActionRequest request)
        {
            var idRaw = request.Get("itemId");
            if (idRaw == null
                || !ushort.TryParse(idRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0)
            {
                return WebActionResult.Fail("Enter a valid item ID.");
            }

            store.UpsertItem(new ShopItemConfig
            {
                ItemId = itemId,
                BuyPrice = request.GetDecimal("buyPrice") ?? 0m,
                SellPrice = request.GetDecimal("sellPrice") ?? 0m
            });
            return WebActionResult.Ok("Saved.");
        }

        private static WebActionResult RemoveItem(ShopConfigStore store, WebActionRequest request)
        {
            var key = request.Get("key");
            if (key == null)
            {
                return WebActionResult.Fail("Enter a valid item ID.");
            }

            return store.RemoveItem(key) ? WebActionResult.Ok("Deleted.") : WebActionResult.Fail("Not found.");
        }

        private static WebActionResult QuickAddItem(ShopConfigStore store, WebActionRequest request)
        {
            var key = request.Get("key");
            if (key == null
                || !ushort.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0)
            {
                return WebActionResult.Fail("Enter a valid item ID.");
            }

            if (store.Items.Any(x => x.ItemId == itemId))
            {
                return WebActionResult.Fail("Already in the shop.");
            }

            store.UpsertItem(new ShopItemConfig
            {
                ItemId = itemId,
                BuyPrice = request.GetDecimal("buyPrice") ?? 0m,
                SellPrice = request.GetDecimal("sellPrice") ?? 0m
            });
            return WebActionResult.Ok("Added to the shop.");
        }

        // ----- bundles -----

        private static async Task<IReadOnlyList<WebRecord>> LoadBundleRecordsAsync(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await ShopNames.BuildMapAsync(itemDirectory);
            var records = new List<WebRecord>();
            foreach (var bundle in store.Bundles)
            {
                var rawParts = new List<string>();
                var prettyParts = new List<string>();
                foreach (var content in bundle.Contents)
                {
                    rawParts.Add(Raw(content.ItemId, content.Amount));
                    prettyParts.Add(ShopNames.Label(content.ItemId, content.Amount, names));
                }

                records.Add(new WebRecord(
                    bundle.Id,
                    bundle.Name,
                    new Dictionary<string, string>
                    {
                        ["id"] = bundle.Id,
                        ["name"] = bundle.Name,
                        ["contents"] = string.Join(", ", rawParts),
                        ["buyPrice"] = Num(bundle.BuyPrice),
                        ["sellPrice"] = Num(bundle.SellPrice)
                    },
                    prettyParts));
            }

            return records;
        }

        private static WebActionResult SaveBundle(ShopConfigStore store, WebActionRequest request)
        {
            var id = request.Get("id");
            var name = request.Get("name");
            var contentsRaw = request.Get("contents");
            if (id == null || name == null || contentsRaw == null)
            {
                return WebActionResult.Fail("Enter the bundle ID, name and contents.");
            }

            var parsed = ParseItems(contentsRaw, out var error);
            if (parsed == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (parsed.Count == 0)
            {
                return WebActionResult.Fail("Contents cannot be empty, e.g. 15x2, 81x1.");
            }

            store.UpsertBundle(new ShopBundleConfig
            {
                Id = id,
                Name = name,
                Contents = parsed,
                BuyPrice = request.GetDecimal("buyPrice") ?? 0m,
                SellPrice = request.GetDecimal("sellPrice") ?? 0m
            });
            return WebActionResult.Ok("Saved.");
        }

        private static WebActionResult RemoveBundle(ShopConfigStore store, WebActionRequest request)
        {
            var id = request.Get("key");
            if (id == null)
            {
                return WebActionResult.Fail("Not found.");
            }

            return store.RemoveBundle(id) ? WebActionResult.Ok("Deleted.") : WebActionResult.Fail("Not found.");
        }

        // ----- discounts -----

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
                    error = $"Invalid tier multiplier: {token} (0<m≤1)";
                    return null;
                }

                result[perm] = mult;
            }

            return result;
        }

        // ----- game item search (with per-row quick-add) -----

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
            // The "Item ID" cell (first column) is each row's key, so the quick-add button passes it.
            // The popup makes the admin set the prices up front (no silent zero-price drafts).
            return WebActionResult.Table(new[] { "Item ID", "Name" }, rows, message)
                .WithRowAction(QuickAddActionId, "Add to shop", null, new[]
                {
                    new WebField("buyPrice", "Buy price", WebFieldType.Number, required: false, placeholder: "0 = not buyable"),
                    new WebField("sellPrice", "Sell price", WebFieldType.Number, required: false, placeholder: "0 = not sellable")
                });
        }

        // ----- helpers -----

        /// <summary>The raw editable form of an item ref, e.g. <c>81x3</c>.</summary>
        private static string Raw(ushort itemId, int amount)
            => itemId.ToString(CultureInfo.InvariantCulture) + "x" + amount.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses a "contents" string into item/amount pairs. Entries are comma/semicolon/newline
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
                    error = $"Invalid amount: {token} (format itemId×amount, e.g. 15x2)";
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
