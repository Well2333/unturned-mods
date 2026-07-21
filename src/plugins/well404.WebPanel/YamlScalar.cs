using System;
using System.Globalization;
using System.Text;

namespace well404.WebPanel
{
    /// <summary>Quotes and unquotes one YAML double-quoted scalar without allowing line injection.</summary>
    internal static class YamlScalar
    {
        public static string Quote(string value)
        {
            var result = new StringBuilder(value.Length + 2);
            result.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (char.IsControl(ch))
                        {
                            result.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            result.Append(ch);
                        }
                        break;
                }
            }

            return result.Append('"').ToString();
        }

        public static string Unquote(string value)
        {
            if (value.Length < 2 || value[value.Length - 1] != value[0])
            {
                return value;
            }

            if (value[0] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }

            if (value[0] != '"')
            {
                return value;
            }

            var result = new StringBuilder(value.Length - 2);
            for (var i = 1; i < value.Length - 1; i++)
            {
                var ch = value[i];
                if (ch != '\\' || i + 1 >= value.Length - 1)
                {
                    result.Append(ch);
                    continue;
                }

                var escaped = value[++i];
                switch (escaped)
                {
                    case '\\': result.Append('\\'); break;
                    case '"': result.Append('"'); break;
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case 'u' when i + 4 < value.Length - 1:
                        var hex = value.Substring(i + 1, 4);
                        if (ushort.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            result.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            result.Append("\\u");
                        }
                        break;
                    default: result.Append(escaped); break;
                }
            }

            return result.ToString();
        }
    }
}
