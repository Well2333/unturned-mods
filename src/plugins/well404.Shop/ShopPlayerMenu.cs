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
    /// The player-facing shop surface for the web panel: lists the catalog as cards and lets a
    /// player buy and sell as themselves. Mirrors the <c>/buy</c> and <c>/sell</c> command logic
    /// (discount, atomic charge + refund on failure). Registered optionally via
    /// <see cref="IPlayerMenuRegistry"/>, so the shop works with or without the panel installed.
    /// </summary>
    public sealed class ShopPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "shop";

        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_ShopService;
        private readonly DiscountService m_DiscountService;
        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;

        public ShopPlayerMenu(
            ShopCatalog catalog,
            ShopService shopService,
            DiscountService discountService,
            IEconomyProvider economy,
            IUserManager userManager)
        {
            m_Catalog = catalog;
            m_ShopService = shopService;
            m_DiscountService = discountService;
            m_Economy = economy;
            m_UserManager = userManager;
        }

        public string Id => MenuId;

        public string Title => "商店";

        public string? Icon => "🛒";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
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
                        ? $"买价：{symbol}{Money(unit)} / 个（原价 {symbol}{Money(entry.BuyPrice)}）"
                        : $"买价：{symbol}{Money(unit)} / 个");
                    buttons.Add(new PlayerButton("buy", "购买", "primary", "购买数量"));
                }

                if (entry.SellPrice > 0m)
                {
                    lines.Add($"卖价：{symbol}{Money(entry.SellPrice)} / 个");
                    buttons.Add(new PlayerButton("sell", "出售", promptLabel: "出售数量"));
                }

                var tags = new List<string>();
                if (entry.IsBundle)
                {
                    tags.Add("礼包");
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

            var header = $"余额：{symbol}{Money(balance)}";
            var message = user == null ? "你需要在线才能购买或出售物品。" : null;
            return new PlayerMenuView(Title, header, cards, message);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var entry = m_Catalog.Find(cardKey);
            if (entry == null)
            {
                return PlayerActionResult.Fail("找不到该商品。");
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
            {
                return PlayerActionResult.Fail("请输入有效的数量。");
            }

            var user = await ResolveOnlineAsync(context.SteamId);
            if (user == null)
            {
                return PlayerActionResult.Fail("你需要在线才能交易。");
            }

            switch (actionId)
            {
                case "buy": return await BuyAsync(user, entry, amount);
                case "sell": return await SellAsync(user, entry, amount);
                default: return PlayerActionResult.Fail("未知操作。");
            }
        }

        private async Task<PlayerActionResult> BuyAsync(UnturnedUser user, ShopEntry entry, int amount)
        {
            if (entry.BuyPrice <= 0m)
            {
                return PlayerActionResult.Fail($"{entry.Name} 不可购买。");
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
                return PlayerActionResult.Fail("余额不足。");
            }

            try
            {
                await m_ShopService.GiveAsync(user, entry, amount);
            }
            catch
            {
                // Refund on failure, mirroring CommandBuy.
                await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_buy_refund:" + entry.Id);
                throw;
            }

            return PlayerActionResult.Ok($"已购买 {amount}× {entry.Name}，花费 {m_Economy.CurrencySymbol}{Money(total)}。");
        }

        private async Task<PlayerActionResult> SellAsync(UnturnedUser user, ShopEntry entry, int amount)
        {
            if (entry.SellPrice <= 0m)
            {
                return PlayerActionResult.Fail($"{entry.Name} 不可出售。");
            }

            var took = await m_ShopService.TryTakeAsync(user, entry, amount);
            if (!took)
            {
                return PlayerActionResult.Fail($"你的背包里没有足够的 {entry.Name}。");
            }

            var total = entry.SellPrice * amount;
            await m_Economy.UpdateBalanceAsync(user.Id, user.Type, total, "shop_sell:" + entry.Id);
            return PlayerActionResult.Ok($"已出售 {amount}× {entry.Name}，获得 {m_Economy.CurrencySymbol}{Money(total)}。");
        }

        private async Task<UnturnedUser?> ResolveOnlineAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
