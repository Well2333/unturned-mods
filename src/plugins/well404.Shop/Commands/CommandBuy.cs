using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Shop.Commands
{
    [Command("buy")]
    [CommandSyntax("<id> [amount]")]
    [CommandDescription("Buys an item by its game item id from the shop.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandBuy : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly ShopTradeCoordinator m_Trades;
        private readonly DiscountService m_DiscountService;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandBuy(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            ShopTradeCoordinator trades,
            DiscountService discountService,
            IItemDirectory itemDirectory,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_Trades = trades;
            m_DiscountService = discountService;
            m_ItemDirectory = itemDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1)
            {
                throw new CommandWrongUsageException(Context);
            }

            var user = (UnturnedUser)Context.Actor;
            var id = Context.Parameters[0];
            var amountArgument = Context.Parameters.Length >= 2 ? Context.Parameters[1] : null;
            if (!ShopCommandAmount.TryParse(amountArgument, out var amount))
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:invalid_amount"]);
            }

            var entry = m_Catalog.Find(id);
            if (entry == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:item_not_found", new { id }]);
            }

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var name = ShopNames.DisplayName(entry, names);

            if (entry.BuyPrice <= 0m)
            {
                throw new UserFriendlyException(m_StringLocalizer["buy:not_buyable", new { name }]);
            }

            var multiplier = await m_DiscountService.GetMultiplierAsync(user);
            var unitPrice = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
            var result = await m_Trades.BuyAsync(user, entry, amount, unitPrice);
            switch (result.Status)
            {
                case ShopTradeStatus.InsufficientBalance:
                    throw new UserFriendlyException(m_StringLocalizer["buy:insufficient"]);
                case ShopTradeStatus.DurableEconomyRequired:
                    throw new UserFriendlyException(m_StringLocalizer["errors:durable_required"]);
                case ShopTradeStatus.PendingOperation:
                    throw new UserFriendlyException(m_StringLocalizer["errors:pending_operation", new { operation = result.OperationId }]);
                case ShopTradeStatus.Quarantined:
                    throw new UserFriendlyException(m_StringLocalizer["errors:quarantined", new { operation = result.OperationId }]);
                case ShopTradeStatus.Completed:
                    break;
                default:
                    throw new UserFriendlyException(m_StringLocalizer["errors:invalid_trade"]);
            }

            await PrintAsync(m_StringLocalizer["buy:success", new
            {
                amount = result.ItemCount,
                name,
                symbol = m_Trades.CurrencySymbol,
                total = result.Total
            }]);
        }
    }
}
