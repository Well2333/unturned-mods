using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SDG.Unturned;

namespace well404.AutoSave
{
    /// <summary>
    /// Coordinates game saves and compressed backups. Scheduled saves always run on the configured
    /// cron; <see cref="BackupCadenceController"/> only decides when an archive is created.
    /// </summary>
    public sealed class SaveService
    {
        private readonly BackupService m_Backup;
        private readonly SaveStateStore m_State;
        private readonly AutoSaveConfigStore m_Config;
        private readonly BackupCadenceController m_Cadence;
        private readonly ILogger m_Logger;

        private bool m_LoggedPathsOnce;

        public SaveService(
            BackupService backup,
            SaveStateStore state,
            AutoSaveConfigStore config,
            ILogger logger)
            : this(backup, state, config, new BackupCadenceController(initialOnlinePlayers: 1), logger)
        {
        }

        public SaveService(
            BackupService backup,
            SaveStateStore state,
            AutoSaveConfigStore config,
            BackupCadenceController cadence,
            ILogger logger)
        {
            m_Backup = backup;
            m_State = state;
            m_Config = config;
            m_Cadence = cadence;
            m_Logger = logger;
        }

        /// <summary>True once the level is loaded and the server id is known (safe to save/back up).</summary>
        public static bool IsServerReady() => Level.isLoaded && !string.IsNullOrEmpty(Provider.serverID);

        /// <summary>Saves the game (main thread) and returns whether it ran plus the new save count.</summary>
        public async UniTask<(bool saved, long count)> SaveGameAsync()
        {
            await UniTask.SwitchToMainThread();
            if (!IsServerReady())
            {
                m_Logger.LogDebug("Auto Save: server not fully loaded yet; skipping this save.");
                return (false, m_State.Read());
            }

            SaveManager.save();
            var count = m_State.Read() + 1;
            m_State.Write(count);
            return (true, count);
        }

        /// <summary>Scheduled tick: always save, then create an archive only when its cadence is due.</summary>
        public async Task TickAsync()
        {
            var (saved, count) = await SaveGameAsync();
            if (!saved)
            {
                return;
            }

            // Still on the main thread here (UniTask kept us there); reading game statics is safe.
            var paths = SavePaths.Capture();
            LogPathsOnce(paths);
            m_Logger.LogInformation("Auto Save: game saved (#{Count}).", count);

            var settings = m_Config.Current;
            var reason = m_Cadence.GetScheduledBackupReason(
                settings.Backup,
                settings.IdleBackup,
                count,
                DateTime.UtcNow);
            if (reason == ScheduledBackupReason.None)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                var result = m_Backup.Run(paths, settings.Backup, settings.Retention);
                if (result.Status == BackupStatus.Success)
                {
                    m_Cadence.MarkBackupSucceeded(reason, settings.IdleBackup, DateTime.UtcNow);
                    if (reason == ScheduledBackupReason.FirstAfterEmpty || reason == ScheduledBackupReason.IdleInterval)
                    {
                        m_Logger.LogInformation(
                            "Auto Save: empty-server backup completed; the next idle backup is due in {Hours} hours.",
                            settings.IdleBackup.IntervalHours);
                    }
                }
            });
        }

        /// <summary>
        /// Manual save + immediate backup; awaits the backup so the caller gets the result. The game
        /// is always saved first; the backup runs even if scheduled backups are disabled.
        /// </summary>
        public async Task<BackupResult> RunManualAsync()
        {
            var (saved, _) = await SaveGameAsync();
            if (!saved)
            {
                return BackupResult.Failed("server-not-ready");
            }

            var paths = SavePaths.Capture();
            LogPathsOnce(paths);
            var settings = m_Config.Current;
            return await Task.Run(() => m_Backup.Run(paths, settings.Backup, settings.Retention));
        }

        private void LogPathsOnce(SavePaths paths)
        {
            if (m_LoggedPathsOnce)
            {
                return;
            }

            m_LoggedPathsOnce = true;
            m_Logger.LogInformation(
                "Auto Save: savedata root '{Root}', backups go to '{Backup}'.",
                paths.SavedataRoot, paths.ResolveBackupDirectory(m_Config.Current.Backup.Directory));
        }
    }
}
