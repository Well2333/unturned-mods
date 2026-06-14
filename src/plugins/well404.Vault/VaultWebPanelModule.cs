using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
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

        public static WebPanelModule Create(VaultConfigStore store, VaultService vault)
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

            return new WebPanelModule(ModuleId, "Vault", new[] { settings, overrides }, icon: "🧳");
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
                    error = $"Bad tier format: {token} (expected permission=capacity)";
                    return null;
                }

                var perm = parts[0].Trim();
                if (perm.Length == 0
                    || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cap)
                    || cap < 1)
                {
                    error = $"Invalid tier capacity: {token} (a positive whole number)";
                    return null;
                }

                result[perm] = cap;
            }

            return result;
        }
    }
}
