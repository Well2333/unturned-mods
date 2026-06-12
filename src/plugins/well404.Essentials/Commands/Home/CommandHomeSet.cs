using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Data;
using well404.Essentials.Util;

namespace well404.Essentials.Commands.Home
{
    [Command("set")]
    [CommandParent(typeof(CommandHome))]
    [CommandDescription("Saves your current location as your home.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandHomeSet : Command
    {
        private readonly PlayerDataStore m_PlayerData;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandHomeSet(
            IServiceProvider serviceProvider,
            PlayerDataStore playerData,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_PlayerData = playerData;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            var user = (UnturnedUser)Context.Actor;

            await UniTask.SwitchToMainThread();
            var location = LocationHelper.FromPlayer(user.Player);
            await m_PlayerData.SetHomeAsync(user.Id, location);

            await PrintAsync(m_StringLocalizer["home:set"]);
        }
    }
}
