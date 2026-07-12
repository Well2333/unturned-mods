namespace well404.Vault
{
    /// <summary>One item row stored in vault.sqlite3 with full Unturned item-state fidelity.</summary>
    public class StoredItem
    {
        /// <summary>SQLite row id.</summary>
        public long RecordId { get; set; }

        /// <summary>Game item asset id.</summary>
        public ushort ItemId { get; set; }

        /// <summary>Stack amount; internal ammunition can also be represented in State.</summary>
        public byte Amount { get; set; } = 1;

        /// <summary>Item quality/durability (0–100).</summary>
        public byte Quality { get; set; } = 100;

        /// <summary>Base64 of raw item state bytes (attachments, ammo count, barrel, etc.).</summary>
        public string State { get; set; } = string.Empty;

        /// <summary>Inventory grid cells occupied by this item.</summary>
        public int SlotCost { get; set; } = 1;

        /// <summary>Full stack or magazine capacity; 0 when not applicable.</summary>
        public byte MaxAmount { get; set; }
    }
}
