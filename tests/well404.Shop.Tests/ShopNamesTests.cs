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
        public void Combine_PrefersBilingualNameAndFallsBackToEnglish()
        {
            Assert.Equal("瓶装水 (Bottled Water)",
                ShopNames.Combine("Bottled Water", "瓶装水", null));
            Assert.Equal("Bottled Water",
                ShopNames.Combine("Bottled Water", null, "ignored"));
        }
    }
}
