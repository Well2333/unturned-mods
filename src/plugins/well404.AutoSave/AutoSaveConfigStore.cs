using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace well404.AutoSave
{
    /// <summary>
    /// The single source of truth for Auto Save's settings at runtime. Seeded from
    /// <see cref="IConfiguration"/> at construction; edits (from the web panel) replace the settings
    /// wholesale and rewrite the working-directory <c>config.yaml</c> so OpenMod's config reload and a
    /// future restart see the same values. Raises <see cref="Changed"/> after a save so the plugin can
    /// restart the scheduler with the new cron. Mirrors the other plugins' config stores.
    /// </summary>
    public sealed class AutoSaveConfigStore
    {
        private readonly string m_ConfigPath;
        private readonly object m_Lock = new object();
        private AutoSaveSettings m_Settings;

        public AutoSaveConfigStore(IConfiguration configuration, string workingDirectory)
        {
            m_ConfigPath = Path.Combine(workingDirectory, "config.yaml");
            m_Settings = configuration.Get<AutoSaveSettings>() ?? new AutoSaveSettings();
            Normalize(m_Settings);
        }

        /// <summary>Raised after settings are saved (so the scheduler can be rebuilt).</summary>
        public event Action? Changed;

        /// <summary>The current settings. Replaced wholesale on save, never mutated in place.</summary>
        public AutoSaveSettings Current
        {
            get { lock (m_Lock) { return m_Settings; } }
        }

        public void Save(AutoSaveSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            Normalize(settings);
            lock (m_Lock)
            {
                m_Settings = settings;
                WriteFile(settings);
            }

            Changed?.Invoke();
        }

        private static void Normalize(AutoSaveSettings settings)
        {
            settings.Schedule ??= new ScheduleSettings();
            settings.Backup ??= new BackupSettings();
            settings.Retention ??= new RetentionSettings();
            settings.Schedule.Cron ??= "*/10 * * * *";
            settings.Schedule.TimeZone ??= "";
            settings.Backup.Directory ??= "";
            settings.Backup.ExcludePatterns ??= new List<string>();
            if (settings.Retention.MaxCount < 0) settings.Retention.MaxCount = 0;
            if (settings.Retention.MaxTotalSizeMB < 0) settings.Retention.MaxTotalSizeMB = 0;
        }

        // Caller holds m_Lock.
        private void WriteFile(AutoSaveSettings s)
        {
            var sb = new StringBuilder();
            sb.Append("# Configuration for Auto Save.\n");
            sb.Append("# This file is rewritten by well404.WebPanel when settings are edited.\n\n");

            sb.Append("schedule:\n");
            sb.Append("  # Standard 5-field cron; saves fire on these wall-clock boundaries.\n");
            sb.Append("  cron: ").Append(Quote(s.Schedule.Cron)).Append('\n');
            sb.Append("  # Empty = the server's local time zone; otherwise an IANA or Windows time-zone id.\n");
            sb.Append("  timeZone: ").Append(Quote(s.Schedule.TimeZone)).Append("\n\n");

            sb.Append("backup:\n");
            sb.Append("  enabled: ").Append(Bool(s.Backup.Enabled)).Append('\n');
            sb.Append("  # Back up after every Nth save; <= 0 disables backups.\n");
            sb.Append("  everyNSaves: ").Append(Int(s.Backup.EveryNSaves)).Append('\n');
            sb.Append("  # Empty = \"<install>/Backups/<server id>\".\n");
            sb.Append("  directory: ").Append(Quote(s.Backup.Directory)).Append('\n');
            sb.Append("  # Globs (relative to the savedata root) to exclude from a backup.\n");
            if (s.Backup.ExcludePatterns.Count == 0)
            {
                sb.Append("  excludePatterns: []\n");
            }
            else
            {
                sb.Append("  excludePatterns:\n");
                foreach (var pattern in s.Backup.ExcludePatterns)
                {
                    sb.Append("    - ").Append(Quote(pattern)).Append('\n');
                }
            }

            sb.Append('\n');
            sb.Append("retention:\n");
            sb.Append("  # Delete the oldest backups once EITHER limit is exceeded. 0 = no limit.\n");
            sb.Append("  maxCount: ").Append(Int(s.Retention.MaxCount)).Append('\n');
            sb.Append("  maxTotalSizeMB: ").Append(Long(s.Retention.MaxTotalSizeMB)).Append('\n');

            File.WriteAllText(m_ConfigPath, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Quote(string? value)
        {
            value ??= "";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
