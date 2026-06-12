using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Users;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using Steamworks;
using well404.Essentials.Tp;

namespace well404.Essentials.Commands.Tp
{
    [Command("tpd")]
    [CommandSyntax("[player]")]
    [CommandDescription("Denies a teleport request (the most recent one, or from a named player).")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandTpd : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly TeleportRequestManager m_Requests;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandTpd(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            TeleportRequestManager requests,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Requests = requests;
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

                if (!m_Requests.Take(recipient.SteamId.m_SteamID, named.SteamId.m_SteamID))
                {
                    throw new UserFriendlyException(m_StringLocalizer["tpa:none_from", new { player = named.DisplayName }]);
                }

                await PrintAsync(m_StringLocalizer["tpa:denied", new { player = named.DisplayName }]);
                await named.PrintMessageAsync(m_StringLocalizer["tpa:denied_other", new { player = recipient.DisplayName }]);
                return;
            }

            var earliest = m_Requests.TakeEarliest(recipient.SteamId.m_SteamID);
            if (earliest == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["tpa:none"]);
            }

            var requester = m_UserDirectory.FindUser(new CSteamID(earliest.Value));
            var requesterName = requester?.DisplayName ?? earliest.Value.ToString();
            await PrintAsync(m_StringLocalizer["tpa:denied", new { player = requesterName }]);
            if (requester != null)
            {
                await requester.PrintMessageAsync(m_StringLocalizer["tpa:denied_other", new { player = recipient.DisplayName }]);
            }
        }
    }
}
