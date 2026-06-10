using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;

namespace well404.Shop.Commands
{
    [Command("sell")]
    [CommandSyntax("<id> [amount]")]
    [CommandDescription("Sells an item or bundle to the shop.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandSell : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_ShopService;
        private readonly IEconomyProvider m_Economy;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandSell(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            ShopService shopService,
            IEconomyProvider economy,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_ShopService = shopService;
            m_Economy = economy;
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
            var amount = Context.Parameters.Length >= 2 ? await Context.Parameters.GetAsync<int>(1) : 1;

            if (amount <= 0)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:invalid_amount"]);
            }

            var entry = m_Catalog.Find(id);
            if (entry == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:item_not_found", new { id }]);
            }

            if (entry.SellPrice <= 0m)
            {
                throw new UserFriendlyException(m_StringLocalizer["sell:not_sellable", new { name = entry.Name }]);
            }

            var took = await m_ShopService.TryTakeAsync(user, entry, amount);
            if (!took)
            {
                throw new UserFriendlyException(m_StringLocalizer["sell:not_enough_items", new { name = entry.Name }]);
            }

            var total = entry.SellPrice * amount;
            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell:" + entry.Id);

            await PrintAsync(m_StringLocalizer["sell:success", new
            {
                amount,
                name = entry.Name,
                symbol = m_Economy.CurrencySymbol,
                total
            }]);
        }
    }
}
