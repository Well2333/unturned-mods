using System;

namespace well404.AdminTools
{
    /// <summary>Game-runtime-free helpers for equipment quality repair.</summary>
    public static class EquipmentRepair
    {
        /// <summary>
        /// Repairs quality bytes for gun attachments in an Unturned gun state array. Returns the
        /// number of attached items changed. The supplied predicate decides whether an attachment
        /// asset actually supports durability.
        /// </summary>
        public static int RepairGunAttachmentQualities(byte[] state, Func<ushort, bool> isRepairable)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (isRepairable == null) throw new ArgumentNullException(nameof(isRepairable));
            if (state.Length < 12) return 0;

            var repaired = 0;
            // Gun state stores sight, tactical, grip and barrel IDs at 0/3/6/9 and their qualities immediately after. Magazine data starts at 12 but byte 14 is ammo, not quality.
            foreach (var idOffset in new[] { 0, 3, 6, 9 })
            {
                var attachmentId = BitConverter.ToUInt16(state, idOffset);
                var qualityOffset = idOffset + 2;
                if (attachmentId == 0 || state[qualityOffset] >= 100 || !isRepairable(attachmentId))
                {
                    continue;
                }

                state[qualityOffset] = 100;
                repaired++;
            }

            return repaired;
        }
    }
}
