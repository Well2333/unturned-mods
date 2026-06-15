using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using UnturnedMods.Shared.WebPanel;

namespace well404.AutoSave
{
    /// <summary>
    /// Builds Auto Save's <see cref="WebPanelModule"/>: a settings group (schedule, backup and
    /// retention), a "Back up now" button, and a table of existing backups with a per-row delete.
    /// English source strings are i18n keys localized per request via <see cref="IWebTranslationRegistry"/>.
    /// </summary>
    internal sealed class AutoSaveWebPanelModule
    {
        public const string ModuleId = "well404.autosave";

        private readonly AutoSaveConfigStore m_Config;
        private readonly SaveService m_Save;
        private readonly BackupService m_Backup;
        private readonly Func<SavePaths> m_PathsFactory;
        private readonly IWebTranslationRegistry m_Tr;

        private AutoSaveWebPanelModule(
            AutoSaveConfigStore config, SaveService save, BackupService backup, Func<SavePaths> pathsFactory, IWebTranslationRegistry tr)
        {
            m_Config = config;
            m_Save = save;
            m_Backup = backup;
            m_PathsFactory = pathsFactory;
            m_Tr = tr;
        }

        public static WebPanelModule Create(
            AutoSaveConfigStore config, SaveService save, BackupService backup, Func<SavePaths> pathsFactory, IWebTranslationRegistry tr)
        {
            var module = new AutoSaveWebPanelModule(config, save, backup, pathsFactory, tr);

            var settings = new WebPanelAction(
                id: "settings",
                label: "Schedule & backup",
                kind: WebActionKind.Settings,
                handler: request => Task.FromResult(module.SaveSettings(request)),
                fields: new[]
                {
                    new WebField("cron", "Cron expression", WebFieldType.Text, required: true,
                        placeholder: "minute hour day-of-month month day-of-week — e.g. */10 * * * *"),
                    new WebField("timeZone", "Time zone", WebFieldType.Text,
                        placeholder: "Empty = server local; e.g. Asia/Shanghai"),
                    new WebField("enabled", "Enable backups", WebFieldType.Boolean),
                    new WebField("everyNSaves", "Back up every N saves", WebFieldType.Number,
                        placeholder: "e.g. 6 — a backup after every 6th save"),
                    new WebField("directory", "Backup directory", WebFieldType.Text,
                        placeholder: "Empty = <install>/Backups/<server id>"),
                    new WebField("excludePatterns", "Exclude patterns", WebFieldType.TextArea,
                        placeholder: "One glob per line, relative to the savedata root (e.g. Workshop/**)"),
                    new WebField("maxCount", "Max backups (0 = unlimited)", WebFieldType.Number),
                    new WebField("maxTotalSizeMB", "Max total size MB (0 = unlimited)", WebFieldType.Number)
                },
                description: "When saves fire (cron, wall-clock aligned), how often a backup is taken, where backups go, what to exclude, and how many/how large to keep. Backups use solid LZMA (.tar.lz).",
                loader: () => Task.FromResult(module.LoadSettings()));

            var backupNow = new WebPanelAction(
                id: "backupNow",
                label: "Back up now",
                kind: WebActionKind.Form,
                handler: module.BackupNowAsync,
                description: "Save the game and write a backup immediately (runs even if scheduled backups are off).");

            var backups = new WebPanelAction(
                id: "backups",
                label: "Backups",
                kind: WebActionKind.Table,
                handler: module.ListBackupsAsync,
                description: "Existing backup archives (newest first).");

            var delete = new WebPanelAction(
                id: "delete",
                label: "Delete",
                kind: WebActionKind.Form,
                handler: module.DeleteBackupAsync,
                hidden: true);

            return new WebPanelModule(ModuleId, "Auto Save", new[] { settings, backupNow, backups, delete }, icon: "💾");
        }

        private IReadOnlyDictionary<string, string> LoadSettings()
        {
            var s = m_Config.Current;
            return new Dictionary<string, string>
            {
                ["cron"] = s.Schedule.Cron,
                ["timeZone"] = s.Schedule.TimeZone,
                ["enabled"] = s.Backup.Enabled ? "true" : "false",
                ["everyNSaves"] = s.Backup.EveryNSaves.ToString(CultureInfo.InvariantCulture),
                ["directory"] = s.Backup.Directory,
                ["excludePatterns"] = string.Join("\n", s.Backup.ExcludePatterns),
                ["maxCount"] = s.Retention.MaxCount.ToString(CultureInfo.InvariantCulture),
                ["maxTotalSizeMB"] = s.Retention.MaxTotalSizeMB.ToString(CultureInfo.InvariantCulture)
            };
        }

        private WebActionResult SaveSettings(WebActionRequest request)
        {
            var cron = request.Get("cron");
            var cronError = CronSchedule.Validate(cron ?? string.Empty);
            if (cronError != null)
            {
                return WebActionResult.Fail(m_Tr.Format(request.Language, "Invalid cron expression: {0}", cronError));
            }

            var timeZone = request.Get("timeZone") ?? string.Empty;
            if (!AutoSaveTimeZone.TryResolve(timeZone, out _))
            {
                return WebActionResult.Fail(m_Tr.Resolve("Unknown time zone.", request.Language));
            }

            var everyNRaw = request.Get("everyNSaves");
            if (everyNRaw == null
                || !int.TryParse(everyNRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var everyN))
            {
                return WebActionResult.Fail(m_Tr.Resolve("Enter a whole number for 'Back up every N saves'.", request.Language));
            }

            var maxCount = ParseNonNegativeInt(request.Get("maxCount"));
            var maxSizeMb = ParseNonNegativeLong(request.Get("maxTotalSizeMB"));

            var excludeText = request.Get("excludePatterns") ?? string.Empty;
            var excludes = excludeText
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

            var settings = new AutoSaveSettings
            {
                Schedule = new ScheduleSettings { Cron = cron!, TimeZone = timeZone },
                Backup = new BackupSettings
                {
                    Enabled = request.Get("enabled") != "false",
                    EveryNSaves = everyN,
                    Directory = request.Get("directory") ?? string.Empty,
                    ExcludePatterns = excludes
                },
                Retention = new RetentionSettings { MaxCount = maxCount, MaxTotalSizeMB = maxSizeMb }
            };

            m_Config.Save(settings);
            return WebActionResult.Ok("Saved.");
        }

        private async Task<WebActionResult> BackupNowAsync(WebActionRequest request)
        {
            var result = await m_Save.RunManualAsync();
            if (result.Status == BackupStatus.Failed && result.Error == "server-not-ready")
            {
                return WebActionResult.Fail(m_Tr.Resolve("The server is not fully loaded yet.", request.Language));
            }

            return result.Status switch
            {
                BackupStatus.Success => WebActionResult.Ok(m_Tr.Format(request.Language,
                    "Saved and backed up: {0} ({1}, {2} files).",
                    result.Name!, BackupService.FormatSize(result.SizeBytes), result.FileCount)),
                BackupStatus.Skipped => WebActionResult.Ok(m_Tr.Resolve(
                    "Saved. A backup was already running, so a new one was skipped.", request.Language)),
                _ => WebActionResult.Fail(m_Tr.Format(request.Language, "Saved, but the backup failed: {0}", result.Error ?? ""))
            };
        }

        private Task<WebActionResult> ListBackupsAsync(WebActionRequest request)
        {
            var lang = request.Language;
            var list = m_Backup.ListBackups(m_PathsFactory(), m_Config.Current.Backup);
            var rows = list
                .Select(b => (IReadOnlyList<string>)new[]
                {
                    b.Name,
                    BackupService.FormatSize(b.SizeBytes),
                    b.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                })
                .ToList();

            var message = list.Count == 0 ? m_Tr.Resolve("No backups yet.", lang) : null;
            return Task.FromResult(WebActionResult
                .Table(new[] { "Name", "Size", "Date" }, rows, message)
                .WithRowAction("delete", "Delete", list.Select(b => b.Name).ToList()));
        }

        private Task<WebActionResult> DeleteBackupAsync(WebActionRequest request)
        {
            var name = request.Get("key");
            if (name == null)
            {
                return Task.FromResult(WebActionResult.Fail(m_Tr.Resolve("Not found.", request.Language)));
            }

            return Task.FromResult(m_Backup.DeleteBackup(m_PathsFactory(), m_Config.Current.Backup, name)
                ? WebActionResult.Ok("Deleted.")
                : WebActionResult.Fail(m_Tr.Resolve("Not found.", request.Language)));
        }

        private static int ParseNonNegativeInt(string? raw)
            => raw != null && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;

        private static long ParseNonNegativeLong(string? raw)
            => raw != null && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 0;
    }
}
