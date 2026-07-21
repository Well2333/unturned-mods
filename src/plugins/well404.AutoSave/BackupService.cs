using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace well404.AutoSave
{
    public enum BackupStatus
    {
        Success,
        Skipped,
        Failed
    }

    /// <summary>The outcome of a backup attempt.</summary>
    public sealed class BackupResult
    {
        private BackupResult(BackupStatus status, string? name, long sizeBytes, int fileCount, string? error)
        {
            Status = status;
            Name = name;
            SizeBytes = sizeBytes;
            FileCount = fileCount;
            Error = error;
        }

        public BackupStatus Status { get; }
        public string? Name { get; }
        public long SizeBytes { get; }
        public int FileCount { get; }
        public string? Error { get; }

        public static BackupResult Success(string name, long sizeBytes, int fileCount)
            => new BackupResult(BackupStatus.Success, name, sizeBytes, fileCount, null);

        public static BackupResult Skipped() => new BackupResult(BackupStatus.Skipped, null, 0, 0, null);

        public static BackupResult Failed(string error) => new BackupResult(BackupStatus.Failed, null, 0, 0, error);
    }

    /// <summary>
    /// Snapshots the savedata into a single compressed archive and enforces the retention caps. The
    /// work is plain file IO (the game was already saved synchronously on the main thread before this
    /// runs), so it executes off the main thread. A gate ensures only one backup runs at a time.
    /// Scheduled and manual overlaps are skipped. Callers may explicitly serialize other requests when needed;
    /// </summary>
    public sealed class BackupService
    {
        private readonly IBackupArchiver m_Archiver;
        private readonly ILogger m_Logger;
        private readonly SemaphoreSlim m_Gate = new SemaphoreSlim(1, 1);

        public BackupService(IBackupArchiver archiver, ILogger logger)
        {
            m_Archiver = archiver;
            m_Logger = logger;
        }

        /// <summary>Creates a backup now (blocking file IO; call from a background thread).</summary>
        public BackupResult Run(SavePaths paths, BackupSettings backup, RetentionSettings retention)
            => Run(paths, backup, retention, waitForPrevious: false);

        public BackupResult Run(
            SavePaths paths,
            BackupSettings backup,
            RetentionSettings retention,
            bool waitForPrevious)
        {
            if (waitForPrevious)
            {
                m_Gate.Wait();
            }
            else if (!m_Gate.Wait(0))
            {
                m_Logger.LogWarning("Auto Save: a previous backup is still running; skipping this one.");
                return BackupResult.Skipped();
            }

            try
            {
                var backupDir = paths.ResolveBackupDirectory(backup.Directory);
                Directory.CreateDirectory(backupDir);

                var matcher = new ExcludeMatcher(backup.ExcludePatterns ?? new List<string>());
                var entries = CollectEntries(paths.SavedataRoot, backupDir, matcher);
                if (entries.Count == 0)
                {
                    m_Logger.LogWarning("Auto Save: nothing to back up under {Root} (all excluded?).", paths.SavedataRoot);
                }

                var name = "autosave-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + m_Archiver.Extension;
                var outPath = Path.Combine(backupDir, name);
                var tmpPath = outPath + ".tmp";

                try
                {
                    m_Archiver.Create(tmpPath, entries);
                    if (File.Exists(outPath))
                    {
                        File.Delete(outPath);
                    }

                    File.Move(tmpPath, outPath);
                }
                finally
                {
                    if (File.Exists(tmpPath))
                    {
                        TryDelete(tmpPath);
                    }
                }

                var size = new FileInfo(outPath).Length;
                m_Logger.LogInformation(
                    "Auto Save: backup '{Name}' written ({Files} files, {Size}).",
                    name, entries.Count, FormatSize(size));

                ApplyRetention(backupDir, retention);
                return BackupResult.Success(name, size, entries.Count);
            }
            catch (Exception ex)
            {
                m_Logger.LogError(ex, "Auto Save: backup failed.");
                return BackupResult.Failed(ex.Message);
            }
            finally
            {
                m_Gate.Release();
            }
        }

        /// <summary>Lists existing backups (newest first). Pure file IO.</summary>
        public IReadOnlyList<BackupInfo> ListBackups(SavePaths paths, BackupSettings backup)
        {
            var backupDir = paths.ResolveBackupDirectory(backup.Directory);
            return ReadBackups(backupDir)
                .OrderByDescending(b => b.TimestampUtc)
                .ToList();
        }

        /// <summary>Deletes one backup by file name. Returns false if it is missing or invalid.</summary>
        public bool DeleteBackup(SavePaths paths, BackupSettings backup, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 || name.Contains(".."))
            {
                return false;
            }

            var backupDir = paths.ResolveBackupDirectory(backup.Directory);
            var path = Path.Combine(backupDir, name);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                File.Delete(path);
                m_Logger.LogInformation("Auto Save: deleted backup '{Name}'.", name);
                return true;
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Auto Save: could not delete backup '{Name}'.", name);
                return false;
            }
        }

        private List<BackupEntry> CollectEntries(string savedataRoot, string backupDir, ExcludeMatcher matcher)
        {
            var rootFull = Path.GetFullPath(savedataRoot);
            var backupFull = EnsureTrailingSeparator(Path.GetFullPath(backupDir));
            var entries = new List<BackupEntry>();
            Walk(rootFull, "", backupFull, matcher, entries);
            return entries;
        }

        private void Walk(string absDir, string relDir, string backupFull, ExcludeMatcher matcher, List<BackupEntry> entries)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(absDir);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Auto Save: skipping unreadable directory {Dir}.", absDir);
                return;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var rel = relDir.Length == 0 ? name : relDir + "/" + name;
                if (matcher.IsExcluded(rel))
                {
                    continue;
                }

                DateTime modified;
                try
                {
                    modified = File.GetLastWriteTimeUtc(file);
                }
                catch
                {
                    modified = DateTime.UtcNow;
                }

                entries.Add(new BackupEntry(rel, file, modified));
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(absDir);
            }
            catch
            {
                return;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                var subRel = relDir.Length == 0 ? name : relDir + "/" + name;

                // Never back up the backup directory itself (avoids backups-of-backups).
                if (EnsureTrailingSeparator(Path.GetFullPath(sub))
                    .StartsWith(backupFull, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Prune whole subtrees excluded by a "dir/**"-style pattern.
                if (matcher.IsExcluded(subRel + "/"))
                {
                    continue;
                }

                Walk(sub, subRel, backupFull, matcher, entries);
            }
        }

        private void ApplyRetention(string backupDir, RetentionSettings retention)
        {
            if (retention.MaxCount <= 0 && retention.MaxTotalSizeMB <= 0)
            {
                return;
            }

            var maxBytes = retention.MaxTotalSizeMB > 0 ? retention.MaxTotalSizeMB * 1024L * 1024L : 0L;
            var backups = ReadBackups(backupDir).ToList();
            var toDelete = RetentionPolicy.SelectForDeletion(backups, retention.MaxCount, maxBytes);
            foreach (var backup in toDelete)
            {
                TryDelete(backup.FullPath);
                m_Logger.LogInformation("Auto Save: pruned old backup '{Name}'.", backup.Name);
            }
        }

        private IEnumerable<BackupInfo> ReadBackups(string backupDir)
        {
            if (!Directory.Exists(backupDir))
            {
                yield break;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(backupDir, "autosave-*" + m_Archiver.Extension);
            }
            catch
            {
                yield break;
            }

            foreach (var file in files)
            {
                BackupInfo? info = null;
                try
                {
                    var fi = new FileInfo(file);
                    info = new BackupInfo(fi.Name, fi.FullName, fi.Length, fi.LastWriteTimeUtc);
                }
                catch
                {
                    // ignore files that vanish mid-enumeration
                }

                if (info != null)
                {
                    yield return info;
                }
            }
        }

        private void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Auto Save: could not delete '{Path}'.", path);
            }
        }

        private static string EnsureTrailingSeparator(string path)
            => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;

        public static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            var unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return size.ToString(unit == 0 ? "0" : "0.##", CultureInfo.InvariantCulture) + " " + units[unit];
        }
    }
}
