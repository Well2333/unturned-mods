using System;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace well404.Economy
{
    /// <summary>
    /// The web panel's authoritative, in-memory view of the economy settings, persisted by
    /// rewriting the working-directory <c>config.yaml</c>. <see cref="EconomyProvider"/>
    /// re-reads <see cref="IConfiguration"/> on each call, so it picks up the change once
    /// OpenMod's file-watch reloads (sub-second).
    /// <para>
    /// Seeds its copy once from <see cref="IConfiguration"/> at construction and never
    /// re-reads it, so a burst of edits cannot race against the asynchronous, debounced
    /// config reload (see the same note on <c>well404.Shop.ShopConfigStore</c>).
    /// </para>
    /// </summary>
    public sealed class EconomyConfigStore
    {
        private readonly string m_ConfigPath;
        private readonly object m_Lock = new object();
        private readonly EconomySettings m_Settings;

        public EconomyConfigStore(IConfiguration configuration, string workingDirectory)
        {
            m_ConfigPath = Path.Combine(workingDirectory, "config.yaml");
            m_Settings = configuration.Get<EconomySettings>() ?? new EconomySettings();
        }

        /// <summary>Runs <paramref name="reader"/> against the current settings under the lock.</summary>
        public T Read<T>(Func<EconomySettings, T> reader)
        {
            lock (m_Lock)
            {
                return reader(m_Settings);
            }
        }

        /// <summary>Applies <paramref name="mutate"/> to the settings and rewrites the config file.</summary>
        public void Update(Action<EconomySettings> mutate)
        {
            lock (m_Lock)
            {
                mutate(m_Settings);
                File.WriteAllText(m_ConfigPath, EconomyYaml.Serialize(m_Settings), new UTF8Encoding(false));
            }
        }
    }
}
