using System;
using System.IO;

namespace well404.WebPanel
{
    /// <summary>
    /// Persists the single, shared server-intro Markdown shown on the player panel's home page.
    /// Stored as <c>intro.md</c> in the plugin's working directory so admins can also edit it on
    /// disk; the web panel edits the same file. One shared text for all languages (by design).
    /// </summary>
    public sealed class IntroStore
    {
        private const string FileName = "intro.md";

        private const string DefaultIntro =
            "# Welcome\n\n" +
            "Edit this introduction in the web panel (admin → *Server intro*) or by editing "
            + "`intro.md` in the plugin folder. Markdown is supported.\n\n"
            + "Below are the commands you can use.";

        private readonly string m_Path;

        public IntroStore(string workingDirectory)
        {
            m_Path = Path.Combine(workingDirectory, FileName);
        }

        public string Read()
        {
            try
            {
                return File.Exists(m_Path) ? File.ReadAllText(m_Path) : DefaultIntro;
            }
            catch
            {
                return DefaultIntro;
            }
        }

        public void Write(string markdown)
        {
            File.WriteAllText(m_Path, markdown ?? string.Empty);
        }
    }
}
