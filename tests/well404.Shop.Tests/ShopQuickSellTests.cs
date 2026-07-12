using System.Collections.Generic;
using well404.Shop;
using Xunit;

namespace well404.Shop.Tests
{
    public class ShopQuickSellTests
    {
        [Fact]
        public void CreateCard_RendersOneClickSellButtonWithoutPrompt()
        {
            var card = ShopQuickSell.CreateCard(text => text);

            Assert.Equal(ShopQuickSell.CardKey, card.Key);
            var button = Assert.Single(card.Buttons);
            Assert.Equal(ShopQuickSell.ActionId, button.ActionId);
            Assert.Equal("Sell all", button.Label);
            Assert.Equal("danger", button.Style);
            Assert.Null(button.PromptLabel);
            Assert.Null(button.PromptDefault);
        }

        [Fact]
        public void EligibleEntries_IncludesOnlySellablePlainItems()
        {
            var entries = new[]
            {
                ShopEntry.FromItem(new ShopItemConfig { ItemId = 15, SellPrice = 40m }),
                ShopEntry.FromItem(new ShopItemConfig { ItemId = 81, SellPrice = 0m }),
                ShopEntry.FromBundle(new ShopBundleConfig
                {
                    Id = "kit", Name = "Kit", SellPrice = 100m,
                    Contents = new List<BundleItem> { new BundleItem { ItemId = 15, Amount = 1 } }
                })
            };
            var eligible = ShopQuickSell.EligibleEntries(entries);
            Assert.Single(eligible);
            Assert.True(eligible.ContainsKey(15));
            Assert.False(eligible.ContainsKey(81));
        }

        [Fact]
        public void CalculateTotal_SumsRemovedAmountsAtConfiguredSellPrices()
        {
            var eligible = ShopQuickSell.EligibleEntries(new[]
            {
                ShopEntry.FromItem(new ShopItemConfig { ItemId = 15, SellPrice = 40m }),
                ShopEntry.FromItem(new ShopItemConfig { ItemId = 81, SellPrice = 12.5m })
            });
            IReadOnlyDictionary<ushort, int> removed = new Dictionary<ushort, int>
            {
                [15] = 2, [81] = 3, [99] = 10
            };
            Assert.Equal(117.5m, ShopQuickSell.CalculateTotal(eligible, removed));
        }
    }
}
