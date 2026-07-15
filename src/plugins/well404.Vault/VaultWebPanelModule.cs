using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using UnturnedMods.Shared.Items;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>
    /// Builds the Vault's <see cref="WebPanelModule"/>: capacity settings (base + per-permission
    /// tiers) and a per-player capacity-override collection.
    /// </summary>
    internal static class VaultWebPanelModule
    {
        public const string ModuleId = "well404.vault";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(VaultWebPanelModule).Assembly, "admin-ui.html", "admin-ui.css", "admin-ui.js");

        public static WebPanelModule Create(VaultConfigStore store, VaultService vault, IItemDirectory itemDirectory)
        {
            var settings = new WebPanelAction(
                id: "settings",
                label: "Vault",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveSettings(store, request)),
                fields: new[]
                {
                    new WebField("maxSlots", "Capacity (grid cells)", WebFieldType.Number, required: true,
                        placeholder: "e.g. 200 — each item costs its size_x×size_y footprint"),
                    new WebField("tiers", "Capacity tiers", WebFieldType.Text,
                        placeholder: "permission=capacity, comma-separated: e.g. well404.vault.size.vip=400, well404.vault.size.mvp=600")
                },
                description: "Base capacity everyone gets, plus per-permission tiers: a player gets the largest capacity among the base and the tiers they hold. Tiers format: permission=capacity (comma-separated); empty clears all tiers. A specific player can be overridden below.",
                loader: () => Task.FromResult((IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["maxSlots"] = store.MaxSlots.ToString(CultureInfo.InvariantCulture),
                    ["tiers"] = FormatTiers(store.Tiers)
                }));

            var overrides = new WebPanelAction(
                id: "overrides",
                label: "Per-player capacity",
                kind: WebActionKind.Collection,
                handler: request => SaveOverrideAsync(vault, request),
                fields: new[]
                {
                    new WebField("steamId", "Steam ID", WebFieldType.Text, required: true),
                    new WebField("capacity", "Capacity", WebFieldType.Number, required: true)
                },
                description: "Override a specific player's vault capacity (in grid cells). This wins over the base and tiers.",
                recordsLoader: () => LoadOverrideRecordsAsync(vault),
                deleteHandler: request => DeleteOverrideAsync(vault, request),
                keyField: "steamId",
                layout: "list",
                summaryFields: new[] { "capacity" });

            var inventory = new WebPanelAction(
                id: "inventory",
                label: "Player vault contents",
                kind: WebActionKind.Table,
                handler: request => LoadInventoryAsync(vault, itemDirectory, request),
                description: "Load the exact SQLite rows stored for one player.",
                hidden: true);

            var updateItem = new WebPanelAction(
                id: "updateitem",
                label: "Update stored item",
                kind: WebActionKind.Form,
                handler: request => UpdateItemAsync(vault, request),
                hidden: true);

            var deleteItem = new WebPanelAction(
                id: "deleteitem",
                label: "Delete stored item",
                kind: WebActionKind.Form,
                handler: request => DeleteItemAsync(vault, request),
                hidden: true);

            var deleteItems = new WebPanelAction(
                id: "deleteitems",
                label: "Delete matching stored items",
                kind: WebActionKind.Form,
                handler: request => DeleteItemsAsync(vault, request),
                hidden: true);

            return new WebPanelModule(ModuleId, "Vault",
                new[] { settings, overrides, inventory, updateItem, deleteItem, deleteItems },
                icon: "🧳", ui: s_Ui);
        }

        private static async Task<WebActionResult> LoadInventoryAsync(
            VaultService vault, IItemDirectory itemDirectory, WebActionRequest request)
        {
            var steamId = ValidSteamId(request.Get("steamId"));
            if (steamId == null)
            {
                return WebActionResult.Fail("Enter a valid 17-digit Steam ID.");
            }

            var names = await LocalizedItemCatalog.BuildAsync(itemDirectory);
            var rows = new List<IReadOnlyList<string>>();
            foreach (var item in vault.Get(steamId))
            {
                rows.Add(new[]
                {
                    item.RecordId.ToString(CultureInfo.InvariantCulture),
                    item.ItemId.ToString(CultureInfo.InvariantCulture),
                    LocalizedItemCatalog.DisplayName(item.ItemId, names, request.Language),
                    item.Amount.ToString(CultureInfo.InvariantCulture),
                    item.Quality.ToString(CultureInfo.InvariantCulture),
                    item.SlotCost.ToString(CultureInfo.InvariantCulture),
                    item.MaxAmount.ToString(CultureInfo.InvariantCulture)
                });
            }

            var used = vault.UsedSlots(steamId).ToString(CultureInfo.InvariantCulture);
            return WebActionResult.Table(
                new[] { "Record ID", "Item ID", "Name", "Amount", "Quality", "Grid cells", "Max amount" },
                rows,
                rows.Count == 0 ? "This player's vault is empty." : $"{rows.Count} stored row(s), {used} grid cell(s) used.");
        }

        private static async Task<WebActionResult> UpdateItemAsync(VaultService vault, WebActionRequest request)
        {
            var steamId = ValidSteamId(request.Get("steamId"));
            if (steamId == null
                || !long.TryParse(request.Get("recordId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordId)
                || recordId < 1
                || !ushort.TryParse(request.Get("itemId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0
                || !byte.TryParse(request.Get("amount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
                || amount == 0
                || !byte.TryParse(request.Get("quality"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality)
                || quality > 100)
            {
                return WebActionResult.Fail("Enter a valid Steam ID, record ID, item ID, amount (1–255), and quality (0–100).");
            }

            return await vault.UpdateStoredItemAsync(steamId, recordId, itemId, amount, quality)
                ? WebActionResult.Ok("Stored item updated; its opaque state data was preserved.")
                : WebActionResult.Fail("Stored row not found for this player.");
        }

        private static async Task<WebActionResult> DeleteItemAsync(VaultService vault, WebActionRequest request)
        {
            var steamId = ValidSteamId(request.Get("steamId"));
            if (steamId == null
                || !long.TryParse(request.Get("recordId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recordId)
                || recordId < 1)
            {
                return WebActionResult.Fail("Enter a valid Steam ID and record ID.");
            }

            return await vault.DeleteStoredItemAsync(steamId, recordId)
                ? WebActionResult.Ok("Stored row deleted.")
                : WebActionResult.Fail("Stored row not found for this player.");
        }

        private static async Task<WebActionResult> DeleteItemsAsync(VaultService vault, WebActionRequest request)
        {
            var steamId = ValidSteamId(request.Get("steamId"));
            if (steamId == null
                || !ushort.TryParse(request.Get("itemId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var itemId)
                || itemId == 0)
            {
                return WebActionResult.Fail("Enter a valid Steam ID and item ID.");
            }

            var count = await vault.DeleteStoredItemsAsync(steamId, itemId);
            return count > 0
                ? WebActionResult.Ok($"Deleted {count} stored row(s) with item ID {itemId}.")
                : WebActionResult.Fail("No matching stored rows were found for this player.");
        }

        private static string? ValidSteamId(string? value)
        {
            if (value == null || value.Length != 17 || !ulong.TryParse(value, out var parsed) || parsed == 0)
            {
                return null;
            }

            return value;
        }

        private static WebActionResult SaveSettings(VaultConfigStore store, WebActionRequest request)
        {
            var raw = request.Get("maxSlots");
            if (raw == null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots) || slots < 1)
            {
                return WebActionResult.Fail("Enter a valid capacity (a positive whole number).");
            }

            var tiers = ParseTiers(request.Get("tiers") ?? string.Empty, out var error);
            if (tiers == null)
            {
                return WebActionResult.Fail(error!);
            }

            store.Save(slots, tiers);
            return WebActionResult.Ok("Saved.");
        }

        private static Task<IReadOnlyList<WebRecord>> LoadOverrideRecordsAsync(VaultService vault)
        {
            var records = new List<WebRecord>();
            foreach (var pair in vault.Overrides)
            {
                records.Add(new WebRecord(
                    pair.Key,
                    pair.Key,
                    new Dictionary<string, string>
                    {
                        ["steamId"] = pair.Key,
                        ["capacity"] = pair.Value.ToString(CultureInfo.InvariantCulture)
                    }));
            }

            return Task.FromResult((IReadOnlyList<WebRecord>)records);
        }

        private static async Task<WebActionResult> SaveOverrideAsync(VaultService vault, WebActionRequest request)
        {
            var steamId = request.Get("steamId");
            var capRaw = request.Get("capacity");
            if (steamId == null || capRaw == null
                || !int.TryParse(capRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap) || cap < 1)
            {
                return WebActionResult.Fail("Enter a Steam ID and a capacity.");
            }

            await vault.SetOverrideAsync(steamId, cap);
            return WebActionResult.Ok("Saved.");
        }

        private static async Task<WebActionResult> DeleteOverrideAsync(VaultService vault, WebActionRequest request)
        {
            var steamId = request.Get("key");
            if (steamId == null)
            {
                return WebActionResult.Fail("Not found.");
            }

            return await vault.ClearOverrideAsync(steamId)
                ? WebActionResult.Ok("Deleted.")
                : WebActionResult.Fail("Not found.");
        }

        private static string FormatTiers(IReadOnlyDictionary<string, int> tiers)
        {
            var parts = new List<string>();
            foreach (var tier in tiers)
            {
                parts.Add(tier.Key + "=" + tier.Value.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
        }

        /// <summary>Parses "perm=capacity, perm:capacity" into a map. Returns null + error on a bad entry.</summary>
        private static Dictionary<string, int>? ParseTiers(string raw, out string? error)
        {
            error = null;
            var result = new Dictionary<string, int>();
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
                    // Arg-free so the host can localize it by key (no interpolated token).
                    error = "Bad tier format (expected permission=capacity).";
                    return null;
                }

                var perm = parts[0].Trim();
                if (perm.Length == 0
                    || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap)
                    || cap < 1)
                {
                    error = "Invalid tier capacity (a positive whole number).";
                    return null;
                }

                result[perm] = cap;
            }

            return result;
        }
    }
}
