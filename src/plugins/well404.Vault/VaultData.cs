using System.Collections.Generic;

namespace well404.Vault
{
    /// <summary>
    /// Persisted vault contents, keyed by Steam ID. Serialized by OpenMod's <c>IDataStore</c> to
    /// <c>vault.data.yaml</c> in the plugin's data directory.
    /// </summary>
    public class VaultData
    {
        public Dictionary<string, List<StoredItem>> Players { get; set; } = new Dictionary<string, List<StoredItem>>();

        /// <summary>Per-player capacity overrides (Steam ID → grid cells). Wins over the config base/tiers.</summary>
        public Dictionary<string, int> Overrides { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>One stored item, kept with full state so guns/magazines/ammo boxes restore exactly.</summary>
    public class StoredItem
    {
        /// <summary>A short unique id for this exact stored copy (so a specific instance can be withdrawn).</summary>
        public string Uid { get; set; } = string.Empty;

        /// <summary>Game item asset id.</summary>
        public ushort ItemId { get; set; }

        /// <summary>Stack amount (most items 1; the internal ammo/round count lives in <see cref="State"/>).</summary>
        public byte Amount { get; set; } = 1;

        /// <summary>Item quality/durability (0–100).</summary>
        public byte Quality { get; set; } = 100;

        /// <summary>Base64 of the raw item state bytes (attachments, ammo count, barrel, etc.).</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>Grid cells this item occupies (size_x × size_y), captured when stored.</summary>
        public int SlotCost { get; set; } = 1;
    }
}
