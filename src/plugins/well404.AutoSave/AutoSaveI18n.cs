using System.Collections.Generic;

namespace well404.AutoSave
{
    /// <summary>Chinese translations for Auto Save's web strings (English source strings are the keys).</summary>
    internal static class AutoSaveI18n
    {
        public const string Zh = "zh";

        public static readonly IReadOnlyDictionary<string, string> ZhTable = new Dictionary<string, string>
        {
            // ---- module chrome / action labels ----
            ["Auto Save"] = "自动保存",
            ["Schedule & backup"] = "计划与备份",
            ["Back up now"] = "立即备份",
            ["Backups"] = "备份列表",
            ["Delete"] = "删除",

            // ---- field labels ----
            ["Cron expression"] = "Cron 表达式",
            ["Time zone"] = "时区",
            ["Enable backups"] = "启用备份",
            ["Back up every N saves"] = "每 N 次保存备份一次",
            ["Backup directory"] = "备份目录",
            ["Exclude patterns"] = "排除规则",
            ["Max backups (0 = unlimited)"] = "最大备份数（0=不限）",
            ["Max total size MB (0 = unlimited)"] = "最大总体积 MB（0=不限）",

            // ---- placeholders ----
            ["minute hour day-of-month month day-of-week — e.g. */10 * * * *"] = "分 时 日 月 周 —— 例如 */10 * * * *",
            ["Empty = server local; e.g. Asia/Shanghai"] = "留空=服务器本地时区；例如 Asia/Shanghai",
            ["e.g. 6 — a backup after every 6th save"] = "例如 6 —— 每第 6 次保存后备份",
            ["Empty = <install>/Backups/<server id>"] = "留空=<安装目录>/Backups/<服务器id>",
            ["One glob per line, relative to the savedata root (e.g. Workshop/**)"] = "每行一个 glob，相对存档根目录（如 Workshop/**）",

            // ---- descriptions ----
            ["When saves fire (cron, wall-clock aligned), how often a backup is taken, where backups go, what to exclude, and how many/how large to keep. Backups use solid LZMA (.tar.lz)."]
                = "保存何时触发（cron，按墙钟对齐）、多久备份一次、备份存放位置、排除哪些内容、保留多少/多大。备份采用 LZMA 实体压缩（.tar.lz）。",
            ["Save the game and write a backup immediately (runs even if scheduled backups are off)."]
                = "立即保存游戏并写入一次备份（即使关闭了定时备份也会执行）。",
            ["Existing backup archives (newest first)."] = "现有备份文件（最新在前）。",

            // ---- table columns ----
            ["Name"] = "名称",
            ["Size"] = "大小",
            ["Date"] = "时间",

            // ---- result messages / templates ----
            ["Invalid cron expression: {0}"] = "无效的 cron 表达式：{0}",
            ["Unknown time zone."] = "未知的时区。",
            ["Enter a whole number for 'Back up every N saves'."] = "请为「每 N 次保存备份一次」输入整数。",
            ["Saved."] = "已保存。",
            ["The server is not fully loaded yet."] = "服务器尚未完全加载。",
            ["Saved and backed up: {0} ({1}, {2} files)."] = "已保存并备份：{0}（{1}，{2} 个文件）。",
            ["Saved. A backup was already running, so a new one was skipped."] = "已保存。已有备份正在进行，本次跳过新备份。",
            ["Saved, but the backup failed: {0}"] = "已保存，但备份失败：{0}",
            ["No backups yet."] = "暂无备份。",
            ["Not found."] = "未找到。",
            ["Deleted."] = "已删除。"
        };
    }
}
