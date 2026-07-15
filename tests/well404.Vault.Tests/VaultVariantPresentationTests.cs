using System.Linq;
using UnturnedMods.Shared.Items;
using well404.Vault;
using Xunit;

namespace well404.Vault.Tests
{
    public class VaultVariantPresentationTests
    {
        [Fact]
        public void NonQualityItems_IgnoreRawQualityWhenMerging()
        {
            var variants = new[]
            {
                new ItemVariant(96, 1, 70, string.Empty, 1, 2, 0),
                new ItemVariant(96, 1, 39, string.Empty, 1, 2, 0),
                new ItemVariant(96, 1, 10, string.Empty, 1, 2, 0)
            };

            var merged = Assert.Single(VaultPlayerMenu.MergeVisibleVariants(variants, false));

            Assert.Equal(3, merged.Count);
            Assert.Equal("96|1|*|", VaultPlayerMenu.VariantKey(merged, false));
            Assert.Null(VaultPlayerMenu.ParseVariant("96|1|*|")!.Value.quality);
            Assert.False(VaultPlayerMenu.ShouldShowQuality(false, 10));
        }

        [Fact]
        public void QualityItems_RemainSeparateVariants()
        {
            var variants = new[]
            {
                new ItemVariant(4, 1, 70, string.Empty, 1, 4, 0),
                new ItemVariant(4, 1, 39, string.Empty, 2, 4, 0)
            };

            var merged = VaultPlayerMenu.MergeVisibleVariants(variants, true)
                .OrderBy(item => item.Quality)
                .ToList();

            Assert.Equal(2, merged.Count);
            Assert.Equal("4|1|39|", VaultPlayerMenu.VariantKey(merged[0], true));
            Assert.Equal((byte)39, VaultPlayerMenu.ParseVariant("4|1|39|")!.Value.quality);
            Assert.True(VaultPlayerMenu.ShouldShowQuality(true, 39));
            Assert.False(VaultPlayerMenu.ShouldShowQuality(true, 100));
        }

        [Theory]
        [InlineData("MAGAZINE", "ammunition")]
        [InlineData("FOOD", "food")]
        [InlineData("WATER", "food")]
        [InlineData("MEDICAL", "medical")]
        [InlineData("GUN", "weapons")]
        [InlineData("MELEE", "weapons")]
        [InlineData("SUPPLY", "materials")]
        [InlineData("TOOL", "tools")]
        [InlineData("VEHICLE_REPAIR_TOOL", "tools")]
        [InlineData("BACKPACK", "clothing")]
        [InlineData("SIGHT", "attachments")]
        [InlineData("STRUCTURE", "building")]
        [InlineData("TIRE", "vehicles")]
        [InlineData("FUTURE_WORKSHOP_TYPE", "other")]
        public void NativeItemTypes_MapToStableFilters(string itemType, string expected)
            => Assert.Equal(expected, LocalizedItemCatalog.CategoryForType(itemType));

        [Fact]
        public void AmmoSupplyBlueprint_OverridesGenericMaterialCategory()
        {
            Assert.Equal("materials", LocalizedItemCatalog.CategoryForType("SUPPLY", false));
            Assert.Equal("ammunition", LocalizedItemCatalog.CategoryForType("SUPPLY", true));
        }


        [Theory]
        [InlineData("Common", 0)]
        [InlineData("Uncommon", 1)]
        [InlineData("Rare", 2)]
        [InlineData("Epic", 3)]
        [InlineData("Legendary", 4)]
        [InlineData("Mythical", 5)]
        [InlineData("Unknown", 0)]
        public void NativeRarity_MapsToStableAscendingRank(string rarity, int expected)
            => Assert.Equal(expected, LocalizedItemCatalog.RarityRankFor(rarity));

        [Fact]
        public void ItemMetadata_ContainsAllSortFilterAndRarityFields()
        {
            var item = new LocalizedItemInfo(
                "Military Magazine", "军用弹匣", false,
                "MAGAZINE", "rare", 2, "ammunition");

            var metadata = VaultPlayerMenu.ItemMetadata(item, 6, 9, 18);

            Assert.Equal("6", metadata["itemId"]);
            Assert.Equal("9", metadata["count"]);
            Assert.Equal("18", metadata["totalSlots"]);
            Assert.Equal("rare", metadata["rarity"]);
            Assert.Equal("2", metadata["rarityRank"]);
            Assert.Equal("ammunition", metadata["category"]);
            Assert.Equal("MAGAZINE", metadata["itemType"]);
        }

    }
}
