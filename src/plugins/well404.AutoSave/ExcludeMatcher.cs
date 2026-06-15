using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace well404.AutoSave
{
    /// <summary>
    /// Matches savedata-relative paths against a list of exclude globs. Pure (no game/IO), so it is
    /// unit tested directly. Paths use <c>/</c> separators and are matched case-insensitively.
    /// Wildcards: <c>*</c> matches within one path segment, <c>**</c> matches across segments
    /// (including none), <c>?</c> matches a single non-separator character.
    /// </summary>
    public sealed class ExcludeMatcher
    {
        private readonly Regex[] m_Patterns;

        public ExcludeMatcher(IEnumerable<string> patterns)
        {
            if (patterns == null)
            {
                throw new ArgumentNullException(nameof(patterns));
            }

            var list = new List<Regex>();
            foreach (var pattern in patterns)
            {
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    list.Add(GlobToRegex(pattern.Trim()));
                }
            }

            m_Patterns = list.ToArray();
        }

        /// <summary>True if <paramref name="relativePath"/> matches any exclude pattern.</summary>
        public bool IsExcluded(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
            {
                return false;
            }

            var normalized = relativePath.Replace('\\', '/').TrimStart('/');
            foreach (var pattern in m_Patterns)
            {
                if (pattern.IsMatch(normalized))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Translates a glob into an anchored, case-insensitive regex.</summary>
        private static Regex GlobToRegex(string glob)
        {
            var normalized = glob.Replace('\\', '/').TrimStart('/');
            var sb = new StringBuilder("^");
            var i = 0;
            while (i < normalized.Length)
            {
                var c = normalized[i];
                if (c == '*')
                {
                    if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                    {
                        // "**" — across segments.
                        i += 2;
                        if (i < normalized.Length && normalized[i] == '/')
                        {
                            // "**/" matches zero or more leading segments (so a top-level match works too).
                            sb.Append("(?:.*/)?");
                            i++;
                        }
                        else
                        {
                            sb.Append(".*");
                        }
                    }
                    else
                    {
                        // "*" — within a single segment.
                        sb.Append("[^/]*");
                        i++;
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                    i++;
                }
                else if (c == '/')
                {
                    sb.Append('/');
                    i++;
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                    i++;
                }
            }

            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
