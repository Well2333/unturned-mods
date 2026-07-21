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
    /// construction, like the other plugins config stores. Runtime container capacity adjustments
    /// are stored in SQLite and managed by <see cref="VaultService"/>.
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

        public PersonalVaultPurchaseSettings PersonalPurchase
        {
            get
            {
                lock (m_Lock)
                {
                    var source = m_Settings.PersonalPurchase ?? new PersonalVaultPurchaseSettings();
                    return new PersonalVaultPurchaseSettings
                    {
                        Enabled = source.Enabled,
                        MaxSlots = source.MaxSlots,
                        SlotsPerPurchase = source.SlotsPerPurchase,
                        Price = source.Price
                    };
                }
            }
        }

        public TeamVaultSettings TeamVault
        {
            get
            {
                lock (m_Lock)
                {
                    var source = m_Settings.TeamVault ?? new TeamVaultSettings();
                    return new TeamVaultSettings
                    {
                        Enabled = source.Enabled,
                        BaseSlots = source.BaseSlots,
                        MaxSlots = source.MaxSlots,
                        Purchase = new TeamVaultPurchaseSettings
                        {
                            Enabled = source.Purchase?.Enabled ?? true,
                            SlotsPerPurchase = source.Purchase?.SlotsPerPurchase ?? 10,
                            Price = source.Purchase?.Price ?? 500m
                        }
                    };
                }
            }
        }

        public void Save(int maxSlots, Dictionary<string, int> tiers,
            PersonalVaultPurchaseSettings? personalPurchase = null, TeamVaultSettings? teamVault = null)
        {
            lock (m_Lock)
            {
                m_Settings.MaxSlots = Math.Max(1, maxSlots);
                m_Settings.Tiers = tiers;
                if (personalPurchase != null)
                {
                    personalPurchase.MaxSlots = Math.Max(m_Settings.MaxSlots, personalPurchase.MaxSlots);
                    personalPurchase.SlotsPerPurchase = Math.Max(1, personalPurchase.SlotsPerPurchase);
                    personalPurchase.Price = Math.Max(0m, personalPurchase.Price);
                    m_Settings.PersonalPurchase = personalPurchase;
                }
                if (teamVault != null)
                {
                    teamVault.BaseSlots = Math.Max(1, teamVault.BaseSlots);
                    teamVault.MaxSlots = Math.Max(teamVault.BaseSlots, teamVault.MaxSlots);
                    teamVault.Purchase ??= new TeamVaultPurchaseSettings();
                    teamVault.Purchase.SlotsPerPurchase = Math.Max(1, teamVault.Purchase.SlotsPerPurchase);
                    teamVault.Purchase.Price = Math.Max(0m, teamVault.Purchase.Price);
                    m_Settings.TeamVault = teamVault;
                }
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

            var personal = m_Settings.PersonalPurchase ?? new PersonalVaultPurchaseSettings();
            sb.Append("\n# Personal vault capacity purchases.\n");
            sb.Append("personalPurchase:\n");
            sb.Append("  enabled: ").Append(personal.Enabled ? "true" : "false").Append("\n");
            sb.Append("  maxSlots: ").Append(Math.Max(m_Settings.MaxSlots, personal.MaxSlots).ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("  slotsPerPurchase: ").Append(Math.Max(1, personal.SlotsPerPurchase).ToString(CultureInfo.InvariantCulture)).Append("\n");
            sb.Append("  price: ").Append(Math.Max(0m, personal.Price).ToString(CultureInfo.InvariantCulture)).Append("\n");

            var team = m_Settings.TeamVault ?? new TeamVaultSettings();
            var purchase = team.Purchase ?? new TeamVaultPurchaseSettings();
            sb.Append('\n');
            sb.Append("# Shared Unturned-party vault. Members buy capacity with their own balance.\n");
            sb.Append("teamVault:\n");
            sb.Append("  enabled: ").Append(team.Enabled ? "true" : "false").Append('\n');
            sb.Append("  baseSlots: ").Append(Math.Max(1, team.BaseSlots).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  maxSlots: ").Append(Math.Max(team.BaseSlots, team.MaxSlots).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("  purchase:\n");
            sb.Append("    enabled: ").Append(purchase.Enabled ? "true" : "false").Append('\n');
            sb.Append("    slotsPerPurchase: ").Append(Math.Max(1, purchase.SlotsPerPurchase).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("    price: ").Append(Math.Max(0m, purchase.Price).ToString(CultureInfo.InvariantCulture)).Append('\n');

            File.WriteAllText(m_ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
