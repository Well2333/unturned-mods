using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Shop.Commands
{
    [Command("buy")]
    [CommandSyntax("<id> [amount]")]
    [CommandDescription("Buys an item (by its item id) or a bundle (by its id) from the shop.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandBuy : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_ShopService;
        private readonly DiscountService m_DiscountService;
        private readonly IEconomyProvider m_Economy;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandBuy(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            ShopService shopService,
            DiscountService discountService,
            IEconomyProvider economy,
            IItemDirectory itemDirectory,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_ShopService = shopService;
            m_DiscountService = discountService;
            m_Economy = economy;
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

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var name = ShopNames.DisplayName(entry, names);

            if (entry.BuyPrice <= 0m)
            {
                throw new UserFriendlyException(m_StringLocalizer["buy:not_buyable", new { name }]);
            }

            var multiplier = await m_DiscountService.GetMultiplierAsync(user);
            var unitPrice = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
            var total = unitPrice * amount;

            // UpdateBalanceAsync throws NotEnoughBalanceException (user-friendly) if unaffordable.
            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, -total, "shop_buy:" + entry.Id);

            try
            {
                await m_ShopService.GiveAsync(user, entry, amount);
            }
            catch
            {
                await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_buy_refund:" + entry.Id);
                throw;
            }

            await PrintAsync(m_StringLocalizer["buy:success", new
            {
                amount,
                name,
                symbol = m_Economy.CurrencySymbol,
                total
            }]);
        }
    }
}
