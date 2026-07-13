using System.Globalization;
using System.Text;

namespace well404.Shop
{
    /// <summary>
    /// Hand-rolled YAML writer for <see cref="ShopSettings"/>. The shop catalog is a
    /// simple, fixed shape, so emitting it by hand keeps the plugin dependency-free
    /// (no YamlDotNet to ship) and lets the web panel rewrite <c>config.yaml</c>.
    /// <para>
    /// Rewriting replaces the whole file (comments in the original are lost — this is
    /// the accepted trade-off for editing the catalog from the panel).
    /// </para>
    /// </summary>
    internal static class ShopYaml
    {
        public static string Serialize(ShopSettings settings)
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Shop.\n");
            sb.Append("# This file is rewritten by well404.WebPanel when the catalog is edited;\n");
            sb.Append("# manual comments will be lost on the next panel edit. Requires well404.Economy.\n\n");

            sb.Append("discounts:\n");
            sb.Append("  enabled: ").Append(Bool(settings.Discounts.Enabled)).Append('\n');
            if (settings.Discounts.Tiers.Count == 0)
            {
                sb.Append("  tiers: {}\n");
            }
            else
            {
                sb.Append("  tiers:\n");
                foreach (var tier in settings.Discounts.Tiers)
                {
                    sb.Append("    ").Append(Quote(tier.Key)).Append(": ").Append(Num(tier.Value)).Append('\n');
                }
            }

            sb.Append('\n');
            if (settings.Groups.Count == 0)
            {
                sb.Append("groups: []\n");
            }
            else
            {
                sb.Append("groups:\n");
                foreach (var group in settings.Groups)
                {
                    sb.Append("  - id: ").Append(Quote(group.Id)).Append('\n');
                    sb.Append("    name: ").Append(Quote(group.Name)).Append('\n');
                }
            }

            sb.Append('\n');


            // Plain items: referenced by their own game item id; name is resolved from the directory.
            if (settings.Items.Count == 0)
            {
                sb.Append("items: []\n");
            }
            else
            {
                sb.Append("items:\n");
                foreach (var item in settings.Items)
                {
                    sb.Append("  - itemId: ").Append(item.ItemId.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    sb.Append("    buyPrice: ").Append(Num(item.BuyPrice)).Append('\n');
                    sb.Append("    sellPrice: ").Append(Num(item.SellPrice)).Append('\n');
                    sb.Append("    group: ").Append(Quote(item.Group)).Append('\n');
                    sb.Append("    note: ").Append(Quote(item.Note)).Append('\n');
                    sb.Append("    order: ").Append(item.Order.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Double-quotes a scalar and escapes control characters for YAML.</summary>
        private static string Quote(string value)
        {
            var escaped = (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
            return "\"" + escaped + "\"";
        }
    }
}
