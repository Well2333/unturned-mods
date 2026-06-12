using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Economy
{
    /// <summary>
    /// The player-facing wallet surface for the web panel: shows the player's balance and lets
    /// them transfer currency to another online player. Mirrors the <c>/pay</c> command logic
    /// (transfer toggle, minimum, tax, atomic withdraw + credit with refund on failure). Each
    /// online recipient is a card whose key is their Steam ID; the button prompts for the amount.
    /// Registered optionally via <see cref="IPlayerMenuRegistry"/>.
    /// </summary>
    public sealed class EconomyPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "economy";

        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IConfiguration m_Configuration;

        public EconomyPlayerMenu(
            IEconomyProvider economy,
            IUserManager userManager,
            IUnturnedUserDirectory userDirectory,
            IConfiguration configuration)
        {
            m_Economy = economy;
            m_UserManager = userManager;
            m_UserDirectory = userDirectory;
            m_Configuration = configuration;
        }

        public string Id => MenuId;

        public string Title => "钱包";

        public string? Icon => "💰";

        private TransferSettings Transfer =>
            (m_Configuration.Get<EconomySettings>() ?? new EconomySettings()).Transfer;

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var balance = await m_Economy.GetBalanceAsync(context.SteamId, KnownActorTypes.Player);
            var symbol = m_Economy.CurrencySymbol;
            var header = $"余额：{symbol}{Money(balance)} {m_Economy.CurrencyName}";

            var transfer = Transfer;
            var cards = new List<PlayerCard>();
            string? message = null;

            if (!transfer.Enabled)
            {
                message = "转账功能当前已关闭。";
                return new PlayerMenuView(Title, header, cards, message);
            }

            var taxNote = transfer.TaxPercent > 0m ? $"（手续费 {Money(transfer.TaxPercent)}%）" : string.Empty;
            var prompt = $"转账金额（最低 {symbol}{Money(transfer.MinAmount)}）";

            foreach (var online in m_UserDirectory.GetOnlineUsers())
            {
                if (string.Equals(online.Id, context.SteamId, StringComparison.Ordinal))
                {
                    continue;
                }

                cards.Add(new PlayerCard(
                    online.Id,
                    online.DisplayName,
                    new[] { "向该玩家转账" + taxNote },
                    null,
                    new[] { new PlayerButton("pay", "转账", "primary", prompt) }));
            }

            if (cards.Count == 0)
            {
                message = "当前没有其他在线玩家可转账。";
            }

            return new PlayerMenuView(Title, header, cards, message);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            if (actionId != "pay")
            {
                return PlayerActionResult.Fail("未知操作。");
            }

            var transfer = Transfer;
            if (!transfer.Enabled)
            {
                return PlayerActionResult.Fail("转账功能已关闭。");
            }

            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m)
            {
                return PlayerActionResult.Fail("请输入有效的金额。");
            }

            var symbol = m_Economy.CurrencySymbol;
            if (amount < transfer.MinAmount)
            {
                return PlayerActionResult.Fail($"单次转账至少 {symbol}{Money(transfer.MinAmount)}。");
            }

            if (string.Equals(cardKey, context.SteamId, StringComparison.Ordinal))
            {
                return PlayerActionResult.Fail("不能转账给自己。");
            }

            var target = await m_UserManager.FindUserAsync(
                KnownActorTypes.Player, cardKey, UserSearchMode.FindById) as UnturnedUser;
            var targetName = target?.DisplayName ?? cardKey;

            var tax = decimal.Round(amount * transfer.TaxPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var received = amount - tax;

            // Withdraw first; throws NotEnoughBalanceException if the sender can't afford it.
            try
            {
                await m_Economy.UpdateBalanceAsync(context.SteamId, KnownActorTypes.Player, -amount, "pay_out:" + cardKey);
            }
            catch (NotEnoughBalanceException)
            {
                return PlayerActionResult.Fail("余额不足。");
            }

            try
            {
                await m_Economy.UpdateBalanceAsync(cardKey, KnownActorTypes.Player, received, "pay_in:" + context.SteamId);
            }
            catch
            {
                // Refund the sender if crediting the receiver failed.
                await m_Economy.UpdateBalanceAsync(context.SteamId, KnownActorTypes.Player, amount, "pay_refund:" + cardKey);
                throw;
            }

            if (target != null)
            {
                try
                {
                    await target.PrintMessageAsync($"{context.DisplayName} 向你转账了 {symbol}{Money(received)}。");
                }
                catch
                {
                    // Notifying the recipient is best-effort; the transfer already succeeded.
                }
            }

            return PlayerActionResult.Ok(
                $"已向 {targetName} 转账 {symbol}{Money(amount)}（对方实收 {symbol}{Money(received)}，手续费 {symbol}{Money(tax)}）。");
        }

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
