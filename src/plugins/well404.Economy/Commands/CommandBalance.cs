using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;

namespace well404.Economy.Commands
{
    [Command("balance")]
    [CommandAlias("bal")]
    [CommandAlias("money")]
    [CommandSyntax("[player]")]
    [CommandDescription("Shows your balance, or another player's balance.")]
    public class CommandBalance : Command
    {
        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandBalance(
            IServiceProvider serviceProvider,
            IEconomyProvider economy,
            IUserManager userManager,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Economy = economy;
            m_UserManager = userManager;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length == 0)
            {
                var actor = Context.Actor as UnturnedUser;
                if (actor == null)
                {
                    throw new UserFriendlyException(m_StringLocalizer["errors:not_a_player"]);
                }

                var balance = await m_Economy.GetBalanceAsync(actor.Id, actor.Type);
                await PrintAsync(m_StringLocalizer["balance:self", new
                {
                    symbol = m_Economy.CurrencySymbol,
                    currency = m_Economy.CurrencyName,
                    balance
                }]);
                return;
            }

            var search = Context.Parameters[0];
            var target = await PlayerResolver.ResolveAsync(m_UserManager, search);
            if (target == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:player_not_found", new { player = search }]);
            }

            var targetBalance = await m_Economy.GetBalanceAsync(target.Id, KnownActorTypes.Player);
            await PrintAsync(m_StringLocalizer["balance:other", new
            {
                player = target.DisplayName,
                symbol = m_Economy.CurrencySymbol,
                currency = m_Economy.CurrencyName,
                balance = targetBalance
            }]);
        }
    }
}
