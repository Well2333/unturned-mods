using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
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
            if (!EssentialsConfigStore.IsValidWarpName(name))
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:invalid_name"]);
            }
            var tags = new List<string>();
            for (var i = 1; i < Context.Parameters.Length; i++)
            {
                tags.Add(Context.Parameters[i]);
            }
            var normalizedTags = tags.Count == 0 ? tags : EssentialsConfigStore.ParseTags(tags);
            if (normalizedTags.Count > EssentialsConfigStore.MaxWarpTags)
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:too_many_tags", new { count = EssentialsConfigStore.MaxWarpTags }]);
            }
            foreach (var tag in normalizedTags)
            {
                if (!EssentialsConfigStore.IsValidTagId(tag))
                {
                    throw new UserFriendlyException(m_StringLocalizer["warp:invalid_tag", new { tag }]);
                }
            }

            await UniTask.SwitchToMainThread();
            var location = LocationHelper.FromPlayer(user.Player);
            var map = m_WarpMap.CurrentMapName;
            if (!EssentialsConfigStore.IsValidMapName(map))
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:invalid_map"]);
            }
            m_Warps.Set(name.Trim(), location, normalizedTags, map);

            await PrintAsync(m_StringLocalizer["warp:set", new { name }]);
        }
    }
}
