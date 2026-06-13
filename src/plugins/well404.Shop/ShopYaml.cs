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
                }
            }

            sb.Append('\n');

            // Bundles: a named pack of items, referenced by their own id.
            if (settings.Bundles.Count == 0)
            {
                sb.Append("bundles: []\n");
                return sb.ToString();
            }

            sb.Append("bundles:\n");
            foreach (var bundle in settings.Bundles)
            {
                sb.Append("  - id: ").Append(Quote(bundle.Id)).Append('\n');
                sb.Append("    name: ").Append(Quote(bundle.Name)).Append('\n');
                sb.Append("    buyPrice: ").Append(Num(bundle.BuyPrice)).Append('\n');
                sb.Append("    sellPrice: ").Append(Num(bundle.SellPrice)).Append('\n');
                if (bundle.Contents.Count > 0)
                {
                    sb.Append("    contents:\n");
                    foreach (var content in bundle.Contents)
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
