using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("party")]
    [CommandDescription("Shows your party members. Subcommands: invite, accept, deny, leave, kick.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandParty : Command
    {
        private readonly PartyService m_Party;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandParty(
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
            var members = m_Party.GetMembers(user);
            if (members.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["party:none_self"]);
                return;
            }

            await PrintAsync(m_StringLocalizer["party:list_header", new { name = m_Party.GetPartyName(user) ?? "?" }]);
            foreach (var member in members)
            {
                var role = m_StringLocalizer[member.IsLeader ? "party:role_leader" : "party:role_member"];
                await PrintAsync(m_StringLocalizer["party:list_entry", new { player = member.DisplayName, role }]);
            }
        }
    }
}
