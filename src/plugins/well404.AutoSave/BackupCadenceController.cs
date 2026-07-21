using System;

namespace well404.AutoSave
{
    public enum ScheduledBackupReason
    {
        None,
        Regular,
        FirstAfterEmpty,
        IdleInterval
    }

    /// <summary>
    /// Keeps backup throttling independent from the save schedule. Scheduled saves always continue;
    /// only archive creation changes cadence while no players are online.
    /// </summary>
    public sealed class BackupCadenceController
    {
        private enum Mode
        {
            Active,
            WaitingForFirstEmptyBackup,
            IdleLongCycle
        }

        private readonly object m_Lock = new object();
        private int m_OnlinePlayers;
        private Mode m_Mode;
        private DateTime? m_NextIdleBackupUtc;

        public BackupCadenceController(int initialOnlinePlayers)
        {
            m_OnlinePlayers = Math.Max(0, initialOnlinePlayers);
            m_Mode = m_OnlinePlayers > 0 ? Mode.Active : Mode.WaitingForFirstEmptyBackup;
        }

        /// <summary>Returns true only for the 0 -&gt; 1 transition.</summary>
        public bool PlayerConnected()
        {
            lock (m_Lock)
            {
                var wasEmpty = m_OnlinePlayers == 0;
                m_OnlinePlayers++;
                if (wasEmpty)
                {
                    m_Mode = Mode.Active;
                    m_NextIdleBackupUtc = null;
                }

                return wasEmpty;
            }
        }

        /// <summary>Returns true only when this disconnect changes the count from 1 to 0.</summary>
        public bool PlayerDisconnected()
        {
            lock (m_Lock)
            {
                if (m_OnlinePlayers == 0)
                {
                    return false;
                }

                m_OnlinePlayers--;
                if (m_OnlinePlayers != 0)
                {
                    return false;
                }

                m_Mode = Mode.WaitingForFirstEmptyBackup;
                m_NextIdleBackupUtc = null;
                return true;
            }
        }

        public ScheduledBackupReason GetScheduledBackupReason(
            BackupSettings backup,
            IdleBackupSettings idle,
            long saveCount,
            DateTime utcNow)
        {
            if (!backup.Enabled || backup.EveryNSaves <= 0)
            {
                return ScheduledBackupReason.None;
            }

            lock (m_Lock)
            {
                var regularDue = saveCount % backup.EveryNSaves == 0;
                if (!idle.Enabled || m_Mode == Mode.Active)
                {
                    return regularDue ? ScheduledBackupReason.Regular : ScheduledBackupReason.None;
                }

                if (m_Mode == Mode.WaitingForFirstEmptyBackup)
                {
                    return regularDue ? ScheduledBackupReason.FirstAfterEmpty : ScheduledBackupReason.None;
                }

                return m_NextIdleBackupUtc == null || utcNow >= m_NextIdleBackupUtc.Value
                    ? ScheduledBackupReason.IdleInterval
                    : ScheduledBackupReason.None;
            }
        }

        public void MarkBackupSucceeded(ScheduledBackupReason reason, IdleBackupSettings idle, DateTime utcNow)
        {
            if (reason != ScheduledBackupReason.FirstAfterEmpty && reason != ScheduledBackupReason.IdleInterval)
            {
                return;
            }

            lock (m_Lock)
            {
                if (!idle.Enabled || m_OnlinePlayers > 0)
                {
                    return;
                }

                m_Mode = Mode.IdleLongCycle;
                m_NextIdleBackupUtc = utcNow.AddHours(Math.Min(8760, Math.Max(1, idle.IntervalHours)));
            }
        }
    }
}
