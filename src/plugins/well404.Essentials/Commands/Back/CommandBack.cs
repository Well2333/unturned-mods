using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Back;
using well404.Essentials.Data;
using well404.Essentials.Teleport;

namespace well404.Essentials.Commands.Back
{
    [Command("back")]
    [CommandDescription("Teleports you back to where you last died, with brief invincibility.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandBack : Command
    {
        private readonly PlayerDataStore m_PlayerData;
        private readonly TeleportService m_Teleport;
        private readonly InvincibilityService m_Invincibility;
        private readonly IConfiguration m_Configuration;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandBack(
            IServiceProvider serviceProvider,
            PlayerDataStore playerData,
            TeleportService teleport,
            InvincibilityService invincibility,
            IConfiguration configuration,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_PlayerData = playerData;
            m_Teleport = teleport;
            m_Invincibility = invincibility;
            m_Configuration = configuration;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;

            var death = await m_PlayerData.GetLastDeathAsync(user.Id);
            if (death == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["back:none"]);
            }

            if (!await m_Teleport.TryTeleportAsync(user, death, TeleportKind.Back, "back"))
            {
                return;
            }

            await PrintAsync(m_StringLocalizer["back:success"]);

            var seconds = (m_Configuration.Get<EssentialsSettings>() ?? new EssentialsSettings()).Back.InvincibilitySeconds;
            if (seconds > 0)
            {
                m_Invincibility.Protect(user.SteamId.m_SteamID, seconds);
                await PrintAsync(m_StringLocalizer["back:invincible", new { seconds }]);
            }
        }
    }
}
