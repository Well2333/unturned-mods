using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("leave")]
    [CommandParent(typeof(CommandParty))]
    [CommandDescription("Leaves your current party.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPartyLeave : Command
    {
        private readonly PartyService m_Party;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPartyLeave(
            IServiceProvider serviceProvider,
            PartyService party,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Party = party;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;

            await UniTask.SwitchToMainThread();
            await PrintAsync(m_Party.Leave(user)
                ? m_StringLocalizer["party:left"]
                : m_StringLocalizer["party:none_self"]);
        }
    }
}
