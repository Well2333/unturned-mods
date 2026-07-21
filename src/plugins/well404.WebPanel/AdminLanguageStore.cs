using System;
using System.IO;
using System.Text;

namespace well404.WebPanel
{
    /// <summary>
    /// Persists the admin panel's chosen UI language on the server (a YAML config file
    /// <c>admin-language.yaml</c> in the working directory), so it survives the admin URL changing
    /// (e.g. a fresh quick-tunnel domain) — browser localStorage is per-origin and would be lost.
    /// The admin surface has no per-user identity (it is gated by one secret token), so one shared
    /// value is enough. Thread-safe.
    /// </summary>
    public sealed class AdminLanguageStore
    {
        private const string FileName = "admin-language.yaml";
        private const string LegacyFileName = "admin-language.txt";

        private readonly string m_Path;
        private readonly string m_LegacyPath;
        private readonly object m_Lock = new object();
        private string? m_Language;

        public AdminLanguageStore(string workingDirectory)
        {
            m_Path = Path.Combine(workingDirectory, FileName);
            m_LegacyPath = Path.Combine(workingDirectory, LegacyFileName);
            Load();
        }

        /// <summary>The saved admin UI language, or null if none has been chosen yet.</summary>
        public string? Get()
        {
            lock (m_Lock)
            {
                return m_Language;
            }
        }

        /// <summary>Saves the admin UI language (ignored when blank).</summary>
        public void Set(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            lock (m_Lock)
            {
                m_Language = language.Trim();
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(m_Path))
                {
                    m_Language = ParseLanguage(File.ReadAllLines(m_Path));
                    return;
                }

                // One-time migration from the old plain-text file, if present.
                if (File.Exists(m_LegacyPath))
                {
                    var saved = File.ReadAllText(m_LegacyPath).Trim();
                    if (saved.Length > 0)
                    {
                        m_Language = saved;
                        Save();
                    }
                }
            }
            catch
            {
                // Ignore — treat as "no saved language".
            }
        }

        private static string? ParseLanguage(string[] lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || !line.StartsWith("language:", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = line.Substring("language:".Length).Trim();
                value = YamlScalar.Unquote(value);
                return value.Length > 0 ? value : null;
            }

            return null;
        }

        // Caller holds m_Lock.
        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Admin web-panel UI language. Managed by well404.WebPanel.\n");
                sb.Append("language: ").Append(YamlScalar.Quote(m_Language ?? string.Empty)).Append('\n');
                File.WriteAllText(m_Path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Best-effort; an unwritable data dir just means it won't persist across restarts.
            }
        }
    }
}
