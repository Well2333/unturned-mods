using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using Steamworks;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("deny")]
    [CommandParent(typeof(CommandParty))]
    [CommandSyntax("[player]")]
    [CommandDescription("Denies a party invite (the most recent one, or from a named player).")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPartyDeny : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly PartyInviteManager m_Invites;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPartyDeny(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            PartyInviteManager invites,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Invites = invites;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var recipient = (UnturnedUser)Context.Actor;

            if (Context.Parameters.Length >= 1)
            {
                var search = Context.Parameters[0];
                var named = m_UserDirectory.FindUser(search, UserSearchMode.FindByNameOrId);
                if (named == null)
                {
                    throw new UserFriendlyException(m_StringLocalizer["errors:player_not_found", new { player = search }]);
                }

                if (!m_Invites.Take(recipient.SteamId.m_SteamID, named.SteamId.m_SteamID))
                {
                    throw new UserFriendlyException(m_StringLocalizer["party:none_from", new { player = named.DisplayName }]);
                }

                await PrintAsync(m_StringLocalizer["party:denied", new { player = named.DisplayName }]);
                await named.PrintMessageAsync(m_StringLocalizer["party:denied_other", new { player = recipient.DisplayName }]);
                return;
            }

            var earliest = m_Invites.TakeEarliest(recipient.SteamId.m_SteamID);
            if (earliest == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["party:no_invites"]);
            }

            var inviter = m_UserDirectory.FindUser(new CSteamID(earliest.Value));
            var inviterName = inviter?.DisplayName ?? earliest.Value.ToString();
            await PrintAsync(m_StringLocalizer["party:denied", new { player = inviterName }]);
            if (inviter != null)
            {
                await inviter.PrintMessageAsync(m_StringLocalizer["party:denied_other", new { player = recipient.DisplayName }]);
            }
        }
    }
}
