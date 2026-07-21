using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;

namespace well404.Economy.Commands
{
    [Command("pay")]
    [CommandSyntax("<player> <amount>")]
    [CommandDescription("Transfers currency to another player.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPay : Command
    {
        private readonly IEconomyProvider m_Economy;
        private readonly IUserManager m_UserManager;
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPay(
            IServiceProvider serviceProvider,
            IEconomyProvider economy,
            IUserManager userManager,
            IConfiguration configuration,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Economy = economy;
            m_UserManager = userManager;
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var transfer = (m_Configuration.Get<EconomySettings>() ?? new EconomySettings()).Transfer;
            if (!transfer.Enabled)
            {
                throw new UserFriendlyException(m_StringLocalizer["pay:disabled"]);
            }
            if (!(m_Economy is EconomyProvider economy) || !economy.SupportsAtomicTransfers)
            {
                throw new UserFriendlyException(m_StringLocalizer["pay:requires_database"]);
            }

            if (Context.Parameters.Length < 2)
            {
                throw new CommandWrongUsageException(Context);
            }

            var sender = (UnturnedUser)Context.Actor;
            var search = Context.Parameters[0];
            var amount = await Context.Parameters.GetAsync<decimal>(1);

            if (amount <= 0m)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:invalid_amount"]);
            }

            if (amount < transfer.MinAmount)
            {
                throw new UserFriendlyException(m_StringLocalizer["pay:min", new
                {
                    symbol = m_Economy.CurrencySymbol,
                    min = transfer.MinAmount
                }]);
            }

            var target = await PlayerResolver.ResolveAsync(m_UserManager, search);
            if (target == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:player_not_found", new { player = search }]);
            }

            if (string.Equals(target.Id, sender.Id, StringComparison.Ordinal))
            {
                throw new UserFriendlyException(m_StringLocalizer["pay:self"]);
            }

            var tax = decimal.Round(amount * transfer.TaxPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var received = amount - tax;

            await economy.TransferAsync(
                sender.Id, sender.Type,
                target.Id, KnownActorTypes.Player,
                amount, received,
                "pay:" + sender.Id + ":" + target.Id);

            await PrintAsync(m_StringLocalizer["pay:sent", new
            {
                symbol = m_Economy.CurrencySymbol,
                amount,
                tax,
                player = target.DisplayName
            }]);

            if (target.Online != null)
            {
                await target.Online.PrintMessageAsync(m_StringLocalizer["pay:received", new
                {
                    symbol = m_Economy.CurrencySymbol,
                    amount = received,
                    player = sender.DisplayName
                }]);
            }
        }
    }
}
