using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Shop
{
    /// <summary>
    /// The player-facing shop surface for the web panel: lists the catalog and lets a player buy and
    /// sell as themselves (mirrors <c>/buy</c> and <c>/sell</c>). It renders as a compact list with
    /// two sections — plain items (shown by their resolved name, with the item id as a badge) and
    /// bundles (name + content pills) — driven entirely by the generic player-menu model. All text is
    /// localized to the player's chosen language.
    /// </summary>
    public sealed class ShopPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "shop";

        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_ShopService;
        private readonly DiscountService m_DiscountService;
        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IItemDirectory m_ItemDirectory;
        private readonly IWebTranslationRegistry m_Tr;

        public ShopPlayerMenu(
            ShopCatalog catalog,
            ShopService shopService,
            DiscountService discountService,
            IEconomyProvider economy,
            IUserManager userManager,
            IItemDirectory itemDirectory,
            IWebTranslationRegistry translations)
        {
            m_Catalog = catalog;
            m_ShopService = shopService;
            m_DiscountService = discountService;
            m_Economy = economy;
            m_UserManager = userManager;
            m_ItemDirectory = itemDirectory;
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

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var itemsGroup = m_Tr.Resolve("Items", lang);
            var bundlesGroup = m_Tr.Resolve("Bundles", lang);

            var cards = new List<PlayerCard>();
            var quickSellEntries = ShopQuickSell.EligibleEntries(m_Catalog.Entries);
            if (quickSellEntries.Count > 0)
            {
                cards.Add(ShopQuickSell.CreateCard(text => m_Tr.Resolve(text, lang)));
            }

            foreach (var entry in m_Catalog.Entries)
            {
                var buttons = new List<PlayerButton>();
                if (entry.BuyPrice > 0m)
                {
                    var unit = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
                    buttons.Add(new PlayerButton("buy",
                        m_Tr.Resolve("Buy", lang) + " " + symbol + Money(unit),
                        "success", m_Tr.Resolve("Quantity to buy", lang)));
                }

                if (entry.SellPrice > 0m)
                {
                    buttons.Add(new PlayerButton("sell",
                        m_Tr.Resolve("Sell", lang) + " " + symbol + Money(entry.SellPrice),
                        promptLabel: m_Tr.Resolve("Quantity to sell", lang)));
                }

                if (entry.IsBundle)
                {
                    var tags = new List<string>();
                    foreach (var content in entry.Contents)
                    {
                        tags.Add(ShopNames.Label(content.ItemId, content.Amount, names));
                    }

                    cards.Add(new PlayerCard(entry.Id, entry.BundleName, null, tags, buttons, bundlesGroup));
                }
                else
                {
                    cards.Add(new PlayerCard(entry.Id, ShopNames.NameOf(entry.ItemId, names),
                        null, null, buttons, itemsGroup, "#" + entry.ItemId.ToString(CultureInfo.InvariantCulture)));
                }
            }

            var header = m_Tr.Format(lang, "Balance: {0}{1}", symbol, Money(balance));
            var message = user == null ? m_Tr.Resolve("You must be online to buy or sell.", lang) : null;
            return new PlayerMenuView(m_Tr.Resolve("Shop", lang), header, cards, message, null, "list");
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            if (actionId == ShopQuickSell.ActionId && cardKey == ShopQuickSell.CardKey)
            {
                var quickSellUser = await ResolveOnlineAsync(context.SteamId);
                if (quickSellUser == null)
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to trade.", lang));
                }
                return await SellAllAsync(quickSellUser, lang);
            }

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

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var name = ShopNames.DisplayName(entry, names);

            switch (actionId)
            {
                case "buy": return await BuyAsync(user, entry, name, amount, lang);
                case "sell": return await SellAsync(user, entry, name, amount, lang);
                default: return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }
        }

        private async Task<PlayerActionResult> BuyAsync(UnturnedUser user, ShopEntry entry, string name, int amount, string lang)
        {
            if (entry.BuyPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not buyable.", name));
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
                amount, name, m_Economy.CurrencySymbol + Money(total)));
        }

        private async Task<PlayerActionResult> SellAsync(UnturnedUser user, ShopEntry entry, string name, int amount, string lang)
        {
            if (entry.SellPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not sellable.", name));
            }

            var took = await m_ShopService.TryTakeAsync(user, entry, amount);
            if (!took)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "You don't have enough {0} in your inventory.", name));
            }

            var total = entry.SellPrice * amount;
            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell:" + entry.Id);
            return PlayerActionResult.Ok(m_Tr.Format(lang, "Sold {0}× {1} for {2}.",
                amount, name, m_Economy.CurrencySymbol + Money(total)));
        }

        private async Task<PlayerActionResult> SellAllAsync(UnturnedUser user, string lang)
        {
            var eligible = ShopQuickSell.EligibleEntries(m_Catalog.Entries);
            if (eligible.Count == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("The shop has no items eligible for quick sell.", lang));
            }

            var removed = await m_ShopService.TakeAllAsync(user, eligible.Keys);
            var total = ShopQuickSell.CalculateTotal(eligible, removed);
            var itemCount = 0;
            foreach (var amount in removed.Values)
            {
                itemCount += amount;
            }
            if (itemCount == 0 || total <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("No sellable items were found in your inventory.", lang));
            }

            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell_all");
            return PlayerActionResult.Ok(m_Tr.Format(lang, "Sold {0} item(s) for {1}.",
                itemCount, m_Economy.CurrencySymbol + Money(total)));
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
