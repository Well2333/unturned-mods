using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenMod.API.Permissions;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.WebPanel;

namespace well404.WebPanel
{
    /// <summary>
    /// The player panel's home/intro tab: the shared Markdown server introduction plus the list of
    /// commands the viewing player may actually use (filtered by their permissions). Command help
    /// is contributed by feature plugins via <see cref="IPlayerCommandRegistry"/>; descriptions and
    /// group headings are localized to the player's chosen language.
    /// </summary>
    public sealed class IntroPlayerMenu : IPlayerMenu
    {
        public const string MenuId = "home";

        private readonly IntroStore m_Intro;
        private readonly IPlayerCommandRegistry m_Commands;
        private readonly IWebTranslationRegistry m_Tr;
        private readonly IPermissionChecker m_Permissions;
        private readonly IUserManager m_UserManager;

        public IntroPlayerMenu(
            IntroStore intro,
            IPlayerCommandRegistry commands,
            IWebTranslationRegistry translations,
            IPermissionChecker permissions,
            IUserManager userManager)
        {
            m_Intro = intro;
            m_Commands = commands;
            m_Tr = translations;
            m_Permissions = permissions;
            m_UserManager = userManager;
        }

        public string Id => MenuId;

        // English key; the server resolves tab titles to the player's language.
        public string Title => "Home";

        public string? Icon => "🏠";

        public async Task<PlayerMenuView> RenderAsync(PlayerMenuContext context)
        {
            var lang = context.Language;
            var actor = await ResolveActorAsync(context.SteamId);

            // Group commands by their (localized) heading, keeping registration order.
            var groups = new List<string>();
            var byGroup = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var cmd in m_Commands.GetAll())
            {
                if (cmd.Permission != null && actor != null && !await CanUseAsync(actor, cmd.Permission))
                {
                    continue;
                }

                var heading = cmd.GroupKey != null ? m_Tr.Resolve(cmd.GroupKey, lang) : m_Tr.Resolve("Commands", lang);
                if (!byGroup.TryGetValue(heading, out var list))
                {
                    list = new List<string>();
                    byGroup[heading] = list;
                    groups.Add(heading);
                }

                var desc = m_Tr.Resolve(cmd.DescriptionKey, lang);
                list.Add(string.IsNullOrEmpty(desc) ? cmd.Command : cmd.Command + "  —  " + desc);
            }

            var cards = new List<PlayerCard>();
            foreach (var heading in groups)
            {
                cards.Add(new PlayerCard(heading, heading, byGroup[heading]));
            }

            return new PlayerMenuView(
                m_Tr.Resolve("Home", lang), null, cards, null, m_Intro.Read());
        }

        public Task<PlayerActionResult> InvokeAsync(
            PlayerMenuContext context, string actionId, string cardKey, string? value)
            => Task.FromResult(PlayerActionResult.Fail("No action."));

        private async Task<IPermissionActor?> ResolveActorAsync(string steamId)
            => await m_UserManager.FindUserAsync(KnownActorTypes.Player, steamId, UserSearchMode.FindById) as UnturnedUser;

        /// <summary>
        /// True if the player may use the command. OpenMod throws on an unregistered permission, so
        /// any failure here defaults to showing the command rather than crashing the whole page.
        /// </summary>
        private async Task<bool> CanUseAsync(IPermissionActor actor, string permission)
        {
            try
            {
                return await m_Permissions.CheckPermissionAsync(actor, permission) == PermissionGrantResult.Grant;
            }
            catch
            {
                return true;
            }
        }
    }
}
