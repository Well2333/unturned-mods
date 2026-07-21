using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Warps;

namespace well404.Essentials.Commands.Warps
{
    [Command("warps")]
    [CommandDescription("Lists the warps you can use.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandWarps : Command
    {
        private readonly WarpService m_Warps;
        private readonly WarpMapService m_WarpMap;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandWarps(
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
            var user = (UnturnedUser)Context.Actor;

            await UniTask.SwitchToMainThread();
            var accessible = new List<string>();
            foreach (var warp in m_Warps.All)
            {
                if (m_WarpMap.IsCurrentMap(warp) && await m_Warps.HasAccessAsync(user, warp.Name))
                {
                    accessible.Add(warp.Name);
                }
            }

            if (accessible.Count == 0)
            {
                await PrintAsync(m_StringLocalizer["warp:list_empty"]);
                return;
            }

            await PrintAsync(m_StringLocalizer["warp:list", new { warps = string.Join(", ", accessible) }]);
        }
    }
}
