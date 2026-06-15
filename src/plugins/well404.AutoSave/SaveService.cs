using System;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SDG.Unturned;

namespace well404.AutoSave
{
    /// <summary>
    /// Coordinates a save with its (optional) backup. <see cref="TickAsync"/> is what the scheduler
    /// calls: it saves the game on the main thread, advances the persisted save counter, and — when a
    /// backup is due — kicks one off on a background thread. <see cref="RunManualAsync"/> does a save
    /// plus an immediate backup and waits for it (for the web panel's "Back up now" button).
    /// <para>
    /// Paths are resolved lazily (per run, on the main thread) rather than captured at load: at load
    /// time the level is not up and <c>Provider.serverID</c> is still empty, so an early capture would
    /// point at the wrong directory.
    /// </para>
    /// </summary>
    public sealed class SaveService
    {
        private readonly BackupService m_Backup;
        private readonly SaveStateStore m_State;
        private readonly AutoSaveConfigStore m_Config;
        private readonly ILogger m_Logger;

        private bool m_LoggedPathsOnce;

        public SaveService(BackupService backup, SaveStateStore state, AutoSaveConfigStore config, ILogger logger)
        {
            m_Backup = backup;
            m_State = state;
            m_Config = config;
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

        /// <summary>Scheduler tick: save, then back up if this save is an Nth one.</summary>
        public async Task TickAsync()
        {
            var (saved, count) = await SaveGameAsync();
            if (!saved)
            {
                return;
            }

            // Still on the main thread here (UniTask kept us there); reading the game statics is safe.
            var paths = SavePaths.Capture();
            LogPathsOnce(paths);
            m_Logger.LogInformation("Auto Save: game saved (#{Count}).", count);

            var settings = m_Config.Current;
            if (IsBackupDue(settings.Backup, count))
            {
                // Off the main thread: the save already flushed every file synchronously.
                _ = Task.Run(() => m_Backup.Run(paths, settings.Backup, settings.Retention));
            }
        }

        /// <summary>
        /// Manual save + immediate backup; awaits the backup so the caller gets the result. The game
        /// is always saved first; the backup runs even if scheduled backups are disabled (that is the
        /// intent of the "Back up now" button).
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

        private static bool IsBackupDue(BackupSettings backup, long count)
            => backup.Enabled && backup.EveryNSaves > 0 && count % backup.EveryNSaves == 0;
    }
}
