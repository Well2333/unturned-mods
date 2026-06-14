using System;
using System.IO;
using System.Text;

namespace well404.WebPanel
{
    /// <summary>
    /// Persists the admin panel's chosen UI language on the server (a single value in
    /// <c>admin-language.txt</c> in the working directory), so it survives the admin URL changing
    /// (e.g. a fresh quick-tunnel domain) — browser localStorage is per-origin and would be lost.
    /// The admin surface has no per-user identity (it is gated by one secret token), so one shared
    /// value is enough. Thread-safe.
    /// </summary>
    public sealed class AdminLanguageStore
    {
        private readonly string m_Path;
        private readonly object m_Lock = new object();
        private string? m_Language;

        public AdminLanguageStore(string workingDirectory)
        {
            m_Path = Path.Combine(workingDirectory, "admin-language.txt");
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
                try
                {
                    File.WriteAllText(m_Path, m_Language, new UTF8Encoding(false));
                }
                catch
                {
                    // Best-effort; an unwritable data dir just means it won't persist across restarts.
                }
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(m_Path))
                {
                    var saved = File.ReadAllText(m_Path).Trim();
                    if (saved.Length > 0)
                    {
                        m_Language = saved;
                    }
                }
            }
            catch
            {
                // Ignore — treat as "no saved language".
            }
        }
    }
}
