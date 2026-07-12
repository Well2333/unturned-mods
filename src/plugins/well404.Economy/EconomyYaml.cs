using System.Globalization;
using System.Text;

namespace well404.Economy
{
    /// <summary>
    /// Hand-rolled YAML writer for <see cref="EconomySettings"/>, so the web panel can
    /// rewrite the economy <c>config.yaml</c> without pulling in a YAML library. Rewriting
    /// replaces the whole file (original comments are lost — the accepted trade-off for
    /// editing settings from the panel).
    /// </summary>
    internal static class EconomyYaml
    {
        public static string Serialize(EconomySettings settings)
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Economy.\n");
            sb.Append("# This file is rewritten by well404.WebPanel when settings are edited;\n");
            sb.Append("# manual comments will be lost on the next panel edit.\n\n");

            sb.Append("currency:\n");
            sb.Append("  name: ").Append(Quote(settings.Currency.Name)).Append('\n');
            sb.Append("  symbol: ").Append(Quote(settings.Currency.Symbol)).Append('\n');
            sb.Append("  startingBalance: ").Append(Num(settings.Currency.StartingBalance)).Append('\n');
            sb.Append('\n');

            sb.Append("backend: ").Append(Quote(settings.Backend)).Append('\n');
            sb.Append('\n');

            sb.Append("transfer:\n");
            sb.Append("  enabled: ").Append(Bool(settings.Transfer.Enabled)).Append('\n');
            sb.Append("  minAmount: ").Append(Num(settings.Transfer.MinAmount)).Append('\n');
            sb.Append("  taxPercent: ").Append(Num(settings.Transfer.TaxPercent)).Append('\n');
            sb.Append('\n');

            sb.Append("killRewards:\n");
            sb.Append("  enabled: ").Append(Bool(settings.KillRewards.Enabled)).Append('\n');
            sb.Append("  player: ").Append(Num(settings.KillRewards.Player)).Append('\n');
            sb.Append("  zombie: ").Append(Num(settings.KillRewards.Zombie)).Append('\n');
            sb.Append("  megaZombie: ").Append(Num(settings.KillRewards.MegaZombie)).Append('\n');
            sb.Append("  animal: ").Append(Num(settings.KillRewards.Animal)).Append('\n');

            return sb.ToString();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Quote(string value)
        {
            var escaped = (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
