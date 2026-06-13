using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using System.Threading.Tasks;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.Economy
{
    /// <summary>
    /// The player-facing wallet surface for the web panel: shows the player's balance and lets them
    /// transfer currency to another online player (mirrors <c>/pay</c>). All web text is localized to
    /// the player's chosen language; the in-game "you received" notice uses the server's language.
    /// </summary>
    public sealed class EconomyPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "economy";

        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly IConfiguration m_Configuration;
        private readonly IWebTranslationRegistry m_Tr;
        private readonly IStringLocalizer m_StringLocalizer;

        public EconomyPlayerMenu(
            IEconomyProvider economy,
            IUserManager userManager,
            IUnturnedUserDirectory userDirectory,
            IConfiguration configuration,
            IWebTranslationRegistry translations,
            IStringLocalizer stringLocalizer)
        {
            m_Economy = economy;
            m_UserManager = userManager;
            m_UserDirectory = userDirectory;
            m_Configuration = configuration;
            m_Tr = translations;
            m_StringLocalizer = stringLocalizer;
        }

        public string Id => MenuId;

        public string Title => "Wallet";

        public string? Icon => "💰";

        private TransferSettings Transfer =>
            (m_Configuration.Get<EconomySettings>() ?? new EconomySettings()).Transfer;

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var balance = await m_Economy.GetBalanceAsync(context.SteamId, KnownActorTypes.Player);
            var symbol = m_Economy.CurrencySymbol;
            var header = m_Tr.Format(lang, "Balance: {0}{1} {2}", symbol, Money(balance), m_Economy.CurrencyName);

            var transfer = Transfer;
            var cards = new List<PlayerCard>();
            if (!transfer.Enabled)
            {
                return new PlayerMenuView(m_Tr.Resolve("Wallet", lang), header, cards,
                    m_Tr.Resolve("Transfers are currently disabled.", lang));
            }

            var line = transfer.TaxPercent > 0m
                ? m_Tr.Format(lang, "Send to this player (fee {0}%)", Money(transfer.TaxPercent))
                : m_Tr.Resolve("Send to this player", lang);
            var prompt = m_Tr.Format(lang, "Amount to transfer (min {0}{1})", symbol, Money(transfer.MinAmount));
            var buttonLabel = m_Tr.Resolve("Transfer", lang);

            foreach (var online in m_UserDirectory.GetOnlineUsers())
            {
                if (string.Equals(online.Id, context.SteamId, StringComparison.Ordinal))
                {
                    continue;
                }

                cards.Add(new PlayerCard(online.Id, online.DisplayName, new[] { line }, null,
                    new[] { new PlayerButton("pay", buttonLabel, "primary", prompt) }));
            }

            var message = cards.Count == 0 ? m_Tr.Resolve("No other players are online to transfer to.", lang) : null;
            return new PlayerMenuView(m_Tr.Resolve("Wallet", lang), header, cards, message);
        }

        public async Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
        {
            var lang = context.Language;
            if (actionId != "pay")
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Unknown action.", lang));
            }

            var transfer = Transfer;
            if (!transfer.Enabled)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Transfers are currently disabled.", lang));
            }

            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0m)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Enter a valid amount.", lang));
            }

            var symbol = m_Economy.CurrencySymbol;
            if (amount < transfer.MinAmount)
            {
                return PlayerActionResult.Fail(m_Tr.Format(lang, "Minimum transfer is {0}{1}.", symbol, Money(transfer.MinAmount)));
            }

            if (string.Equals(cardKey, context.SteamId, StringComparison.Ordinal))
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("You can't transfer to yourself.", lang));
            }

            var target = await m_UserManager.FindUserAsync(
                KnownActorTypes.Player, cardKey, UserSearchMode.FindById) as UnturnedUser;
            var targetName = target?.DisplayName ?? cardKey;

            var tax = decimal.Round(amount * transfer.TaxPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var received = amount - tax;

            try
            {
                await m_Economy.UpdateBalanceAsync(context.SteamId, KnownActorTypes.Player, -amount, "pay_out:" + cardKey);
            }
            catch (NotEnoughBalanceException)
            {
                return PlayerActionResult.Fail(m_Tr.Resolve("Insufficient balance.", lang));
            }

            try
            {
                await m_Economy.UpdateBalanceAsync(cardKey, KnownActorTypes.Player, received, "pay_in:" + context.SteamId);
            }
            catch
            {
                await m_Economy.UpdateBalanceAsync(context.SteamId, KnownActorTypes.Player, amount, "pay_refund:" + cardKey);
                throw;
            }

            if (target != null)
            {
                try
                {
                    // In-game notice to the recipient uses the server's configured language.
                    await target.PrintMessageAsync(m_StringLocalizer["pay:received", new
                    {
                        symbol,
                        amount = received,
                        player = context.DisplayName
                    }]);
                }
                catch
                {
                    // Best-effort; the transfer already succeeded.
                }
            }

            return PlayerActionResult.Ok(m_Tr.Format(lang, "Transferred {0} to {1}. They received {2} (fee {3}).",
                symbol + Money(amount), targetName, symbol + Money(received), symbol + Money(tax)));
        }

        private static string Money(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
