using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace well404.WebPanel
{
    /// <summary>
    /// Remembers each player's chosen panel language across sessions and restarts. When a player
    /// switches the language on their web panel it is saved here (keyed by Steam ID) and reused the
    /// next time they open the panel — even from another device or a new panel URL. Persisted as a
    /// YAML config file <c>player-languages.yaml</c> (a <c>players:</c> map of Steam ID → language)
    /// in the plugin's working directory.
    /// </summary>
    public sealed class PlayerLanguageStore
    {
        private const string FileName = "player-languages.yaml";
        private const string LegacyFileName = "player-languages.txt";

        private readonly string m_Path;
        private readonly string m_LegacyPath;
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, string> m_Languages = new Dictionary<string, string>(StringComparer.Ordinal);

        public PlayerLanguageStore(string workingDirectory)
        {
            m_Path = Path.Combine(workingDirectory, FileName);
            m_LegacyPath = Path.Combine(workingDirectory, LegacyFileName);
            Load();
        }

        /// <summary>The saved language for a player, or null if they never chose one.</summary>
        public string? Get(string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            lock (m_Lock)
            {
                return m_Languages.TryGetValue(steamId, out var lang) ? lang : null;
            }
        }

        /// <summary>Saves a player's chosen language (best-effort persisted to disk).</summary>
        public void Set(string steamId, string language)
        {
            if (string.IsNullOrEmpty(steamId) || string.IsNullOrWhiteSpace(language))
            {
                return;
            }

            language = language.Trim();
            lock (m_Lock)
            {
                if (m_Languages.TryGetValue(steamId, out var existing) && existing == language)
                {
                    return;
                }

                m_Languages[steamId] = language;
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(m_Path))
                {
                    ParseYaml(File.ReadAllLines(m_Path));
                    return;
                }

                // One-time migration from the old "steamId=lang" text file, if present.
                if (File.Exists(m_LegacyPath))
                {
                    foreach (var raw in File.ReadAllLines(m_LegacyPath))
                    {
                        var line = raw.Trim();
                        var eq = line.IndexOf('=');
                        if (eq <= 0)
                        {
                            continue;
                        }

                        var id = line.Substring(0, eq).Trim();
                        var lang = line.Substring(eq + 1).Trim();
                        if (id.Length > 0 && lang.Length > 0)
                        {
                            m_Languages[id] = lang;
                        }
                    }

                    if (m_Languages.Count > 0)
                    {
                        Save();
                    }
                }
            }
            catch
            {
                // A missing/corrupt file just means no saved preferences yet.
            }
        }

        // Parses the flat "players:" map; keys and values may be quoted.
        private void ParseYaml(IEnumerable<string> lines)
        {
            var inPlayers = false;
            foreach (var raw in lines)
            {
                if (raw.Length == 0)
                {
                    continue;
                }

                var trimmed = raw.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                // A non-indented line is a top-level key; we only care about "players:".
                if (!char.IsWhiteSpace(raw[0]))
                {
                    inPlayers = trimmed.StartsWith("players:", StringComparison.Ordinal);
                    continue;
                }

                if (!inPlayers)
                {
                    continue;
                }

                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var id = Unquote(trimmed.Substring(0, colon).Trim());
                var lang = Unquote(trimmed.Substring(colon + 1).Trim());
                if (id.Length > 0 && lang.Length > 0)
                {
                    m_Languages[id] = lang;
                }
            }
        }

        // Caller holds m_Lock.
        private void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Player web-panel language preferences, by Steam ID. Managed by well404.WebPanel.\n");
                if (m_Languages.Count == 0)
                {
                    sb.Append("players: {}\n");
                }
                else
                {
                    sb.Append("players:\n");
                    foreach (var pair in m_Languages)
                    {
                        sb.Append("  \"").Append(pair.Key).Append("\": \"").Append(pair.Value).Append("\"\n");
                    }
                }

                File.WriteAllText(m_Path, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // Best-effort; an unwritable file just means the choice won't survive a restart.
            }
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}
