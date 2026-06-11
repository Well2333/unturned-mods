using System.Collections.Generic;
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

            if (settings.Items.Count == 0)
            {
                sb.Append("items: []\n");
                return sb.ToString();
            }

            sb.Append("items:\n");
            foreach (var entry in settings.Items)
            {
                sb.Append("  - id: ").Append(Quote(entry.Id)).Append('\n');
                sb.Append("    name: ").Append(Quote(entry.Name)).Append('\n');
                sb.Append("    type: ").Append(Quote(entry.Type)).Append('\n');
                if (!entry.IsBundle)
                {
                    sb.Append("    itemId: ").Append(entry.ItemId.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    sb.Append("    amount: ").Append(entry.Amount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }

                sb.Append("    buyPrice: ").Append(Num(entry.BuyPrice)).Append('\n');
                sb.Append("    sellPrice: ").Append(Num(entry.SellPrice)).Append('\n');

                if (entry.IsBundle && entry.Contents.Count > 0)
                {
                    sb.Append("    contents:\n");
                    foreach (var content in entry.Contents)
                    {
                        sb.Append("      - itemId: ").Append(content.ItemId.ToString(CultureInfo.InvariantCulture)).Append('\n');
                        sb.Append("        amount: ").Append(content.Amount.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            return sb.ToString();
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>Double-quotes a scalar and escapes backslashes and quotes for YAML.</summary>
        private static string Quote(string value)
        {
            var escaped = (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "\"" + escaped + "\"";
        }
    }
}
