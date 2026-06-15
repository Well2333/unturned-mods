using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Users;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using Steamworks;
using well404.Essentials.Teleport;
using well404.Essentials.Tp;
using well404.Essentials.Util;

namespace well404.Essentials.Commands.Tp
{
    [Command("tpa")]
    [CommandSyntax("[player]")]
    [CommandDescription("Accepts a teleport request (the most recent one, or from a named player).")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandTpa : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly TeleportService m_Teleport;
        private readonly TeleportRequestManager m_Requests;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandTpa(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            TeleportService teleport,
            TeleportRequestManager requests,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Teleport = teleport;
            m_Requests = requests;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var recipient = (UnturnedUser)Context.Actor;
            var requester = ResolveRequester(recipient);

            await UniTask.SwitchToMainThread();
            var destination = LocationHelper.FromPlayer(recipient.Player);

            await PrintAsync(m_StringLocalizer["tpa:accepted", new { player = requester.DisplayName }]);
            await requester.PrintMessageAsync(m_StringLocalizer["tpa:accepted_other", new { player = recipient.DisplayName }]);

            // The requester is the one who travels (and pays any fee / serves the warmup).
            await m_Teleport.TryTeleportAsync(requester, destination, TeleportKind.Tp, "tp");
        }

        /// <summary>Removes and returns the requester to accept, or throws a user-friendly error.</summary>
        private UnturnedUser ResolveRequester(UnturnedUser recipient)
        {
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

                return named;
            }

            var earliest = m_Requests.TakeEarliest(recipient.SteamId.m_SteamID);
            if (earliest == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["tpa:none"]);
            }

            var requester = m_UserDirectory.FindUser(new CSteamID(earliest.Value));
            if (requester == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["tpa:sender_offline", new { player = earliest.Value }]);
            }

            return requester;
        }
    }
}
