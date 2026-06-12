using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;
using well404.Essentials.Teleport;
using well404.Essentials.Warps;

namespace well404.Essentials.Commands.Warps
{
    [Command("warp")]
    [CommandSyntax("<name>")]
    [CommandDescription("Teleports you to a warp.")]
    [CommandActor(typeof(UnturnedUser))]
    public class CommandWarp : Command
    {
        private readonly WarpService m_Warps;
        private readonly TeleportService m_Teleport;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandWarp(
            IServiceProvider serviceProvider,
            WarpService warps,
            TeleportService teleport,
            IStringLocalizer stringLocalizer) : base(serviceProvider)
        {
            m_Warps = warps;
            m_Teleport = teleport;
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

            var warp = m_Warps.Find(name);
            if (warp == null)
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:not_found", new { name }]);
            }

            if (!await m_Warps.HasAccessAsync(user, warp.Name))
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:no_permission", new { name = warp.Name }]);
            }

            var destination = WarpService.ToLocation(warp);
            var cooldownKey = "warp:" + warp.Name.ToLowerInvariant();
            if (await m_Teleport.TryTeleportAsync(user, destination, TeleportKind.Warp, cooldownKey, warp.CooldownSeconds))
            {
                await PrintAsync(m_StringLocalizer["warp:success", new { name = warp.Name }]);
            }
        }
    }
}
