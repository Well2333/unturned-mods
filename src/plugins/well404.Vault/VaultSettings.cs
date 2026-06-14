using System.Collections.Generic;

namespace well404.Vault
{
    /// <summary>Strongly-typed view of the Vault <c>config.yaml</c>.</summary>
    public class VaultSettings
    {
        /// <summary>
        /// Base vault capacity in inventory grid cells (everyone gets at least this). Each stored
        /// item costs its own grid footprint (size_x × size_y, e.g. a 2×2 ammo box = 4), so big items
        /// cost more than small ones. An item's internal stack/ammo count never affects the cost.
        /// </summary>
        public int MaxSlots { get; set; } = 200;

        /// <summary>
        /// Per-permission capacity tiers: a player who holds the permission gets that capacity. A
        /// player takes the <b>largest</b> capacity among the base and every tier they hold (so a VIP
        /// gets a bigger vault). A specific player can still be overridden individually (see the
        /// per-player overrides managed in the web panel / data file).
        /// e.g. <c>well404.vault.size.vip: 400</c>.
        /// </summary>
        public Dictionary<string, int> Tiers { get; set; } = new Dictionary<string, int>();
    }
}
