using well404.Shop.Commands;
using Xunit;

namespace well404.Shop.Tests
{
    public class ShopCommandAmountTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParse_OmittedAmount_DefaultsToOne(string? value)
        {
            var valid = ShopCommandAmount.TryParse(value!, out var amount);

            Assert.True(valid);
            Assert.Equal(1, amount);
        }

        [Theory]
        [InlineData("1", 1)]
        [InlineData("25", 25)]
        [InlineData("+3", 3)]
        public void TryParse_ValidAmount_UsesProvidedValue(string value, int expected)
        {
            var valid = ShopCommandAmount.TryParse(value, out var amount);

            Assert.True(valid);
            Assert.Equal(expected, amount);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1.5")]
        [InlineData("abc")]
        public void TryParse_InvalidAmount_IsRejected(string value)
        {
            var valid = ShopCommandAmount.TryParse(value, out _);

            Assert.False(valid);
        }
    }
}
