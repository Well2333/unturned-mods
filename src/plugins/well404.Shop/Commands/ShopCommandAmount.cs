using System.Globalization;

namespace well404.Shop.Commands
{
    public static class ShopCommandAmount
    {
        public static bool TryParse(string? value, out int amount)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                amount = 1;
                return true;
            }

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount)
                   && amount > 0;
        }
    }
}
