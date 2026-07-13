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
    /// Builds the shop's <see cref="WebPanelModule"/>. Each catalog record is one game item with
    /// prices, group, note and display order. Game-item search lets the admin look up ids and add
    /// items without manually copying asset metadata. Catalog edits are persisted to
    /// <c>config.yaml</c> via <see cref="ShopConfigStore"/>.
    /// </summary>
    internal static class ShopWebPanelModule
    {
        public const string ModuleId = "well404.shop";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(ShopWebPanelModule).Assembly, "admin-ui.html", "admin-ui.css", "admin-ui.js");

        private const int SearchLimit = 100;
        private const string QuickAddActionId = "additem";

        public static WebPanelModule Create(ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var groups = new WebPanelAction(
                id: "groups",
                label: "Shop groups",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveGroup(store, request)),
                fields: new[]
                {
                    new WebField("id", "Group ID", required: true),
                    new WebField("name", "Group name", placeholder: "Empty uses the group ID")
                },
                description: "Create the second-level tabs shown to players. The default group always exists.",
                recordsLoader: () => Task.FromResult(LoadGroupRecords(store)),
                deleteHandler: request => Task.FromResult(RemoveGroup(store, request)),
                keyField: "id");

            var catalog = new WebPanelAction(
                id: "catalog",
                label: "Player shop catalog",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveCatalog(store, request)),
                fields: new[]
                {
                    new WebField("itemId", "Item ID", WebFieldType.Number, required: true),
                    new WebField("group", "Group ID", required: true, placeholder: "default"),
                    new WebField("note", "Note"),
                    new WebField("buyPrice", "Buy price", WebFieldType.Number),
                    new WebField("sellPrice", "Sell price", WebFieldType.Number)
                },
                description: "This preview uses the same groups and order as the player shop. Drag cards inside a group to reorder them.",
                loader: null,
                recordsLoader: () => LoadCatalogRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(RemoveCatalog(store, request)),
                keyField: null,
                layout: "tabs-grid",
                hidden: false,
                summaryFields: new[] { "buyPrice", "sellPrice", "note" },
                reorderHandler: request => Task.FromResult(ReorderCatalog(store, request)));

            var search = new WebPanelAction(
                id: "search",
                label: "Search game items",
                kind: WebActionKind.Search,
                handler: request => SearchAsync(itemDirectory, request),
                fields: new[]
                {
                    new WebField("query", "Item name or ID", WebFieldType.Text, placeholder: "Type a keyword or numeric ID…")
                },
                description: "Search any game item by name or ID, then click + to add it with prices, group and note.");

            // Invoke-only target of the search's per-row "+" button: quick-adds the clicked item id with popup-supplied prices, group and note. Hidden so it has no card of its own.
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
                new[] { groups, catalog, search, quickAdd, discount },
                icon: "🛒", ui: s_Ui);
        }

        private static IReadOnlyList<WebRecord> LoadGroupRecords(ShopConfigStore store)
        {
            var records = new List<WebRecord>();
            foreach (var group in store.Groups)
            {
                var name = string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name;
                records.Add(new WebRecord(group.Id, name,
                    new Dictionary<string, string>
                    {
                        ["id"] = group.Id,
                        ["name"] = name
                    }));
            }
            return records;
        }

        private static WebActionResult SaveGroup(ShopConfigStore store, WebActionRequest request)
        {
            var id = (request.Get("id") ?? string.Empty).Trim();
            if (id.Length == 0
                || id.Contains(",")
                || id.Contains(((char)10).ToString())
                || id.Contains(((char)13).ToString()))
            {
                return WebActionResult.Fail("Enter a valid group ID.");
            }
            var requestedName = request.Get("name");
            var name = string.IsNullOrWhiteSpace(requestedName) ? id : requestedName.Trim();
            store.UpsertGroup(new ShopGroupConfig { Id = id, Name = name });
            return WebActionResult.Ok("Saved.");
        }

        private static WebActionResult RemoveGroup(ShopConfigStore store, WebActionRequest request)
        {
            var key = request.Get("key");
            if (key == null || !store.RemoveGroup(key))
            {
                return WebActionResult.Fail("The default group cannot be deleted.");
            }
            return WebActionResult.Ok("Deleted.");
        }


        private static async Task<IReadOnlyList<WebRecord>> LoadCatalogRecordsAsync(
            ShopConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await ShopNames.BuildMapAsync(itemDirectory);
            var ordered = new List<KeyValuePair<int, WebRecord>>();
            var groupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in store.Groups)
            {
                var name = string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name;
                groupNames[group.Id] = name;
                ordered.Add(new KeyValuePair<int, WebRecord>(int.MinValue,
                    new WebRecord("group:" + group.Id, name,
                        new Dictionary<string, string>(),
                        null, name, group.Id, "group-header")));
            }
            foreach (var item in store.Items)
            {
                var id = item.ItemId.ToString(CultureInfo.InvariantCulture);
                ordered.Add(new KeyValuePair<int, WebRecord>(item.Order,
                    new WebRecord("item:" + id, ShopNames.NameOf(item.ItemId, names),
                        new Dictionary<string, string>
                        {
                            ["itemId"] = id,
                            ["group"] = item.Group,
                            ["note"] = item.Note,
                            ["buyPrice"] = Num(item.BuyPrice),
                            ["sellPrice"] = Num(item.SellPrice)
                        },
                        new[] { "#" + id }, GroupName(groupNames, item.Group), item.Group, null)));
            }
            ordered.Sort((left, right) => left.Key.CompareTo(right.Key));
            var records = new List<WebRecord>();
            foreach (var pair in ordered) records.Add(pair.Value);
            return records;
        }

        private static WebActionResult SaveCatalog(ShopConfigStore store, WebActionRequest request)
        {
            var requestedGroup = request.Get("group") ?? ShopConfiguration.DefaultGroupId;
            var group = store.ResolveGroupId(requestedGroup);
            if (group == null)
            {
                return WebActionResult.Fail("The selected group does not exist.");
            }
            if (!ReadPrices(request, out var buyPrice, out var sellPrice, out var priceError))
            {
                return WebActionResult.Fail(priceError!);
            }
            var oldKey = request.Get("recordKey");
            var oldOrder = store.GetCatalogOrder(oldKey);
            if (!ushort.TryParse(request.Get("itemId"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var itemId) || itemId == 0)
            {
                return WebActionResult.Fail("Enter a valid item ID.");
            }
            var newKey = "item:" + itemId.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)
                && store.Items.Any(item => item.ItemId == itemId))
            {
                return WebActionResult.Fail("That item already exists in the catalog.");
            }
            store.UpsertItem(new ShopItemConfig
            {
                ItemId = itemId,
                Group = group,
                Note = request.Get("note") ?? string.Empty,
                Order = oldOrder,
                BuyPrice = buyPrice,
                SellPrice = sellPrice
            });
            RemoveOldCatalogKey(store, oldKey, newKey);
            return WebActionResult.Ok("Saved.");
        }

        private static void RemoveOldCatalogKey(ShopConfigStore store, string? oldKey, string newKey)
        {
            if (oldKey == null || string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)) return;
            if (oldKey.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
                store.RemoveItem(oldKey.Substring(5));
        }

        private static WebActionResult RemoveCatalog(ShopConfigStore store, WebActionRequest request)
        {
            var key = request.Get("key") ?? string.Empty;
            var removed = key.StartsWith("item:", StringComparison.OrdinalIgnoreCase)
                && store.RemoveItem(key.Substring(5));
            return removed ? WebActionResult.Ok("Deleted.") : WebActionResult.Fail("Not found.");
        }

        private static WebActionResult ReorderCatalog(ShopConfigStore store, WebActionRequest request)
        {
            var group = request.Get("group") ?? ShopConfiguration.DefaultGroupId;
            var keys = SplitKeys(request.Get("keys"));
            return store.ReorderCatalog(group, keys)
                ? WebActionResult.Ok("Order saved.")
                : WebActionResult.Fail("The catalog order is stale; refresh and try again.");
        }

        private static IReadOnlyList<string> SplitKeys(string? raw)
            => string.IsNullOrEmpty(raw)
                ? (IReadOnlyList<string>)Array.Empty<string>()
                : raw.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

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

            var requestedGroup = request.Get("group") ?? ShopConfiguration.DefaultGroupId;
            var group = store.ResolveGroupId(requestedGroup);
            if (group == null)
            {
                return WebActionResult.Fail("The selected group does not exist.");
            }
            if (!ReadPrices(request, out var buyPrice, out var sellPrice, out var priceError))
            {
                return WebActionResult.Fail(priceError!);
            }

            store.UpsertItem(new ShopItemConfig
            {
                ItemId = itemId,
                Group = group,
                Note = request.Get("note") ?? string.Empty,
                BuyPrice = buyPrice,
                SellPrice = sellPrice
            });
            return WebActionResult.Ok("Added to the shop.");
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
            var names = await ShopNames.BuildMapAsync(itemDirectory);

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
                        var exactId = asset.ItemAssetId ?? string.Empty;
                        rows.Add(new[] { exactId,
                            names.TryGetValue(exactId, out var exactName) ? exactName : asset.ItemName ?? string.Empty });
                        seen.Add(trimmed);
                        break;
                    }
                }
            }

            foreach (var asset in assets)
            {
                var assetId = asset.ItemAssetId ?? string.Empty;
                var itemName = names.TryGetValue(assetId, out var resolvedName)
                    ? resolvedName : asset.ItemName ?? string.Empty;
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
                    new WebField("sellPrice", "Sell price", WebFieldType.Number, required: false, placeholder: "0 = not sellable"),
                    new WebField("group", "Group ID", placeholder: "default"),
                    new WebField("note", "Note")
                });
        }

        // ----- helpers -----

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static string GroupName(
            IReadOnlyDictionary<string, string> names, string group)
            => names.TryGetValue(group, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name : group;

        private static bool ReadPrices(
            WebActionRequest request, out decimal buyPrice, out decimal sellPrice, out string? error)
        {
            error = null;
            if (!ReadNonNegativeDecimal(request.Get("buyPrice"), out buyPrice))
            {
                sellPrice = 0m;
                error = "Buy price must be a non-negative number.";
                return false;
            }
            if (!ReadNonNegativeDecimal(request.Get("sellPrice"), out sellPrice))
            {
                error = "Sell price must be a non-negative number.";
                return false;
            }
            return true;
        }

        private static bool ReadNonNegativeDecimal(string? raw, out decimal value)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                value = 0m;
                return true;
            }
            return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
                && value >= 0m;
        }

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
