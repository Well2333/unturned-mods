using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Sleep;

namespace well404.Essentials.Commands.Sleep
{
    [Command("sleep")]
    [CommandDescription("Vote to skip to the other half of the day. Passes when enough players agree.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandSleep : Command
    {
        private readonly SleepVoteService m_Sleep;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandSleep(
            IServiceProvider serviceProvider,
            SleepVoteService sleep,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Sleep = sleep;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var outcome = await m_Sleep.VoteAsync(user);

            // Counted / Passed are broadcast to everyone by the service; only report the rest here.
            switch (outcome)
            {
                case SleepVoteOutcome.Disabled:
                    await PrintAsync(m_StringLocalizer["sleep:disabled"]);
                    break;
                case SleepVoteOutcome.AlreadyVoted:
                    await PrintAsync(m_StringLocalizer["sleep:already_voted"]);
                    break;
            }
        }
    }
}
