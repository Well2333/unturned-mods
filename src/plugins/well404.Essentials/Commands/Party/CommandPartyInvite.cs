using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Party;

namespace well404.Essentials.Commands.Party
{
    [Command("invite")]
    [CommandParent(typeof(CommandParty))]
    [CommandSyntax("<player>")]
    [CommandDescription("Invites a player to your party.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandPartyInvite : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly PartyInviteManager m_Invites;
        private readonly PartyService m_Party;
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandPartyInvite(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            PartyInviteManager invites,
            PartyService party,
            IConfiguration configuration,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Invites = invites;
            m_Party = party;
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1)
            {
                throw new CommandWrongUsageException(Context);
            }

            var user = (UnturnedUser)Context.Actor;
            var search = Context.Parameters[0];

            var target = m_UserDirectory.FindUser(search, UserSearchMode.FindByNameOrId);
            if (target == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["errors:player_not_found", new { player = search }]);
            }

            if (target.SteamId.m_SteamID == user.SteamId.m_SteamID)
            {
                throw new UserFriendlyException(m_StringLocalizer["party:self"]);
            }

            await UniTask.SwitchToMainThread();
            if (m_Party.SameParty(user, target))
            {
                throw new UserFriendlyException(m_StringLocalizer["party:already_member", new { player = target.DisplayName }]);
            }

            var expiration = (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Party.InviteExpirationSeconds;
            if (!m_Invites.Open(user.SteamId.m_SteamID, target.SteamId.m_SteamID, expiration * 1000))
            {
                throw new UserFriendlyException(m_StringLocalizer["party:already_invited", new { player = target.DisplayName }]);
            }

            await PrintAsync(m_StringLocalizer["party:invite_sent", new { player = target.DisplayName }]);
            await target.PrintMessageAsync(m_StringLocalizer["party:invite_received", new { player = user.DisplayName, seconds = expiration }]);
        }
    }
}
