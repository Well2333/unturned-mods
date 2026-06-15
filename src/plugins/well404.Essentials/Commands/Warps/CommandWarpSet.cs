using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Util;
using well404.Essentials.Warps;

namespace well404.Essentials.Commands.Warps
{
    [Command("set")]
    [CommandParent(typeof(CommandWarp))]
    [CommandSyntax("<name> [cooldownSeconds]")]
    [CommandDescription("Saves your current location as a warp. Admin command.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandWarpSet : Command
    {
        private readonly WarpService m_Warps;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandWarpSet(
            IServiceProvider serviceProvider,
            WarpService warps,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Warps = warps;
            m_StringLocalizer = stringLocalizer;
        }

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1)
            {
                throw new CommandWrongUsageException(Context);
            }

            var user = (UnturnedUser)Context.Actor;
            var name = Context.Parameters[0];
            var cooldown = Context.Parameters.Length >= 2 ? await Context.Parameters.GetAsync<int>(1) : 0;
            if (cooldown < 0)
            {
                cooldown = 0;
            }

            await UniTask.SwitchToMainThread();
            var location = LocationHelper.FromPlayer(user.Player);
            m_Warps.Set(name, location, cooldown);

            await PrintAsync(m_StringLocalizer["warp:set", new { name }]);
        }
    }
}
