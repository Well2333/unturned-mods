namespace well404.Vault
{
    /// <summary>Strongly-typed view of the Vault <c>config.yaml</c>.</summary>
    public class VaultSettings
    {
        /// <summary>
        /// Total vault capacity in inventory grid cells. Each stored item costs its own grid
        /// footprint (size_x × size_y, e.g. a 2×2 ammo box = 4), so big items cost more than small
        /// ones. An item's internal stack/ammo count never affects the cost.
        /// </summary>
        public int MaxSlots { get; set; } = 200;
    }
}
