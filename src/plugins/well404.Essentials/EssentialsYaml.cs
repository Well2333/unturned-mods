using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace well404.Essentials
{
    /// <summary>
    /// Hand-rolled YAML writer for <see cref="EssentialsSettings"/>. Like the Shop, the config
    /// has a small fixed shape, so emitting it by hand keeps the plugin dependency-free and lets
    /// the web panel (and <c>/warp set</c>) rewrite <c>config.yaml</c>.
    /// <para>Rewriting replaces the whole file — comments in the original are lost.</para>
    /// </summary>
    internal static class EssentialsYaml
    {
        public static string Serialize(EssentialsSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Essentials.\n");
            sb.Append("# This file is rewritten by well404.WebPanel / in-game commands; manual comments are lost.\n");
            sb.Append("# Economy fees require an IEconomyProvider (e.g. well404.Economy); without one, teleports are free.\n\n");

            sb.Append("teleport:\n");
            sb.Append("  warmupSeconds: ").Append(Int(s.Teleport.WarmupSeconds)).Append('\n');
            sb.Append("  cancelOnMove: ").Append(Bool(s.Teleport.CancelOnMove)).Append('\n');
            sb.Append("  moveThreshold: ").Append(Num(s.Teleport.MoveThreshold)).Append('\n');
            sb.Append("  cooldownSeconds: ").Append(Int(s.Teleport.CooldownSeconds)).Append('\n');
            sb.Append("  costs:\n");
            sb.Append("    home: ").Append(Num(s.Teleport.Costs.Home)).Append('\n');
            sb.Append("    tp: ").Append(Num(s.Teleport.Costs.Tp)).Append('\n');
            sb.Append("    warp: ").Append(Num(s.Teleport.Costs.Warp)).Append('\n');
            sb.Append("    back: ").Append(Num(s.Teleport.Costs.Back)).Append('\n');
            sb.Append('\n');

            sb.Append("tpa:\n");
            sb.Append("  expirationSeconds: ").Append(Int(s.Tpa.ExpirationSeconds)).Append('\n');
            sb.Append('\n');

            sb.Append("party:\n");
            sb.Append("  inviteExpirationSeconds: ").Append(Int(s.Party.InviteExpirationSeconds)).Append('\n');
            sb.Append("  maxMembers: ").Append(Int(s.Party.MaxMembers)).Append('\n');
            sb.Append('\n');

            sb.Append("back:\n");
            sb.Append("  invincibilitySeconds: ").Append(Int(s.Back.InvincibilitySeconds)).Append('\n');
            sb.Append('\n');

            sb.Append("sleep:\n");
            sb.Append("  enabled: ").Append(Bool(s.Sleep.Enabled)).Append('\n');
            sb.Append("  requiredRatio: ").Append(Num(s.Sleep.RequiredRatio)).Append('\n');
            sb.Append('\n');

            if (s.Warps.Count == 0)
            {
                sb.Append("warps: []\n");
            }
            else
            {
                sb.Append("warps:\n");
                foreach (var w in s.Warps)
                {
                    sb.Append("  - name: ").Append(Quote(w.Name)).Append('\n');
                    sb.Append("    x: ").Append(Num(w.X)).Append('\n');
                    sb.Append("    y: ").Append(Num(w.Y)).Append('\n');
                    sb.Append("    z: ").Append(Num(w.Z)).Append('\n');
                    sb.Append("    yaw: ").Append(Num(w.Yaw)).Append('\n');
                    sb.Append("    cooldownSeconds: ").Append(Int(w.CooldownSeconds)).Append('\n');
                }
            }

            sb.Append('\n');

            if (s.Gifts.Count == 0)
            {
                sb.Append("gifts: []\n");
                return sb.ToString();
            }

            sb.Append("gifts:\n");
            foreach (var g in s.Gifts)
            {
                sb.Append("  - id: ").Append(Quote(g.Id)).Append('\n');
                sb.Append("    name: ").Append(Quote(g.Name)).Append('\n');
                sb.Append("    permission: ").Append(Quote(g.Permission)).Append('\n');
                sb.Append("    cron: ").Append(Quote(g.Cron)).Append('\n');
                if (g.Items.Count == 0)
                {
                    sb.Append("    items: []\n");
                }
                else
                {
                    sb.Append("    items:\n");
                    foreach (var item in g.Items)
                    {
                        sb.Append("      - itemId: ").Append(Int(item.ItemId)).Append('\n');
                        sb.Append("        amount: ").Append(Int(item.Amount)).Append('\n');
                    }
                }
            }

            return sb.ToString();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Int(ushort value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Double-quotes a scalar and escapes backslashes and quotes for YAML.</summary>
        private static string Quote(string value)
        {
            var escaped = (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
