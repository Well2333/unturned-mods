using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;

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
        // Inject the concrete type (not IPlayerWebSessionService) so we can use CreateLinkAsync, which
        // is intentionally NOT on the shared interface — adding to it would require shipping a new
        // UnturnedMods.Shared.dll in every plugin, and a partial upgrade could load a mismatched copy.
        private readonly PlayerWebSessionManager m_Sessions;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandMenu(
            IServiceProvider serviceProvider,
            PlayerWebSessionManager sessions,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Sessions = sessions;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;
            var menuId = Context.Parameters.Length >= 1 ? Context.Parameters[0] : null;

            // Async variant: if the built-in tunnel is still coming up just after server start, this
            // waits briefly for the public URL instead of failing immediately (the common cause of the
            // "no public address" message right after a restart).
            var url = await m_Sessions.CreateLinkAsync(user.Id, user.DisplayName, menuId);
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
