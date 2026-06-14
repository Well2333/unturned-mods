using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace well404.Vault
{
    /// <summary>
    /// The web panel's view of the vault's config settings (base capacity + permission tiers),
    /// persisted by rewriting the working-directory <c>config.yaml</c> (so OpenMod's config reload
    /// picks the change up for the game side). Seeded once from <see cref="IConfiguration"/> at
    /// construction, like the other plugins' config stores. Per-player overrides are NOT here — those
    /// are runtime data managed by <see cref="VaultService"/>.
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

        public IReadOnlyDictionary<string, int> Tiers
        {
            get { lock (m_Lock) { return new Dictionary<string, int>(m_Settings.Tiers); } }
        }

        public void Save(int maxSlots, Dictionary<string, int> tiers)
        {
            lock (m_Lock)
            {
                m_Settings.MaxSlots = Math.Max(1, maxSlots);
                m_Settings.Tiers = tiers;
                WriteFile();
            }
        }

        // Caller holds m_Lock.
        private void WriteFile()
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Personal Vault.\n");
            sb.Append("# This file is rewritten by well404.WebPanel when settings are edited.\n\n");
            sb.Append("# Base capacity in inventory grid cells (everyone gets at least this).\n");
            sb.Append("maxSlots: ").Append(m_Settings.MaxSlots.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# Per-permission capacity tiers; a player gets the largest of the base and the tiers they hold.\n");
            if (m_Settings.Tiers.Count == 0)
            {
                sb.Append("tiers: {}\n");
            }
            else
            {
                sb.Append("tiers:\n");
                foreach (var tier in m_Settings.Tiers)
                {
                    sb.Append("  \"").Append(tier.Key.Replace("\\", "\\\\").Replace("\"", "\\\""))
                        .Append("\": ").Append(tier.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
                }
            }

            File.WriteAllText(m_ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
