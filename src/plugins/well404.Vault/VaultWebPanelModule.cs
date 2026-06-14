using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;

namespace well404.Vault
{
    /// <summary>Builds the Vault's <see cref="WebPanelModule"/>: a single setting for the capacity.</summary>
    internal static class VaultWebPanelModule
    {
        public const string ModuleId = "well404.vault";

        public static WebPanelModule Create(VaultConfigStore store)
        {
            var settings = new WebPanelAction(
                id: "settings",
                label: "Vault",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(SaveSettings(store, request)),
                fields: new[]
                {
                    new WebField("maxSlots", "Capacity (grid cells)", WebFieldType.Number, required: true,
                        placeholder: "e.g. 200 — each item costs its size_x×size_y footprint")
                },
                description: "Total vault capacity in inventory grid cells. Each stored item costs its grid footprint (e.g. a 2×2 ammo box = 4); an item's internal stack/ammo count never counts.",
                loader: () => Task.FromResult((IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                {
                    ["maxSlots"] = store.MaxSlots.ToString(CultureInfo.InvariantCulture)
                }));

            return new WebPanelModule(ModuleId, "Vault", new[] { settings }, icon: "🧳");
        }

        private static WebActionResult SaveSettings(VaultConfigStore store, WebActionRequest request)
        {
            var raw = request.Get("maxSlots");
            if (raw == null || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var slots) || slots < 1)
            {
                return WebActionResult.Fail("Enter a valid capacity (a positive whole number).");
            }

            store.SetMaxSlots(slots);
            return WebActionResult.Ok("Saved.");
        }
    }
}
