using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Extensions.Games.Abstractions.Items;

namespace well404.Shop.Commands
{
    [Command("shop")]
    [CommandAlias("market")]
    [CommandDescription("Lists items available in the shop.")]
    public class CommandShop : Command
    {
        private readonly ShopCatalog m_Catalog;
        private readonly IEconomyProvider m_Economy;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandShop(
            IServiceProvider serviceProvider,
            ShopCatalog catalog,
            IEconomyProvider economy,
            IItemDirectory itemDirectory,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Catalog = catalog;
            m_Economy = economy;
            m_ItemDirectory = itemDirectory;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var entries = m_Catalog.Entries;
            if (entries.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["shop:empty"]);
                return;
            }

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);

            await PrintAsync(m_StringLocalizer["shop:header"]);
            foreach (var entry in entries)
            {
                var line = new StringBuilder();
                line.Append(entry.Id).Append(" - ").Append(ShopNames.DisplayName(entry, names));
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
