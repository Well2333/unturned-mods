using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Shop.Commands
{
    [Command("sell")]
    [CommandSyntax("<id> [amount]")]
    [CommandDescription("Sells an item by its game item id to the shop.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandSell : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly ShopTradeCoordinator m_Trades;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandSell(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            ShopTradeCoordinator trades,
            IItemDirectory itemDirectory,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_Trades = trades;
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

            if (entry.SellPrice <= 0m)
            {
                throw new UserFriendlyException(m_StringLocalizer["sell:not_sellable", new { name }]);
            }

            var result = await m_Trades.SellAsync(user, entry, amount);
            switch (result.Status)
            {
                case ShopTradeStatus.NotEnoughItems:
                    throw new UserFriendlyException(m_StringLocalizer["sell:not_enough_items", new { name }]);
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

            await PrintAsync(m_StringLocalizer["sell:success", new
            {
                amount = result.ItemCount,
                name,
                symbol = m_Trades.CurrencySymbol,
                total = result.Total
            }]);
        }
    }
}
