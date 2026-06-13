using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Shop
{
    /// <summary>
    /// The player-facing shop surface for the web panel: lists the catalog and lets a player buy and
    /// sell as themselves (mirrors <c>/buy</c> and <c>/sell</c>). All text is localized to the
    /// player's chosen language.
    /// </summary>
    public sealed class ShopPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "shop";

        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_ShopService;
        private readonly DiscountService m_DiscountService;
        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IWebTranslationRegistry m_Tr;

        public ShopPlayerMenu(
            ShopCatalog catalog,
            ShopService shopService,
            DiscountService discountService,
            IEconomyProvider economy,
            IUserManager userManager,
            IWebTranslationRegistry translations)
        {
            m_Catalog = catalog;
            m_ShopService = shopService;
            m_DiscountService = discountService;
            m_Economy = economy;
            m_UserManager = userManager;
            m_Tr = translations;
        }

        public string Id => MenuId;

        public string Title => "Shop";

        public string? Icon => "🛒";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var balance = await m_Economy.GetBalanceAsync(context.SteamId, KnownActorTypes.Player);
            var user = await ResolveOnlineAsync(context.SteamId);
            var multiplier = user != null ? await m_DiscountService.GetMultiplierAsync(user) : 1m;
            var symbol = m_Economy.CurrencySymbol;

            var cards = new List<PlayerCard>();
            foreach (var entry in m_Catalog.Items)
            {
                var lines = new List<string>();
                var buttons = new List<PlayerButton>();

                if (entry.BuyPrice > 0m)
                {
                    var unit = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
                    lines.Add(multiplier < 1m
                        ? m_Tr.Format(lang, "Buy price: {0}{1} each (was {0}{2})", symbol, Money(unit), Money(entry.BuyPrice))
                        : m_Tr.Format(lang, "Buy price: {0}{1} each", symbol, Money(unit)));
                    buttons.Add(new PlayerButton("buy", m_Tr.Resolve("Buy", lang), "primary", m_Tr.Resolve("Quantity to buy", lang)));
                }

                if (entry.SellPrice > 0m)
                {
                    lines.Add(m_Tr.Format(lang, "Sell price: {0}{1} each", symbol, Money(entry.SellPrice)));
                    buttons.Add(new PlayerButton("sell", m_Tr.Resolve("Sell", lang), promptLabel: m_Tr.Resolve("Quantity to sell", lang)));
                }

                var tags = new List<string>();
                if (entry.IsBundle)
                {
                    tags.Add(m_Tr.Resolve("Bundle", lang));
                    foreach (var content in entry.Contents)
                    {
                        tags.Add($"#{content.ItemId}×{content.Amount}");
                    }
                }
                else
                {
                    tags.Add($"#{entry.ItemId}×{entry.Amount}");
                }

                cards.Add(new PlayerCard(entry.Id, entry.Name, lines, tags, buttons));
            }

            var header = m_Tr.Format(lang, "Balance: {0}{1}", symbol, Money(balance));
            var message = user == null ? m_Tr.Resolve("You must be online to buy or sell.", lang) : null;
            return new PlayerMenuView(m_Tr.Resolve("Shop", lang), header, cards, message);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            var entry = m_Catalog.Find(cardKey);
            if (entry == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid quantity.", lang));
            }

            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to trade.", lang));
            }

            switch (actionId)
            {
                case "buy": return await BuyAsync(user, entry, amount, lang);
                case "sell": return await SellAsync(user, entry, amount, lang);
                default: return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }
        }

        private async Task<PlayerActionResult> BuyAsync(UnturnedUser user, ShopEntry entry, int amount, string lang)
        {
            if (entry.BuyPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not buyable.", entry.Name));
            }

            var multiplier = await m_DiscountService.GetMultiplierAsync(user);
            var unitPrice = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
            var total = unitPrice * amount;

            try
            {
                await m_Economy.UpdateBalanceAsync(user.Id, user.Type, -total, "shop_buy:" + entry.Id);
            }
            catch (NotEnoughBalanceException)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Insufficient balance.", lang));
            }

            try
            {
                await m_ShopService.GiveAsync(user, entry, amount);
            }
            catch
            {
                await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_buy_refund:" + entry.Id);
                throw;
            }

            return PlayerActionResult.Ok(m_Tr.Format(lang, "Bought {0}× {1} for {2}.",
                amount, entry.Name, m_Economy.CurrencySymbol + Money(total)));
        }

        private async Task<PlayerActionResult> SellAsync(UnturnedUser user, ShopEntry entry, int amount, string lang)
        {
            if (entry.SellPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not sellable.", entry.Name));
            }

            var took = await m_ShopService.TryTakeAsync(user, entry, amount);
            if (!took)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "You don't have enough {0} in your inventory.", entry.Name));
            }

            var total = entry.SellPrice * amount;
            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell:" + entry.Id);
            return PlayerActionResult.Ok(m_Tr.Format(lang, "Sold {0}× {1} for {2}.",
                amount, entry.Name, m_Economy.CurrencySymbol + Money(total)));
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
