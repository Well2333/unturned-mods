using System;
using System.Linq;
using System.Threading.Tasks;
using OpenMod.API.Commands;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Users;

namespace well404.AdminTools.Commands
{
    [Command("god")]
    [CommandSyntax("[player] [on|off]")]
    [CommandDescription("Toggles godmode (invincibility) for yourself or a player.")]
    public class CommandGod : Command
    {
        private readonly AdminToolsService m_Admin;
        public CommandGod(IServiceProvider serviceProvider, AdminToolsService admin) : base(serviceProvider) => m_Admin = admin;

        protected override async Task OnExecuteAsync()
        {
            string target;
            int stateIndex;
            if (Context.Parameters.Length >= 1 && !IsOnOff(Context.Parameters[0]))
            {
                target = Context.Parameters[0];
                stateIndex = 1;
            }
            else if (Context.Actor is UnturnedUser self)
            {
                target = self.SteamId.m_SteamID.ToString();
                stateIndex = 0;
            }
            else
            {
                throw new CommandWrongUsageException(Context);
            }

            bool? on = null;
            if (Context.Parameters.Length > stateIndex)
            {
                on = IsOn(Context.Parameters[stateIndex]);
            }

            var result = await m_Admin.SetGodAsync(target, on);
            await PrintAsync(result.Message);
        }

        private static bool IsOnOff(string v) => IsOn(v) || string.Equals(v, "off", StringComparison.OrdinalIgnoreCase);
        private static bool IsOn(string v) => string.Equals(v, "on", StringComparison.OrdinalIgnoreCase) || v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    [Command("kick")]
    [CommandSyntax("<player> [reason]")]
    [CommandDescription("Kicks a player off the server.")]
    public class CommandKick : Command
    {
        private readonly AdminToolsService m_Admin;
        public CommandKick(IServiceProvider serviceProvider, AdminToolsService admin) : base(serviceProvider) => m_Admin = admin;

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1) throw new CommandWrongUsageException(Context);
            var player = Context.Parameters[0];
            var reason = Context.Parameters.Length > 1 ? string.Join(" ", Context.Parameters.Skip(1)) : null;
            var result = await m_Admin.KickAsync(player, reason);
            await PrintAsync(result.Message);
        }
    }

    [Command("ban")]
    [CommandSyntax("<player> [minutes] [reason]")]
    [CommandDescription("Bans a player (optionally for N minutes; omit for permanent).")]
    public class CommandBan : Command
    {
        private readonly AdminToolsService m_Admin;
        public CommandBan(IServiceProvider serviceProvider, AdminToolsService admin) : base(serviceProvider) => m_Admin = admin;

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1) throw new CommandWrongUsageException(Context);
            var player = Context.Parameters[0];

            int? minutes = null;
            var reasonStart = 1;
            if (Context.Parameters.Length > 1 && int.TryParse(Context.Parameters[1], out var m))
            {
                minutes = m;
                reasonStart = 2;
            }

            var reason = Context.Parameters.Length > reasonStart ? string.Join(" ", Context.Parameters.Skip(reasonStart)) : null;
            var result = await m_Admin.BanAsync(player, reason, minutes);
            await PrintAsync(result.Message);
        }
    }

    [Command("unban")]
    [CommandSyntax("<steamId>")]
    [CommandDescription("Lifts a ban on a SteamID.")]
    public class CommandUnban : Command
    {
        private readonly AdminToolsService m_Admin;
        public CommandUnban(IServiceProvider serviceProvider, AdminToolsService admin) : base(serviceProvider) => m_Admin = admin;

        protected override async Task OnExecuteAsync()
        {
            if (Context.Parameters.Length < 1) throw new CommandWrongUsageException(Context);
            var result = await m_Admin.UnbanAsync(Context.Parameters[0]);
            await PrintAsync(result.Message);
        }
    }
}
