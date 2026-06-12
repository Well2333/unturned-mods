using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using Steamworks;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("accept")]
    [CommandParent(typeof(CommandParty))]
    [CommandSyntax("[player]")]
    [CommandDescription("Accepts a party invite (the most recent one, or from a named player).")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPartyAccept : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly PartyInviteManager m_Invites;
        private readonly PartyService m_Party;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPartyAccept(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            PartyInviteManager invites,
            PartyService party,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Invites = invites;
            m_Party = party;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var recipient = (UnturnedUser)Context.Actor;
            var inviter = ResolveInviter(recipient);

            await UniTask.SwitchToMainThread();
            var status = m_Party.JoinViaInvite(inviter, recipient);
            switch (status)
            {
                case PartyJoinStatus.Full:
                    throw new UserFriendlyException(m_StringLocalizer["party:full"]);
                case PartyJoinStatus.Failed:
                    throw new UserFriendlyException(m_StringLocalizer["party:join_failed"]);
                default:
                    await PrintAsync(m_StringLocalizer["party:joined", new { player = inviter.DisplayName }]);
                    await inviter.PrintMessageAsync(m_StringLocalizer["party:joined_other", new { player = recipient.DisplayName }]);
                    break;
            }
        }

        private UnturnedUser ResolveInviter(UnturnedUser recipient)
        {
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

                return named;
            }

            var earliest = m_Invites.TakeEarliest(recipient.SteamId.m_SteamID);
            if (earliest == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["party:no_invites"]);
            }

            var inviter = m_UserDirectory.FindUser(new CSteamID(earliest.Value));
            if (inviter == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["party:inviter_offline", new { player = earliest.Value }]);
            }

            return inviter;
        }
    }
}
