using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel.Commands
{
    /// <summary>
    /// Opens the player's personal web panel: mints a short-lived session link via
    /// <see cref="IPlayerWebSessionService"/> and pushes it to the player's Steam overlay
    /// browser. An optional first argument focuses a specific tab (menu id, e.g. <c>shop</c>).
    /// </summary>
    [Command("menu")]
    [CommandAlias("panel")]
    [CommandSyntax("[tab]")]
    [CommandDescription("Opens your personal web panel in the Steam overlay browser.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandMenu : Command
    {
        private readonly IPlayerWebSessionService m_Sessions;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandMenu(
            IServiceProvider serviceProvider,
            IPlayerWebSessionService sessions,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Sessions = sessions;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var menuId = Context.Parameters.Length >= 1 ? Context.Parameters[0] : null;

            var url = m_Sessions.CreateLink(user.Id, user.DisplayName, menuId);
            if (url == null)
            {
                // No reachable public base URL configured (see web.publicBaseUrl).
                throw new UserFriendlyException(m_StringLocalizer["menu:unavailable"]);
            }

            await UniTask.SwitchToMainThread();
            var player = user.Player.Player;
            if (player == null)
            {
                return;
            }

            player.sendBrowserRequest(m_StringLocalizer["menu:prompt"], url);
            await PrintAsync(m_StringLocalizer["menu:opened"]);
        }
    }
}
