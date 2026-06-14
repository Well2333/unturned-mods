using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace well404.Vault
{
    /// <summary>
    /// The web panel's view of the vault's settings, persisted by rewriting the working-directory
    /// <c>config.yaml</c> (so OpenMod's config reload picks the change up for the game side). Seeded
    /// once from <see cref="IConfiguration"/> at construction, like the other plugins' config stores.
    /// </summary>
    public sealed class VaultConfigStore
    {
        private readonly string m_ConfigPath;
        private readonly object m_Lock = new object();
        private readonly VaultSettings m_Settings;

        public VaultConfigStore(IConfiguration configuration, string workingDirectory)
        {
            m_ConfigPath = Path.Combine(workingDirectory, "config.yaml");
            m_Settings = configuration.Get<VaultSettings>() ?? new VaultSettings();
        }

        public int MaxSlots
        {
            get { lock (m_Lock) { return m_Settings.MaxSlots; } }
        }

        public void SetMaxSlots(int slots)
        {
            lock (m_Lock)
            {
                m_Settings.MaxSlots = Math.Max(1, slots);
                Save();
            }
        }

        // Caller holds m_Lock.
        private void Save()
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Personal Vault.\n");
            sb.Append("# This file is rewritten by well404.WebPanel when settings are edited.\n\n");
            sb.Append("# Total vault capacity in inventory grid cells (each item costs its size_x*size_y footprint).\n");
            sb.Append("maxSlots: ").Append(m_Settings.MaxSlots.ToString(CultureInfo.InvariantCulture)).Append('\n');
            File.WriteAllText(m_ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
