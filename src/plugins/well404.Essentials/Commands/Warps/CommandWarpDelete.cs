using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using well404.Essentials.Warps;

namespace well404.Essentials.Commands.Warps
{
    [Command("delete")]
    [CommandAlias("del")]
    [CommandParent(typeof(CommandWarp))]
    [CommandSyntax("<name>")]
    [CommandDescription("Deletes a warp. Admin command (usable from console too).")]
    public class CommandWarpDelete : Command
    {
        private readonly WarpService m_Warps;
        private readonly IStringLocalizer m_StringLocalizer;

        public CommandWarpDelete(
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

            var name = Context.Parameters[0];
            if (!m_Warps.Delete(name))
            {
                throw new UserFriendlyException(m_StringLocalizer["warp:not_found", new { name }]);
            }

            await PrintAsync(m_StringLocalizer["warp:deleted", new { name }]);
        }
    }
}
