using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;

namespace well404.Economy.Commands
{
    [Command("eco")]
    [CommandDescription("Economy administration commands.")]
    public class CommandEco : Command
    {
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandEco(IServiceProvider serviceProvider, IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_StringLocalizer = stringLocalizer;
        }

        protected override Task OnExecuteAsync()
        {
            throw new CommandWrongUsageException(Context);
        }
    }

    /// <summary>Shared resolution + parsing for the eco admin subcommands.</summary>
    public abstract class CommandEcoBase : Command
    {
        protected readonly IEconomyProvider Economy;
        protected readonly IUserManager UserManager;
        protected readonly IStringLocalizer StringLocalizer;

        protected CommandEcoBase(
            IServiceProvider serviceProvider,
            IEconomyProvider economy,
            IUserManager userManager,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            Economy = economy;
            UserManager = userManager;
            StringLocalizer = stringLocalizer;
        }

        protected async Task<(ResolvedPlayer target, decimal amount)> ResolveTargetAndAmountAsync()
        {
            if (Context.Parameters.Length < 2)
            {
                throw new CommandWrongUsageException(Context);
            }

            var search = Context.Parameters[0];
            var amount = await Context.Parameters.GetAsync<decimal>(1);
            if (amount <= 0m)
            {
                throw new UserFriendlyException(StringLocalizer["errors:invalid_amount"]);
            }

            var target = await PlayerResolver.ResolveAsync(UserManager, search);
            if (target == null)
            {
                throw new UserFriendlyException(StringLocalizer["errors:player_not_found", new { player = search }]);
            }

            return (target, amount);
        }
    }

    [Command("give")]
    [CommandParent(typeof(CommandEco))]
    [CommandSyntax("<player> <amount>")]
    [CommandDescription("Gives currency to a player.")]
    public class CommandEcoGive : CommandEcoBase
    {
        public CommandEcoGive(IServiceProvider sp, IEconomyProvider e, IUserManager u, IStringLocalizer l)
            : base(sp, e, u, l) { }

        protected override async Task OnExecuteAsync()
        {
            var (target, amount) = await ResolveTargetAndAmountAsync();
            var balance = await Economy.UpdateBalanceAsync(target.Id, KnownActorTypes.Player, amount, "admin_give");
            await PrintAsync(StringLocalizer["eco:give", new
            {
                symbol = Economy.CurrencySymbol, amount, player = target.DisplayName, balance
            }]);
        }
    }

    [Command("take")]
    [CommandParent(typeof(CommandEco))]
    [CommandSyntax("<player> <amount>")]
    [CommandDescription("Takes currency from a player.")]
    public class CommandEcoTake : CommandEcoBase
    {
        public CommandEcoTake(IServiceProvider sp, IEconomyProvider e, IUserManager u, IStringLocalizer l)
            : base(sp, e, u, l) { }

        protected override async Task OnExecuteAsync()
        {
            var (target, amount) = await ResolveTargetAndAmountAsync();
            var balance = await Economy.UpdateBalanceAsync(target.Id, KnownActorTypes.Player, -amount, "admin_take");
            await PrintAsync(StringLocalizer["eco:take", new
            {
                symbol = Economy.CurrencySymbol, amount, player = target.DisplayName, balance
            }]);
        }
    }

    [Command("set")]
    [CommandParent(typeof(CommandEco))]
    [CommandSyntax("<player> <amount>")]
    [CommandDescription("Sets a player's balance.")]
    public class CommandEcoSet : CommandEcoBase
    {
        public CommandEcoSet(IServiceProvider sp, IEconomyProvider e, IUserManager u, IStringLocalizer l)
            : base(sp, e, u, l) { }

        protected override async Task OnExecuteAsync()
        {
            var (target, amount) = await ResolveTargetAndAmountAsync();
            await Economy.SetBalanceAsync(target.Id, KnownActorTypes.Player, amount);
            await PrintAsync(StringLocalizer["eco:set", new
            {
                symbol = Economy.CurrencySymbol, balance = amount, player = target.DisplayName
            }]);
        }
    }
}
