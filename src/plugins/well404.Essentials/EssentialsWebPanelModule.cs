using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.Items;
using UnturnedMods.Shared.WebPanel;
using well404.Essentials.Warps;

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
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(EssentialsWebPanelModule).Assembly, "admin-ui.html", "admin-ui.css", "admin-ui.js");

        private const int SearchLimit = 100;

        public static WebPanelModule Create(
            EssentialsConfigStore store,
            WarpService warpService,
            IItemDirectory itemDirectory)
        {
            var teleport = new WebPanelAction(
                id: "teleport",
                label: "Teleport rules",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveTeleport(store, request)),
                fields: new[]
                {
                    new WebField("warmupSeconds", "Warm-up seconds", WebFieldType.Number, placeholder: "Seconds to stand still before teleporting; 0 = instant"),
                    new WebField("cancelOnMove", "Cancel on move", WebFieldType.Boolean),
                    new WebField("moveThreshold", "Move threshold (m)", WebFieldType.Number),
                    new WebField("cooldownSeconds", "Cooldown seconds", WebFieldType.Number, placeholder: "Cooldown after a successful teleport; 0 = none"),
                    new WebField("costHome", "home cost", WebFieldType.Number),
                    new WebField("costTp", "tp cost", WebFieldType.Number),
                    new WebField("costWarp", "warp cost", WebFieldType.Number),
                    new WebField("costBack", "back cost", WebFieldType.Number)
                },
                description: "Shared rules for all teleports (home/tp/warp/back). Costs require an economy plugin (e.g. well404.Economy); default 0 = free.",
                loader: () => Task.FromResult(store.Read(s => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["warmupSeconds"] = Int(s.Teleport.WarmupSeconds),
                    ["cancelOnMove"] = s.Teleport.CancelOnMove ? "true" : "false",
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
                    new WebField("tpaExpiration", "tpa request lifetime (s)", WebFieldType.Number),
                    new WebField("partyInviteExpiration", "party invite lifetime (s)", WebFieldType.Number),
                    new WebField("partyMaxMembers", "party max members", WebFieldType.Number, placeholder: "0 = no extra limit"),
                    new WebField("sleepEnabled", "sleep voting", WebFieldType.Boolean),
                    new WebField("sleepRatio", "sleep pass ratio", WebFieldType.Number, placeholder: "0.5 = half"),
                    new WebField("backInvincibility", "back invincibility (s)", WebFieldType.Number)
                },
                description: "tpa/party request timeouts, party member cap, sleep-vote toggle and pass ratio (of online players), and /back post-landing invincibility seconds (0 = none).",
                loader: () => Task.FromResult(store.Read(s => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["tpaExpiration"] = Int(s.Tpa.ExpirationSeconds),
                    ["partyInviteExpiration"] = Int(s.Party.InviteExpirationSeconds),
                    ["partyMaxMembers"] = Int(s.Party.MaxMembers),
                    ["sleepEnabled"] = s.Sleep.Enabled ? "true" : "false",
                    ["sleepRatio"] = Num(s.Sleep.RequiredRatio),
                    ["backInvincibility"] = Int(s.Back.InvincibilitySeconds)
                })));

            var warps = new WebPanelAction(
                id: "warps",
                label: "Warps",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveWarp(warpService, request)),
                fields: new[]
                {
                    new WebField("name", "Name", WebFieldType.Text, required: true, placeholder: "Name used by /warp"),
                    new WebField("x", "X", WebFieldType.Number, required: true),
                    new WebField("y", "Y", WebFieldType.Number, required: true),
                    new WebField("z", "Z", WebFieldType.Number, required: true),
                    new WebField("yaw", "Yaw", WebFieldType.Number),
                    new WebField("cooldownSeconds", "Cooldown seconds", WebFieldType.Number, placeholder: "0 = use global cooldown")
                },
                description: "Players teleport with /warp <name> and also need permission well404.Essentials:well404.essentials.warps.<name>. Capture coordinates in-game with /warp set.",
                recordsLoader: () => Task.FromResult(LoadWarpRecords(store)),
                deleteHandler: request => Task.FromResult(RemoveWarp(warpService, request)),
                keyField: "name");

            var gifts = new WebPanelAction(
                id: "gifts",
                label: "Gift packs",
                kind: WebActionKind.Collection,
                handler: request => Task.FromResult(SaveGift(store, request)),
                fields: new[]
                {
                    new WebField("id", "Gift ID", WebFieldType.Text, required: true, placeholder: "Unique ID used by /gift"),
                    new WebField("name", "Display name", WebFieldType.Text, required: true),
                    new WebField("permission", "Permission", WebFieldType.Text, placeholder: "Empty = everyone; set a permission node for VIP-only"),
                    new WebField("cron", "Refresh (crontab)", WebFieldType.Text, placeholder: "e.g. 0 0 * * * (daily at 0:00); empty = one-time only"),
                    new WebField("items", "Gift contents", WebFieldType.Text, required: true, placeholder: "itemId\u00d7amount, comma-separated. e.g. 15x2, 81x1")
                },
                description: "Free gift packs. Each player may claim once per crontab period. crontab uses server local time. Look up item IDs with the search below.",
                recordsLoader: () => LoadGiftRecordsAsync(store, itemDirectory),
                deleteHandler: request => Task.FromResult(RemoveGift(store, request)),
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
                description: "Fuzzy-search all game items by name or ID; take the item ID into the gift contents.");

            return new WebPanelModule(
                ModuleId, "Essentials",
                new[] { teleport, rules, warps, gifts, search },
                icon: "🏠", ui: s_Ui);
        }

        private static WebActionResult SaveTeleport(EssentialsConfigStore store, WebActionRequest request)
        {
            store.Update(s =>
            {
                s.Teleport.WarmupSeconds = ReadInt(request, "warmupSeconds", s.Teleport.WarmupSeconds);
                var move = request.Get("cancelOnMove");
                if (move != null)
                {
                    s.Teleport.CancelOnMove = move == "true";
                }

                s.Teleport.MoveThreshold = request.GetDecimal("moveThreshold") ?? s.Teleport.MoveThreshold;
                s.Teleport.CooldownSeconds = ReadInt(request, "cooldownSeconds", s.Teleport.CooldownSeconds);
                s.Teleport.Costs.Home = request.GetDecimal("costHome") ?? s.Teleport.Costs.Home;
                s.Teleport.Costs.Tp = request.GetDecimal("costTp") ?? s.Teleport.Costs.Tp;
                s.Teleport.Costs.Warp = request.GetDecimal("costWarp") ?? s.Teleport.Costs.Warp;
                s.Teleport.Costs.Back = request.GetDecimal("costBack") ?? s.Teleport.Costs.Back;
            });

            return WebActionResult.Ok("Saved teleport rules.");
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
                    s.Sleep.Enabled = sleep == "true";
                }

                s.Sleep.RequiredRatio = request.GetDecimal("sleepRatio") ?? s.Sleep.RequiredRatio;
                s.Back.InvincibilitySeconds = ReadInt(request, "backInvincibility", s.Back.InvincibilitySeconds);
            });

            return WebActionResult.Ok("Saved tpa / sleep / back settings.");
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
                        warp.CooldownSeconds > 0 ? $"cooldown {warp.CooldownSeconds}s" : "no cooldown"
                    }));
            }

            return records;
        }

        private static WebActionResult SaveWarp(WarpService warps, WebActionRequest request)
        {
            var name = request.Get("name");
            if (name == null)
            {
                return WebActionResult.Fail("Enter a name.");
            }

            var x = request.GetDecimal("x");
            var y = request.GetDecimal("y");
            var z = request.GetDecimal("z");
            if (x == null || y == null || z == null)
            {
                return WebActionResult.Fail("Enter X / Y / Z coordinates.");
            }

            warps.Upsert(new WarpEntry
            {
                Name = name,
                X = x.Value,
                Y = y.Value,
                Z = z.Value,
                Yaw = request.GetDecimal("yaw") ?? 0m,
                CooldownSeconds = ReadInt(request, "cooldownSeconds", 0)
            });

            return WebActionResult.Ok($"Saved warp {name}. Remember to grant players the permission {Warps.WarpService.PermissionFor(name)}.");
        }

        private static WebActionResult RemoveWarp(WarpService warps, WebActionRequest request)
        {
            var name = request.Get("key");
            if (name == null)
            {
                return WebActionResult.Fail("Missing name.");
            }

            return warps.Delete(name)
                ? WebActionResult.Ok($"Deleted warp {name}.")
                : WebActionResult.Fail($"Warp not found: {name}.");
        }

        private static async Task<IReadOnlyList<WebRecord>> LoadGiftRecordsAsync(
            EssentialsConfigStore store, IItemDirectory itemDirectory)
        {
            var names = await LocalizedItemCatalog.BuildAsync(itemDirectory);

            var records = new List<WebRecord>();
            foreach (var gift in store.Gifts)
            {
                var rawParts = new List<string>();
                var pills = new List<string>();
                foreach (var item in gift.Items)
                {
                    rawParts.Add(Raw(item.ItemId, item.Amount));
                    pills.Add(FormatItem(item.ItemId, item.Amount, names, "zh"));
                }

                if (!string.IsNullOrWhiteSpace(gift.Permission))
                {
                    pills.Add("perm: " + gift.Permission);
                }

                pills.Add(string.IsNullOrWhiteSpace(gift.Cron) ? "one-time" : "cron: " + gift.Cron);

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
                return WebActionResult.Fail("Enter the gift ID, display name and contents.");
            }

            var parsed = ParseItems(itemsRaw, out var error);
            if (parsed == null)
            {
                return WebActionResult.Fail(error!);
            }

            if (parsed.Count == 0)
            {
                return WebActionResult.Fail("Contents cannot be empty, e.g. 15x2, 81x1.");
            }

            store.UpsertGift(new GiftEntry
            {
                Id = id,
                Name = name,
                Permission = request.Get("permission") ?? string.Empty,
                Cron = request.Get("cron") ?? string.Empty,
                Items = parsed
            });

            return WebActionResult.Ok($"Saved gift {id} ({name}), {parsed.Count} item(s).");
        }

        private static WebActionResult RemoveGift(EssentialsConfigStore store, WebActionRequest request)
        {
            var id = request.Get("key");
            if (id == null)
            {
                return WebActionResult.Fail("Missing gift ID.");
            }

            return store.RemoveGift(id)
                ? WebActionResult.Ok($"Deleted gift {id}.")
                : WebActionResult.Fail($"Gift not found: {id}.");
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
            var names = await LocalizedItemCatalog.BuildAsync(itemDirectory);

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
                        var exactName = names.TryGetValue(exactId, out var exactInfo)
                            ? exactInfo.DisplayName(request.Language) : asset.ItemName ?? string.Empty;
                        rows.Add(new[] { exactId, exactName });
                        seen.Add(trimmed);
                        break;
                    }
                }
            }

            foreach (var asset in assets)
            {
                var assetId = asset.ItemAssetId ?? string.Empty;
                var hasResolvedName = names.TryGetValue(assetId, out var resolvedName);
                var itemName = hasResolvedName
                    ? resolvedName!.DisplayName(request.Language) : asset.ItemName ?? string.Empty;
                if (seen.Contains(assetId))
                {
                    continue;
                }

                var match = assetId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (hasResolvedName ? resolvedName!.Matches(query)
                        : itemName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
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

        private static string Raw(ushort itemId, int amount)
            => itemId.ToString(CultureInfo.InvariantCulture) + "x" + amount.ToString(CultureInfo.InvariantCulture);

        private static string FormatItem(ushort itemId, int amount, IReadOnlyDictionary<string, LocalizedItemInfo> names, string language)
        {
            var id = itemId.ToString(CultureInfo.InvariantCulture);
            var qty = amount.ToString(CultureInfo.InvariantCulture);
            var display = LocalizedItemCatalog.DisplayName(itemId, names, language);
            return string.Join("\n", display.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name + "(" + id + ")*" + qty));
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
                    error = $"Invalid item ID: {token}";
                    return null;
                }

                if (!int.TryParse(amountPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount < 1)
                {
                    error = $"Invalid amount: {token} (format itemId\u00d7amount, e.g. 15x2)";
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
