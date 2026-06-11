using System;
using System.Collections.Generic;
using System.Globalization;
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
                label: "商品",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(Save(store, request)),
                fields: new[]
                {
                    new WebField("id", "商品ID", WebFieldType.Text, required: true, placeholder: "/buy /sell 用的唯一ID"),
                    new WebField("name", "显示名", WebFieldType.Text, required: true),
                    new WebField("items", "物品", WebFieldType.Text, required: true,
                        placeholder: "物品ID×数量，逗号分隔。单个=物品，多个=礼包。如 15x2 或 15x2, 81x1"),
                    new WebField("buyPrice", "买价", WebFieldType.Number, required: false, placeholder: "0=不可买"),
                    new WebField("sellPrice", "卖价", WebFieldType.Number, required: false, placeholder: "0=不可卖")
                },
                description: "点商品编辑，「新增」添加。「物品」填一条=普通物品，多条（逗号分隔）=礼包；"
                    + "格式 物品ID×数量（只写ID则数量1），如 15x2 或 15x2, 81x1。物品ID 可用下方检索。",
                recordsLoader: () => LoadItemRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(Remove(store, request)),
                keyField: "id");

            var search = new WebPanelAction(
                id: "search",
                label: "检索游戏物品",
                kind: WebActionKind.Search,
                handler: request => SearchAsync(itemDirectory, request),
                fields: new[]
                {
                    new WebField("query", "物品名或ID", WebFieldType.Text, placeholder: "输入关键词或数字ID…")
                },
                description: "在全部游戏物品中按名称或 ID 模糊检索，拿到「物品ID」填到上面的表单。");

            var discount = new WebPanelAction(
                id: "discount",
                label: "VIP 折扣",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveDiscount(store, request)),
                fields: new[]
                {
                    new WebField("enabled", "折扣总开关", WebFieldType.Select, options: new[] { "开", "关" }),
                    new WebField("tiers", "档位", WebFieldType.Text,
                        placeholder: "权限=倍率，逗号分隔：如 well404.shop.vip=0.9, well404.shop.mvp=0.8")
                },
                description: "折扣作用于买价；玩家取其拥有权限中最低(最优)的倍率。"
                    + "「档位」格式 权限=倍率（0<倍率≤1，逗号分隔）；清空即取消所有档位。",
                loader: () => Task.FromResult(store.ReadDiscounts(d =>
                {
                    var parts = new List<string>();
                    foreach (var tier in d.Tiers)
                    {
                        parts.Add(tier.Key + "=" + Num(tier.Value));
                    }

                    return (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    {
                        ["enabled"] = d.Enabled ? "开" : "关",
                        ["tiers"] = string.Join(", ", parts)
                    };
                })));

            return new WebPanelModule(
                ModuleId, "商店 / 商品",
                new[] { items, search, discount },
                icon: "🛒");
        }

        private static WebActionResult SaveDiscount(ShopConfigStore store, WebActionRequest request)
        {
            var enabledRaw = request.Get("enabled");
            bool? enabled = enabledRaw == "开" ? true : enabledRaw == "关" ? (bool?)false : null;

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

            return WebActionResult.Ok("已保存折扣设置。");
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
                    error = $"档位格式错误：{token}（应为 权限=倍率）";
                    return null;
                }

                var perm = parts[0].Trim();
                if (perm.Length == 0
                    || !decimal.TryParse(parts[1].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var mult)
                    || mult <= 0m || mult > 1m)
                {
                    error = $"档位倍率无效：{token}（0<倍率≤1）";
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
                return WebActionResult.Fail("请填写商品ID、显示名与物品。");
            }

            var parsed = ParseItems(itemsRaw, out var error);
            if (parsed == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (parsed.Count == 0)
            {
                return WebActionResult.Fail("「物品」不能为空，格式如 15x2 或 15x2, 81x1。");
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
                summary = $"物品 {entry.ItemId}×{entry.Amount}";
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
                summary = $"礼包 {parsed.Count} 种物品";
            }

            store.Upsert(entry);
            return WebActionResult.Ok(
                $"已保存 {entry.Id}（{entry.Name}）：{summary}，买 {Num(buyPrice)} / 卖 {Num(sellPrice)}。");
        }

        private static WebActionResult Remove(ShopConfigStore store, WebActionRequest request)
        {
            // The collection delete endpoint sends the record key as "key".
            var id = request.Get("key");
            if (id == null)
            {
                return WebActionResult.Fail("缺少商品 ID。");
            }

            return store.Remove(id)
                ? WebActionResult.Ok($"已删除商品 {id}。")
                : WebActionResult.Fail($"未找到商品 {id}。");
        }

        private static async Task<WebActionResult> SearchAsync(IItemDirectory itemDirectory, WebActionRequest request)
        {
            var query = request.Get("query");
            if (query == null)
            {
                return WebActionResult.Table(new[] { "物品ID", "名称" }, new List<IReadOnlyList<string>>(), "输入物品名或 ID 检索。");
            }

            await UniTask.SwitchToMainThread();
            var assets = await itemDirectory.GetItemAssetsAsync();

            var rows = new List<IReadOnlyList<string>>();
            var truncated = false;
            foreach (var asset in assets)
            {
                var assetId = asset.ItemAssetId ?? string.Empty;
                var itemName = asset.ItemName ?? string.Empty;
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
                ? "没有匹配的物品。"
                : (truncated ? $"结果过多，仅显示前 {SearchLimit} 条，请细化关键词。" : null);
            return WebActionResult.Table(new[] { "物品ID", "名称" }, rows, message);
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
                    error = $"物品ID 无效：{token}";
                    return null;
                }

                if (!int.TryParse(amountPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1)
                {
                    error = $"数量无效：{token}（格式 物品ID×数量，如 15x2）";
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
