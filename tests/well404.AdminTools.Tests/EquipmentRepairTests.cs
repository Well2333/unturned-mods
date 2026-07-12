using System;
using well404.AdminTools;
using Xunit;

namespace well404.AdminTools.Tests
{
    public class EquipmentRepairTests
    {
        [Fact]
        public void RepairGunAttachmentQualities_RepairsEligibleAttachmentsOnly()
        {
            var state = new byte[18];
            Put(state, 0, 100, 20);  // sight: repairable
            Put(state, 3, 200, 45);  // tactical: not repairable according to predicate
            Put(state, 6, 300, 100); // grip: already full
            Put(state, 9, 400, 1);   // barrel: repairable
            Put(state, 12, 500, 7);  // magazine ID + ammo count; byte 14 is not quality

            var repaired = EquipmentRepair.RepairGunAttachmentQualities(
                state, id => id == 100 || id == 400 || id == 500);

            Assert.Equal(2, repaired);
            Assert.Equal(100, state[2]);
            Assert.Equal(45, state[5]);
            Assert.Equal(100, state[8]);
            Assert.Equal(100, state[11]);
            Assert.Equal(7, state[14]);
        }

        [Fact]
        public void RepairGunAttachmentQualities_ShortState_IsIgnored()
        {
            var state = new byte[11];
            Assert.Equal(0, EquipmentRepair.RepairGunAttachmentQualities(state, _ => true));
        }

        private static void Put(byte[] state, int offset, ushort id, byte quality)
        {
            var bytes = BitConverter.GetBytes(id);
            state[offset] = bytes[0];
            state[offset + 1] = bytes[1];
            state[offset + 2] = quality;
        }
    }
}
