using System.Collections.Generic;
using well404.Essentials;
using well404.Essentials.Data;
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

            Assert.Equal(new[] { "default" }, warps[0].Tags);
            Assert.Equal(new[] { "default" }, warps[1].Tags);
            Assert.Equal(new[] { "vip" }, warps[2].Tags);
            Assert.Equal(new[] { 1, 2, 10 }, new[] { warps[0].Order, warps[1].Order, warps[2].Order });
        }

        [Fact]
        public void ParseTags_SplitsWhitespaceAndCommasAndRemovesDuplicates()
        {
            var tags = EssentialsConfigStore.ParseTags(new[] { "public city", "VIP,city", "  " });

            Assert.Equal(new[] { "public", "city", "vip" }, tags);
        }

        [Fact]
        public void NormalizeWarpTags_MaterializesPresetsAndMigratesUnknownWarpTags()
        {
            var settings = new WarpTagSettings();
            var warps = new List<WarpEntry>
            {
                new WarpEntry { Name = "legacy", Tags = new List<string> { "city", "trade-hub" } }
            };

            var changed = EssentialsConfigStore.NormalizeWarpTagSettings(settings, warps);

            Assert.True(changed);
            Assert.True(settings.Initialized);
            Assert.Contains(settings.Presets, tag => tag.Id == "city" && tag.NameZh == "城市" && tag.Emoji == "🏙️");
            Assert.Contains(settings.Presets, tag => tag.Id == "military-base" && tag.NameEn == "Military Base");
            Assert.Contains(settings.Custom, tag => tag.Id == "trade-hub" && tag.NameEn == "trade-hub");
        }

        [Theory]
        [InlineData(null, "compact")]
        [InlineData("AUTO", "compact")]
        [InlineData("compact", "compact")]
        [InlineData("LARGE", "large")]
        [InlineData("anything", "compact")]
        public void PlayerMapSize_NormalizesToSupportedServerValues(string? value, string expected)
        {
            Assert.Equal(expected, PlayerDataStore.NormalizeWarpMapSize(value));
        }

        [Fact]
        public void NormalizeWarps_ForceOrderCompactsCurrentListOrder()
        {
            var warps = new List<WarpEntry>
            {
                new WarpEntry { Name = "b", Tags = new List<string> { "public", "city" }, Order = 8 },
                new WarpEntry { Name = "a", Tags = new List<string> { "public" }, Order = 3 }
            };

            EssentialsConfigStore.NormalizeWarps(warps, true);

            Assert.Equal(1, warps[0].Order);
            Assert.Equal(2, warps[1].Order);
        }
    }
}
