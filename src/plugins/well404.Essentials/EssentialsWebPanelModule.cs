using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.WebPanel;

namespace well404.Essentials
{
    /// <summary>
    /// Builds Essentials' <see cref="WebPanelModule"/>: settings groups for the teleport rules,
    /// the tpa/sleep/back knobs, and CRUD collections for warps and gift packs (plus an item
    /// search to fill in gift contents). Everything is written back to <c>config.yaml</c> via the
    /// shared <see cref="EssentialsConfigStore"/>.
    /// </summary>
    internal static class EssentialsWebPanelModule
    {
        public const string ModuleId = "well404.essentials";

        private const int SearchLimit = 100;

        public static WebPanelModule Create(EssentialsConfigStore store, IItemDirectory itemDirectory)
        {
            var teleport = new WebPanelAction(
                id: "teleport",
                label: "传送设置",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveTeleport(store, request)),
                fields: new[]
                {
                    new WebField("warmupSeconds", "预热秒数", WebFieldType.Number, placeholder: "传送前需静止的秒数，0=瞬移"),
                    new WebField("cancelOnMove", "移动取消", WebFieldType.Select, options: new[] { "开", "关" }),
                    new WebField("moveThreshold", "移动阈值(米)", WebFieldType.Number),
                    new WebField("cooldownSeconds", "冷却秒数", WebFieldType.Number, placeholder: "成功传送后的冷却，0=无"),
                    new WebField("costHome", "home 费用", WebFieldType.Number),
                    new WebField("costTp", "tp 费用", WebFieldType.Number),
                    new WebField("costWarp", "warp 费用", WebFieldType.Number),
                    new WebField("costBack", "back 费用", WebFieldType.Number)
                },
                description: "所有传送(home/tp/warp/back)共用的规则。费用需安装经济插件(如 well404.Economy)才会扣费，默认 0=免费。",
                loader: () => Task.FromResult(store.Read(s => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["warmupSeconds"] = Int(s.Teleport.WarmupSeconds),
                    ["cancelOnMove"] = s.Teleport.CancelOnMove ? "开" : "关",
                    ["moveThreshold"] = Num(s.Teleport.MoveThreshold),
                    ["cooldownSeconds"] = Int(s.Teleport.CooldownSeconds),
                    ["costHome"] = Num(s.Teleport.Costs.Home),
                    ["costTp"] = Num(s.Teleport.Costs.Tp),
                    ["costWarp"] = Num(s.Teleport.Costs.Warp),
                    ["costBack"] = Num(s.Teleport.Costs.Back)
                })));

            var rules = new WebPanelAction(
                id: "rules",
                label: "tpa / sleep / back",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveRules(store, request)),
                fields: new[]
                {
                    new WebField("tpaExpiration", "tpa 请求有效期(秒)", WebFieldType.Number),
                    new WebField("partyInviteExpiration", "party 邀请有效期(秒)", WebFieldType.Number),
                    new WebField("partyMaxMembers", "party 人数上限", WebFieldType.Number, placeholder: "0=不额外限制"),
                    new WebField("sleepEnabled", "sleep 投票", WebFieldType.Select, options: new[] { "开", "关" }),
                    new WebField("sleepRatio", "sleep 通过比例", WebFieldType.Number, placeholder: "0.5=半数"),
                    new WebField("backInvincibility", "back 无敵秒数", WebFieldType.Number)
                },
                description: "tpa/party 请求超时、party 人数上限、sleep 投票开关与通过比例(在线玩家的比例)、/back 落地后的无敌秒数(0=无)。",
                loader: () => Task.FromResult(store.Read(s => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["tpaExpiration"] = Int(s.Tpa.ExpirationSeconds),
                    ["partyInviteExpiration"] = Int(s.Party.InviteExpirationSeconds),
                    ["partyMaxMembers"] = Int(s.Party.MaxMembers),
                    ["sleepEnabled"] = s.Sleep.Enabled ? "开" : "关",
                    ["sleepRatio"] = Num(s.Sleep.RequiredRatio),
                    ["backInvincibility"] = Int(s.Back.InvincibilitySeconds)
                })));

            var warps = new WebPanelAction(
                id: "warps",
                label: "传送点",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveWarp(store, request)),
                fields: new[]
                {
                    new WebField("name", "名称", WebFieldType.Text, required: true, placeholder: "/warp 用的名称"),
                    new WebField("x", "X", WebFieldType.Number, required: true),
                    new WebField("y", "Y", WebFieldType.Number, required: true),
                    new WebField("z", "Z", WebFieldType.Number, required: true),
                    new WebField("yaw", "朝向(yaw)", WebFieldType.Number),
                    new WebField("cooldownSeconds", "冷却秒数", WebFieldType.Number, placeholder: "0=用全局冷却")
                },
                description: "玩家用 /warp <名称> 传送，还需要权限 well404.essentials.warps.<名称>。坐标可在游戏内用 /warp set 采集。",
                recordsLoader: () => Task.FromResult(LoadWarpRecords(store)),
                deleteHandler: request => Task.FromResult(RemoveWarp(store, request)),
                keyField: "name");

            var gifts = new WebPanelAction(
                id: "gifts",
                label: "礼包",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveGift(store, request)),
                fields: new[]
                {
                    new WebField("id", "礼包ID", WebFieldType.Text, required: true, placeholder: "/gift 用的唯一ID"),
                    new WebField("name", "显示名", WebFieldType.Text, required: true),
                    new WebField("permission", "权限", WebFieldType.Text, placeholder: "留空=所有人；VIP 专属填权限节点"),
                    new WebField("cron", "刷新(crontab)", WebFieldType.Text, placeholder: "如 0 0 * * * (每天0点)；留空=只能领一次"),
                    new WebField("items", "物品", WebFieldType.Text, required: true, placeholder: "物品ID×数量，逗号分隔。如 15x2, 81x1")
                },
                description: "免费礼包。每位玩家在每个 crontab 周期内可领一次。crontab 按服务器本地时间。物品ID 可用下方检索。",
                recordsLoader: () => LoadGiftRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(RemoveGift(store, request)),
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
                description: "在全部游戏物品中按名称或 ID 模糊检索，拿到「物品ID」填到礼包的物品里。");

            return new WebPanelModule(
                ModuleId, "实用功能 / Essentials",
                new[] { teleport, rules, warps, gifts, search },
                icon: "🏠");
        }

        private static WebActionResult SaveTeleport(EssentialsConfigStore store, WebActionRequest request)
        {
            store.Update(s =>
            {
                s.Teleport.WarmupSeconds = ReadInt(request, "warmupSeconds", s.Teleport.WarmupSeconds);
                var move = request.Get("cancelOnMove");
                if (move != null)
                {
                    s.Teleport.CancelOnMove = move == "开";
                }

                s.Teleport.MoveThreshold = request.GetDecimal("moveThreshold") ?? s.Teleport.MoveThreshold;
                s.Teleport.CooldownSeconds = ReadInt(request, "cooldownSeconds", s.Teleport.CooldownSeconds);
                s.Teleport.Costs.Home = request.GetDecimal("costHome") ?? s.Teleport.Costs.Home;
                s.Teleport.Costs.Tp = request.GetDecimal("costTp") ?? s.Teleport.Costs.Tp;
                s.Teleport.Costs.Warp = request.GetDecimal("costWarp") ?? s.Teleport.Costs.Warp;
                s.Teleport.Costs.Back = request.GetDecimal("costBack") ?? s.Teleport.Costs.Back;
            });

            return WebActionResult.Ok("已保存传送设置。");
        }

        private static WebActionResult SaveRules(EssentialsConfigStore store, WebActionRequest request)
        {
            store.Update(s =>
            {
                s.Tpa.ExpirationSeconds = ReadInt(request, "tpaExpiration", s.Tpa.ExpirationSeconds);
                s.Party.InviteExpirationSeconds = ReadInt(request, "partyInviteExpiration", s.Party.InviteExpirationSeconds);
                s.Party.MaxMembers = ReadInt(request, "partyMaxMembers", s.Party.MaxMembers);
                var sleep = request.Get("sleepEnabled");
                if (sleep != null)
                {
                    s.Sleep.Enabled = sleep == "开";
                }

                s.Sleep.RequiredRatio = request.GetDecimal("sleepRatio") ?? s.Sleep.RequiredRatio;
                s.Back.InvincibilitySeconds = ReadInt(request, "backInvincibility", s.Back.InvincibilitySeconds);
            });

            return WebActionResult.Ok("已保存 tpa / sleep / back 设置。");
        }

        private static IReadOnlyList<WebRecord> LoadWarpRecords(EssentialsConfigStore store)
        {
            var records = new List<WebRecord>();
            foreach (var warp in store.Warps)
            {
                records.Add(new WebRecord(
                    warp.Name,
                    warp.Name,
                    new Dictionary<string, string>
                    {
                        ["name"] = warp.Name,
                        ["x"] = Num(warp.X),
                        ["y"] = Num(warp.Y),
                        ["z"] = Num(warp.Z),
                        ["yaw"] = Num(warp.Yaw),
                        ["cooldownSeconds"] = Int(warp.CooldownSeconds)
                    },
                    new[]
                    {
                        $"({Num(warp.X)}, {Num(warp.Y)}, {Num(warp.Z)})",
                        warp.CooldownSeconds > 0 ? $"冷却 {warp.CooldownSeconds}s" : "无冷却"
                    }));
            }

            return records;
        }

        private static WebActionResult SaveWarp(EssentialsConfigStore store, WebActionRequest request)
        {
            var name = request.Get("name");
            if (name == null)
            {
                return WebActionResult.Fail("请填写名称。");
            }

            var x = request.GetDecimal("x");
            var y = request.GetDecimal("y");
            var z = request.GetDecimal("z");
            if (x == null || y == null || z == null)
            {
                return WebActionResult.Fail("请填写 X / Y / Z 坐标。");
            }

            store.UpsertWarp(new WarpEntry
            {
                Name = name,
                X = x.Value,
                Y = y.Value,
                Z = z.Value,
                Yaw = request.GetDecimal("yaw") ?? 0m,
                CooldownSeconds = ReadInt(request, "cooldownSeconds", 0)
            });

            return WebActionResult.Ok($"已保存传送点 {name}。记得给玩家授予权限 {Warps.WarpService.PermissionFor(name)}。");
        }

        private static WebActionResult RemoveWarp(EssentialsConfigStore store, WebActionRequest request)
        {
            var name = request.Get("key");
            if (name == null)
            {
                return WebActionResult.Fail("缺少名称。");
            }

            return store.RemoveWarp(name)
                ? WebActionResult.Ok($"已删除传送点 {name}。")
                : WebActionResult.Fail($"未找到传送点 {name}。");
        }

        private static async Task<IReadOnlyList<WebRecord>> LoadGiftRecordsAsync(
            EssentialsConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await BuildNameMapAsync(itemDirectory);

            var records = new List<WebRecord>();
            foreach (var gift in store.Gifts)
            {
                var rawParts = new List<string>();
                var pills = new List<string>();
                foreach (var item in gift.Items)
                {
                    rawParts.Add(Raw(item.ItemId, item.Amount));
                    pills.Add(FormatItem(item.ItemId, item.Amount, names));
                }

                if (!string.IsNullOrWhiteSpace(gift.Permission))
                {
                    pills.Add("权限: " + gift.Permission);
                }

                pills.Add(string.IsNullOrWhiteSpace(gift.Cron) ? "一次性" : "cron: " + gift.Cron);

                records.Add(new WebRecord(
                    gift.Id,
                    gift.Name.Length > 0 ? gift.Name : gift.Id,
                    new Dictionary<string, string>
                    {
                        ["id"] = gift.Id,
                        ["name"] = gift.Name,
                        ["permission"] = gift.Permission,
                        ["cron"] = gift.Cron,
                        ["items"] = string.Join(", ", rawParts)
                    },
                    pills));
            }

            return records;
        }

        private static WebActionResult SaveGift(EssentialsConfigStore store, WebActionRequest request)
        {
            var id = request.Get("id");
            var name = request.Get("name");
            var itemsRaw = request.Get("items");
            if (id == null || name == null || itemsRaw == null)
            {
                return WebActionResult.Fail("请填写礼包ID、显示名与物品。");
            }

            var parsed = ParseItems(itemsRaw, out var error);
            if (parsed == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (parsed.Count == 0)
            {
                return WebActionResult.Fail("「物品」不能为空，格式如 15x2, 81x1。");
            }

            store.UpsertGift(new GiftEntry
            {
                Id = id,
                Name = name,
                Permission = request.Get("permission") ?? string.Empty,
                Cron = request.Get("cron") ?? string.Empty,
                Items = parsed
            });

            return WebActionResult.Ok($"已保存礼包 {id}（{name}），{parsed.Count} 种物品。");
        }

        private static WebActionResult RemoveGift(EssentialsConfigStore store, WebActionRequest request)
        {
            var id = request.Get("key");
            if (id == null)
            {
                return WebActionResult.Fail("缺少礼包 ID。");
            }

            return store.RemoveGift(id)
                ? WebActionResult.Ok($"已删除礼包 {id}。")
                : WebActionResult.Fail($"未找到礼包 {id}。");
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

        private static string Raw(ushort itemId, int amount)
            => itemId.ToString(CultureInfo.InvariantCulture) + "x" + amount.ToString(CultureInfo.InvariantCulture);

        private static string FormatItem(ushort itemId, int amount, IReadOnlyDictionary<string, string> names)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            var qty = amount.ToString(CultureInfo.InvariantCulture);
            return names.TryGetValue(id, out var name) && name.Length > 0
                ? $"{name}({id})*{qty}"
                : $"{id}*{qty}";
        }

        /// <summary>Parses an "items" string (comma/semicolon separated <c>itemId</c> or <c>itemId×amount</c>).</summary>
        private static List<GiftItem>? ParseItems(string raw, out string? error)
        {
            error = null;
            raw = NormalizeFullWidth(raw);
            var result = new List<GiftItem>();
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

                result.Add(new GiftItem { ItemId = itemId, Amount = amount });
            }

            return result;
        }

        private static int ReadInt(WebActionRequest request, string name, int fallback)
        {
            var value = request.GetDecimal(name);
            return value == null ? fallback : (int)value.Value;
        }

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

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
