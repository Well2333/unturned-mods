using System;
using System.Collections.Generic;
using System.IO;

namespace well404.WebPanel
{
    /// <summary>
    /// Remembers each player's chosen panel language across sessions and restarts. When a player
    /// switches the language on their web panel it is saved here (keyed by Steam ID) and reused the
    /// next time they open the panel — even from another device. Persisted as a small
    /// <c>player-languages.txt</c> (<c>steamId=lang</c> per line) in the plugin's working directory.
    /// </summary>
    public sealed class PlayerLanguageStore
    {
        private const string FileName = "player-languages.txt";

        private readonly string m_Path;
        private readonly object m_Lock = new object();
        private readonly Dictionary<string, string> m_Languages = new Dictionary<string, string>(StringComparer.Ordinal);

        public PlayerLanguageStore(string workingDirectory)
        {
            m_Path = Path.Combine(workingDirectory, FileName);
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
                if (!File.Exists(m_Path))
                {
                    return;
                }

                foreach (var raw in File.ReadAllLines(m_Path))
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
            }
            catch
            {
                // A missing/corrupt file just means no saved preferences yet.
            }
        }

        // Caller holds m_Lock.
        private void Save()
        {
            try
            {
                var lines = new List<string>(m_Languages.Count);
                foreach (var pair in m_Languages)
                {
                    lines.Add(pair.Key + "=" + pair.Value);
                }

                File.WriteAllLines(m_Path, lines);
            }
            catch
            {
                // Best-effort; an unwritable file just means the choice won't survive a restart.
            }
        }
    }
}
