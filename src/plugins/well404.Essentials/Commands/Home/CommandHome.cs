using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Data;
using well404.Essentials.Teleport;

namespace well404.Essentials.Commands.Home
{
    [Command("home")]
    [CommandDescription("Teleports you to your saved home.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandHome : Command
    {
        private readonly PlayerDataStore m_PlayerData;
        private readonly TeleportService m_Teleport;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandHome(
            IServiceProvider serviceProvider,
            PlayerDataStore playerData,
            TeleportService teleport,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_PlayerData = playerData;
            m_Teleport = teleport;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;

            var home = await m_PlayerData.GetHomeAsync(user.Id);
            if (home == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["home:none"]);
            }

            if (await m_Teleport.TryTeleportAsync(user, home, TeleportKind.Home, "home"))
            {
                await PrintAsync(m_StringLocalizer["home:success"]);
            }
        }
    }
}
