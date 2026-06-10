using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Economy.Abstractions;

namespace well404.Shop.Commands
{
    [Command("shop")]
    [CommandAlias("market")]
    [CommandDescription("Lists items available in the shop.")]
    public class CommandShop : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly IEconomyProvider m_Economy;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandShop(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            IEconomyProvider economy,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_Economy = economy;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var items = m_Catalog.Items;
            if (items.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["shop:empty"]);
                return;
            }

            await PrintAsync(m_StringLocalizer["shop:header"]);
            foreach (var entry in items)
            {
                var line = new StringBuilder();
                line.Append(entry.Id).Append(" - ").Append(entry.Name);
                if (entry.BuyPrice > 0m)
                {
                    line.Append(" | ").Append(m_StringLocalizer["shop:buy_label"])
                        .Append(' ').Append(m_Economy.CurrencySymbol).Append(entry.BuyPrice);
                }

                if (entry.SellPrice > 0m)
                {
                    line.Append(" | ").Append(m_StringLocalizer["shop:sell_label"])
                        .Append(' ').Append(m_Economy.CurrencySymbol).Append(entry.SellPrice);
                }

                await PrintAsync(line.ToString());
            }
        }
    }
}
