using System.Globalization;
using System.Text;

namespace well404.WebPanel
{
    /// <summary>
    /// Minimal JSON output helper. The panel only ever emits a fixed, simple shape
    /// (objects of strings / bools / string-arrays), so a tiny hand-rolled encoder
    /// keeps the plugin dependency-free (no Newtonsoft / System.Text.Json to ship).
    /// </summary>
    internal static class Json
    {
        /// <summary>Encodes a string as a quoted JSON literal, or <c>null</c> when null.</summary>
        public static string Encode(string? value)
        {
            if (value == null)
            {
                return "null";
            }

            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        public static string Bool(bool value) => value ? "true" : "false";
    }
}
