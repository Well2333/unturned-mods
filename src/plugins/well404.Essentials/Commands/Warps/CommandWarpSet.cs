using System;
using System.Collections.Generic;
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
    [CommandSyntax("<name> [tag ...]")]
    [CommandDescription("Saves your current location as a warp and optionally replaces its tags. Admin command.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandWarpSet : Command
    {
        private readonly WarpService m_Warps;
        private readonly WarpMapService m_WarpMap;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandWarpSet(
            IServiceProvider serviceProvider,
            WarpService warps,
            WarpMapService warpMap,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Warps = warps;
            m_WarpMap = warpMap;
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
            var tags = new List<string>();
            for (var i = 1; i < Context.Parameters.Length; i++)
            {
                tags.Add(Context.Parameters[i]);
            }

            await UniTask.SwitchToMainThread();
            var location = LocationHelper.FromPlayer(user.Player);
            m_Warps.Set(name, location, tags, m_WarpMap.CurrentMapName);

            await PrintAsync(m_StringLocalizer["warp:set", new { name }]);
        }
    }
}
