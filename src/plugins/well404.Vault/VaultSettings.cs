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
        public int MaxSlots { get; set; } = 50;

        /// <summary>
        /// Per-permission capacity tiers: a player who holds the permission gets that capacity. A
        /// player takes the <b>largest</b> capacity among the base and every tier they hold (so a VIP
        /// gets a bigger vault). Administrators may adjust the current effective capacity from Vault inspection.
        /// e.g. <c>well404.vault.size.vip: 400</c>.
        /// </summary>
        public Dictionary<string, int> Tiers { get; set; } = new Dictionary<string, int>();

        /// <summary>Players may buy additional personal-vault capacity with their own balance.</summary>
        public PersonalVaultPurchaseSettings PersonalPurchase { get; set; } = new PersonalVaultPurchaseSettings();

        /// <summary>Shared storage owned by the player's current Unturned party.</summary>
        public TeamVaultSettings TeamVault { get; set; } = new TeamVaultSettings();
    }

    public sealed class PersonalVaultPurchaseSettings
    {
        public bool Enabled { get; set; } = true;
        public int MaxSlots { get; set; } = 5000;
        public int SlotsPerPurchase { get; set; } = 10;
        public decimal Price { get; set; } = 100m;
    }

    public sealed class TeamVaultSettings
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Capacity a newly-created team vault receives without payment.</summary>
        public int BaseSlots { get; set; } = 200;

        /// <summary>Hard capacity ceiling after purchases. Defaults to 5000 as requested.</summary>
        public int MaxSlots { get; set; } = 5000;

        public TeamVaultPurchaseSettings Purchase { get; set; } = new TeamVaultPurchaseSettings();
    }

    public sealed class TeamVaultPurchaseSettings
    {
        public bool Enabled { get; set; } = true;
        public int SlotsPerPurchase { get; set; } = 10;
        public decimal Price { get; set; } = 500m;
    }
}
