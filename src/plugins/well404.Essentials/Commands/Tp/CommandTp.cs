using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.API.Users;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Teleport;
using well404.Essentials.Tp;
using well404.Essentials.Util;

namespace well404.Essentials.Commands.Tp
{
    [Command("tp")]
    [CommandSyntax("<player>")]
    [CommandDescription("Teleports you to a player. Same-team is instant; otherwise sends a /tpa request.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandTp : Command
    {
        private readonly IUnturnedUserDirectory m_UserDirectory;
        private readonly TeleportService m_Teleport;
        private readonly TeleportRequestManager m_Requests;
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandTp(
            IServiceProvider serviceProvider,
            IUnturnedUserDirectory userDirectory,
            TeleportService teleport,
            TeleportRequestManager requests,
            IConfiguration configuration,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_UserDirectory = userDirectory;
            m_Teleport = teleport;
            m_Requests = requests;
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
                throw new UserFriendlyException(m_StringLocalizer["tp:self"]);
            }

            await UniTask.SwitchToMainThread();
            var sameTeam = user.Player.Player.quests.isMemberOfSameGroupAs(target.Player.Player);

            if (sameTeam)
            {
                // Teammates teleport directly (no confirmation needed).
                var destination = LocationHelper.FromPlayer(target.Player);
                if (await m_Teleport.TryTeleportAsync(user, destination, TeleportKind.Tp, "tp"))
                {
                    await PrintAsync(m_StringLocalizer["tp:teleported", new { player = target.DisplayName }]);
                }

                return;
            }

            // Different teams: open a request the target must accept.
            var expiration = (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Tpa.ExpirationSeconds;
            if (!m_Requests.Open(user.SteamId.m_SteamID, target.SteamId.m_SteamID, expiration * 1000))
            {
                throw new UserFriendlyException(m_StringLocalizer["tp:already_requested", new { player = target.DisplayName }]);
            }

            await PrintAsync(m_StringLocalizer["tp:request_sent", new { player = target.DisplayName, seconds = expiration }]);
            await target.PrintMessageAsync(m_StringLocalizer["tp:request_received", new { player = user.DisplayName }]);
        }
    }
}
