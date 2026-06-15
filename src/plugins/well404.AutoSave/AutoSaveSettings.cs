using System.Collections.Generic;

namespace well404.AutoSave
{
    /// <summary>Strongly-typed view of the Auto Save <c>config.yaml</c>.</summary>
    public sealed class AutoSaveSettings
    {
        public ScheduleSettings Schedule { get; set; } = new ScheduleSettings();

        public BackupSettings Backup { get; set; } = new BackupSettings();

        public RetentionSettings Retention { get; set; } = new RetentionSettings();
    }

    /// <summary>When saves fire.</summary>
    public sealed class ScheduleSettings
    {
        /// <summary>
        /// Standard 5-field cron expression. Saves fire on the wall-clock boundaries it describes,
        /// independent of when the server started. Default: every 10 minutes.
        /// </summary>
        public string Cron { get; set; } = "*/10 * * * *";

        /// <summary>Time zone the cron is evaluated in. Empty = the server's local time zone.</summary>
        public string TimeZone { get; set; } = "";
    }

    /// <summary>Whether and how the savedata is archived.</summary>
    public sealed class BackupSettings
    {
        /// <summary>When false the game is still saved on schedule, just not backed up.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Back up after every Nth save. &lt;= 0 disables backups.</summary>
        public int EveryNSaves { get; set; } = 6;

        /// <summary>
        /// Backup output directory. Empty = "&lt;server install&gt;/Backups/&lt;server id&gt;". An absolute
        /// path is used as-is; a relative path is relative to the Unturned install root.
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// Glob patterns (relative to the savedata root) to exclude from a backup. Everything not
        /// matched is included, so unknown folders are kept by default.
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>
        {
            "Workshop/**",
            "Steam/**",
            "Bundles/**",
            "OpenMod/packages/**",
            "**/logs/**",
            "**/Logs/**",
            "**/*~",
            "**/*.bak"
        };
    }

    /// <summary>Limits that trigger deletion of the oldest backups. 0 = unlimited.</summary>
    public sealed class RetentionSettings
    {
        /// <summary>Maximum number of backups to keep. 0 = no limit.</summary>
        public int MaxCount { get; set; } = 0;

        /// <summary>Maximum total size of all backups, in megabytes. 0 = no limit.</summary>
        public long MaxTotalSizeMB { get; set; } = 0;
    }
}
