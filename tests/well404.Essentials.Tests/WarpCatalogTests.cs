using System.Collections.Generic;
using well404.Essentials;
using Xunit;

namespace well404.Essentials.Tests
{
    public class WarpCatalogTests
    {
        [Fact]
        public void NormalizeWarps_MigratesLegacyEntriesToDefaultInOriginalOrder()
        {
            var warps = new List<WarpEntry>
            {
                new WarpEntry { Name = "first", Category = "", Order = 0 },
                new WarpEntry { Name = "second", Category = "  ", Order = 0 },
                new WarpEntry { Name = "third", Category = "vip", Order = 10 }
            };

            EssentialsConfigStore.NormalizeWarps(warps);

            Assert.Equal("default", warps[0].Category);
            Assert.Equal("default", warps[1].Category);
            Assert.Equal("vip", warps[2].Category);
            Assert.Equal(new[] { 1, 2, 10 }, new[] { warps[0].Order, warps[1].Order, warps[2].Order });
        }

        [Fact]
        public void NormalizeWarps_ForceOrderCompactsCurrentListOrder()
        {
            var warps = new List<WarpEntry>
            {
                new WarpEntry { Name = "b", Category = "public", Order = 8 },
                new WarpEntry { Name = "a", Category = "public", Order = 3 }
            };

            EssentialsConfigStore.NormalizeWarps(warps, true);

            Assert.Equal(1, warps[0].Order);
            Assert.Equal(2, warps[1].Order);
        }
    }
}
