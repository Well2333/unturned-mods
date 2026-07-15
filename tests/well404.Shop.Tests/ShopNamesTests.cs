using UnturnedMods.Shared.Items;
using well404.Shop;
using Xunit;

namespace well404.Shop.Tests
{
    public class ShopNamesTests
    {
        [Theory]
        [InlineData("Name 瓶装水", "瓶装水")]
        [InlineData("\uFEFFName \"军用弹匣\" // translated", "军用弹匣")]
        [InlineData("Description example\nName 维生素\n", "维生素")]
        public void ParseName_ReadsUnturnedLocalizationName(string input, string expected)
            => Assert.Equal(expected, ShopNames.ParseName(input));

        [Fact]
        public void DisplayName_UsesBilingualChineseAndEnglishOnlyForEnglishUi()
        {
            var item = new LocalizedItemInfo("Bottled Water", "瓶装水", false);

            Assert.Equal("瓶装水\nBottled Water", item.DisplayName("zh"));
            Assert.Equal("Bottled Water", item.DisplayName("en"));
        }
    }
}
