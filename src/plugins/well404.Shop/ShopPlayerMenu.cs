using System;
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
    /// sell as themselves (mirrors <c>/buy</c> and <c>/sell</c>). Items show their resolved game name
    /// and item id badge, driven entirely by the generic player-menu model. All text is
    /// localized to the player's chosen language.
    /// </summary>
    public sealed class ShopPlayerMenu : IPlayerMenu, IPlayerMenuUiProvider
    {
        public const string MenuId = "shop";
        private static readonly WebUiExtension s_Ui = WebUiExtension.FromEmbeddedResources(
            typeof(ShopPlayerMenu).Assembly, "player-ui.html", "player-ui.css", "player-ui.js");

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

        public WebUiExtension Ui => s_Ui;

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var balance = await m_Economy.GetBalanceAsync(context.SteamId, KnownActorTypes.Player);
            var user = await ResolveOnlineAsync(context.SteamId);
            var multiplier = user != null ? await m_DiscountService.GetMultiplierAsync(user) : 1m;
            var symbol = m_Economy.CurrencySymbol;
            IReadOnlyDictionary<ushort, int> inventory = new Dictionary<ushort, int>();
            if (user != null)
            {
                inventory = await m_ShopService.GetInventoryCountsAsync(user);
            }

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var cards = new List<PlayerCard>();
            foreach (var group in m_Catalog.Groups)
            {
                var buttons = new List<PlayerButton>
                {
                    new PlayerButton("sell_group",
                        m_Tr.Resolve("Sell all in this group", lang), "danger",
                        null, null, null, m_Tr.Resolve(
                            "Sell every sellable item in this group? This cannot be undone.", lang))
                };
                cards.Add(new PlayerCard("group:" + group.Id, string.Empty,
                    null, null, buttons, group.Name, null, null,
                    "group-header", group.Id));
            }

            foreach (var entry in m_Catalog.Entries)
            {
                var available = ShopService.AvailableUnits(entry, inventory);
                var lines = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry.Note))
                {
                    lines.Add(entry.Note);
                }
                lines.Add(m_Tr.Format(lang, "In inventory: {0}", available));
                var buttons = new List<PlayerButton>();
                if (entry.BuyPrice > 0m)
                {
                    var unit = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
                    buttons.Add(new PlayerButton("buy",
                        m_Tr.Resolve("Buy", lang) + " " + symbol + Money(unit),
                        "success", m_Tr.Resolve("Choose purchase quantity", lang), "",
                        QuantityChoices(lang), null));
                }

                if (entry.SellPrice > 0m)
                {
                    buttons.Add(new PlayerButton("sell",
                        m_Tr.Resolve("Sell", lang) + " " + symbol + Money(entry.SellPrice),
                        null, m_Tr.Resolve("Choose sale quantity", lang),
                        "", QuantityChoices(lang), null));
                }

                var groupName = GroupName(entry.Group);
                cards.Add(new PlayerCard(entry.Id, ShopNames.NameOf(entry.ItemId, names),
                    lines, null, buttons, groupName,
                    "#" + entry.ItemId.ToString(CultureInfo.InvariantCulture),
                    null, null, entry.Group));
            }

            var header = m_Tr.Format(lang, "Balance: {0}{1}", symbol, Money(balance));
            var message = user == null ? m_Tr.Resolve("You must be online to buy or sell.", lang) : null;
            return new PlayerMenuView(m_Tr.Resolve("Shop", lang), header, cards, message, null, "tabbed-grid");
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You must be online to trade.", lang));
            }
            if (actionId == "sell_group" && cardKey.StartsWith("group:", StringComparison.Ordinal))
            {
                return await SellGroupAsync(user, cardKey.Substring(6), lang);
            }

            var entry = m_Catalog.Find(cardKey);
            if (entry == null)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Item not found.", lang));
            }

            var all = string.Equals(value, "all", StringComparison.OrdinalIgnoreCase);
            var parsed = 0;
            if (!all && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed <= 0))
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid quantity.", lang));
            }

            var names = await ShopNames.BuildMapAsync(m_ItemDirectory);
            var name = ShopNames.DisplayName(entry, names);
            switch (actionId)
            {
                case "buy": return await BuyAsync(user, entry, name, all ? (int?)null : parsed, lang);
                case "sell": return await SellAsync(user, entry, name, all ? (int?)null : parsed, lang);
                default: return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }
        }

        private async Task<PlayerActionResult> BuyAsync(UnturnedUser user, ShopEntry entry, string name, int? requested, string lang)
        {
            if (entry.BuyPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not buyable.", name));
            }

            var multiplier = await m_DiscountService.GetMultiplierAsync(user);
            var unitPrice = DiscountService.ApplyDiscount(entry.BuyPrice, multiplier);
            var amount = requested ?? 0;
            if (requested == null)
            {
                var balance = await m_Economy.GetBalanceAsync(user.Id, user.Type);
                var affordable = decimal.Floor(balance / unitPrice);
                amount = affordable > int.MaxValue ? int.MaxValue : (int)affordable;
                if (amount < 1)
                {
                    return PlayerActionResult.Fail(m_Tr.Resolve("Insufficient balance.", lang));
                }
            }
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

        private async Task<PlayerActionResult> SellAsync(UnturnedUser user, ShopEntry entry, string name, int? requested, string lang)
        {
            if (entry.SellPrice <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "{0} is not sellable.", name));
            }

            var inventory = await m_ShopService.GetInventoryCountsAsync(user);
            var available = ShopService.AvailableUnits(entry, inventory);
            var amount = requested == null ? available : Math.Min(requested.Value, available);
            if (amount < 1)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "You don't have enough {0} in your inventory.", name));
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

        private async Task<PlayerActionResult> SellGroupAsync(UnturnedUser user, string groupId, string lang)
        {
            var inGroup = new List<ShopEntry>();
            foreach (var entry in m_Catalog.Entries)
            {
                if (string.Equals(entry.Group, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    inGroup.Add(entry);
                }
            }
            var eligible = ShopQuickSell.EligibleEntries(inGroup);
            if (eligible.Count == 0)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("This group has no items eligible for quick sell.", lang));
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

            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell_group:" + groupId);
            return PlayerActionResult.Ok(m_Tr.Format(lang, "Sold {0} item(s) for {1}.",
                itemCount, m_Economy.CurrencySymbol + Money(total)));
        }

        private string GroupName(string groupId)
        {
            foreach (var group in m_Catalog.Groups)
            {
                if (string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(group.Name) ? group.Id : group.Name;
                }
            }
            return groupId;
        }

        private IReadOnlyList<PlayerPromptChoice> QuantityChoices(string lang)
            => new[]
            {
                new PlayerPromptChoice("1", "1"),
                new PlayerPromptChoice("5", "5"),
                new PlayerPromptChoice("10", "10"),
                new PlayerPromptChoice(m_Tr.Resolve("All", lang), "all")
            };

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
