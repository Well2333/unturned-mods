using System.Collections.Generic;
using UnturnedMods.Shared.WebPanel;
using well404.Shop;
using Xunit;

namespace well404.Shop.Tests
{
    public class ShopCatalogDesignTests
    {
        [Fact]
        public void Normalize_LegacyCatalog_CreatesDefaultGroupAndStableOrder()
        {
            var settings = new ShopSettings
            {
                Groups = new List<ShopGroupConfig>(),
                Items = new List<ShopItemConfig>
                {
                    new ShopItemConfig { ItemId = 15, Group = "", Note = null!, Order = 0 }
                }
            };

            var changed = ShopConfiguration.Normalize(settings);

            Assert.True(changed);
            var group = Assert.Single(settings.Groups);
            Assert.Equal("default", group.Id);
            Assert.Equal("default", settings.Items[0].Group);
            Assert.Equal(string.Empty, settings.Items[0].Note);
            Assert.Equal(1, settings.Items[0].Order);
        }

        [Fact]
        public void Normalize_UnknownGroup_MovesProductToDefault()
        {
            var settings = new ShopSettings
            {
                Groups = new List<ShopGroupConfig>
                {
                    new ShopGroupConfig { Id = "default", Name = "Default" }
                },
                Items = new List<ShopItemConfig>
                {
                    new ShopItemConfig { ItemId = 15, Group = "removed", Order = 1 }
                }
            };

            Assert.True(ShopConfiguration.Normalize(settings));
            Assert.Equal("default", settings.Items[0].Group);
        }

        [Fact]
        public void Normalize_BlankGroupName_UsesGroupId()
        {
            var settings = new ShopSettings
            {
                Groups = new List<ShopGroupConfig>
                {
                    new ShopGroupConfig { Id = "medical", Name = "   " }
                }
            };

            Assert.True(ShopConfiguration.Normalize(settings));
            Assert.Collection(settings.Groups,
                group =>
                {
                    Assert.Equal("default", group.Id);
                    Assert.Equal("default", group.Name);
                },
                group =>
                {
                    Assert.Equal("medical", group.Id);
                    Assert.Equal("medical", group.Name);
                });
        }

        [Fact]
        public void Normalize_DuplicateItemIds_KeepsOnlyFirstDefinition()
        {
            var settings = new ShopSettings
            {
                Items = new List<ShopItemConfig>
                {
                    new ShopItemConfig { ItemId = 15, SellPrice = 10m, Order = 1 },
                    new ShopItemConfig { ItemId = 15, SellPrice = 999m, Order = 2 }
                }
            };

            Assert.True(ShopConfiguration.Normalize(settings));
            var item = Assert.Single(settings.Items);
            Assert.Equal(10m, item.SellPrice);
        }

        [Fact]
        public void AvailableUnits_Item_UsesInventoryCount()
        {
            var counts = new Dictionary<ushort, int> { [15] = 7, [81] = 2 };
            var plain = ShopEntry.FromItem(new ShopItemConfig { ItemId = 15 });
            Assert.Equal(7, ShopService.AvailableUnits(plain, counts));
        }

        [Fact]
        public void PlayerButton_CarriesQuickChoicesAndConfirmation()
        {
            var choices = new[]
            {
                new PlayerPromptChoice("1", "1"),
                new PlayerPromptChoice("全部", "all")
            };
            var button = new PlayerButton(
                "sell", "出售", "danger", "选择数量", "",
                choices, "确认出售？");

            Assert.Equal(2, button.PromptChoices.Count);
            Assert.Equal("all", button.PromptChoices[1].Value);
            Assert.Equal("确认出售？", button.Confirmation);
        }
    }
}
