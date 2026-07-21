using System;
using well404.AutoSave;
using Xunit;

namespace well404.AutoSave.Tests
{
    public sealed class BackupCadenceControllerTests
    {
        private static readonly BackupSettings Backup = new BackupSettings
        {
            Enabled = true,
            EveryNSaves = 6
        };

        private static readonly IdleBackupSettings Idle = new IdleBackupSettings
        {
            Enabled = true,
            IntervalHours = 24
        };

        [Fact]
        public void ActiveServerKeepsRegularCadence()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 1);
            var now = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

            Assert.Equal(ScheduledBackupReason.None, cadence.GetScheduledBackupReason(Backup, Idle, 5, now));
            Assert.Equal(ScheduledBackupReason.Regular, cadence.GetScheduledBackupReason(Backup, Idle, 6, now));
        }

        [Fact]
        public void EmptyServerRunsOneRegularBackupThenUsesLongInterval()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 1);
            var now = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

            Assert.True(cadence.PlayerDisconnected());
            Assert.Equal(ScheduledBackupReason.None, cadence.GetScheduledBackupReason(Backup, Idle, 5, now));
            Assert.Equal(ScheduledBackupReason.FirstAfterEmpty, cadence.GetScheduledBackupReason(Backup, Idle, 6, now));

            cadence.MarkBackupSucceeded(ScheduledBackupReason.FirstAfterEmpty, Idle, now);

            Assert.Equal(ScheduledBackupReason.None,
                cadence.GetScheduledBackupReason(Backup, Idle, 12, now.AddHours(23)));
            Assert.Equal(ScheduledBackupReason.IdleInterval,
                cadence.GetScheduledBackupReason(Backup, Idle, 13, now.AddHours(24)));
        }

        [Fact]
        public void FirstPlayerReturnLeavesIdleModeExactlyOnce()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 0);
            var now = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

            Assert.True(cadence.PlayerConnected());
            Assert.False(cadence.PlayerConnected());
            Assert.Equal(ScheduledBackupReason.Regular, cadence.GetScheduledBackupReason(Backup, Idle, 6, now));
            Assert.False(cadence.PlayerDisconnected());
            Assert.True(cadence.PlayerDisconnected());
        }

        [Fact]
        public void FailedFirstEmptyBackupDoesNotStartLongInterval()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 0);
            var now = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

            Assert.Equal(ScheduledBackupReason.FirstAfterEmpty,
                cadence.GetScheduledBackupReason(Backup, Idle, 6, now));
            Assert.Equal(ScheduledBackupReason.FirstAfterEmpty,
                cadence.GetScheduledBackupReason(Backup, Idle, 12, now.AddHours(1)));
        }

        [Fact]
        public void DisablingIdleModePreservesLegacyRegularCadence()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 0);
            var idleDisabled = new IdleBackupSettings { Enabled = false, IntervalHours = 24 };
            var now = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

            Assert.Equal(ScheduledBackupReason.None,
                cadence.GetScheduledBackupReason(Backup, idleDisabled, 5, now));
            Assert.Equal(ScheduledBackupReason.Regular,
                cadence.GetScheduledBackupReason(Backup, idleDisabled, 6, now));
        }

        [Fact]
        public void BackupMasterSwitchStillWins()
        {
            var cadence = new BackupCadenceController(initialOnlinePlayers: 0);
            var disabled = new BackupSettings { Enabled = false, EveryNSaves = 6 };

            Assert.Equal(ScheduledBackupReason.None,
                cadence.GetScheduledBackupReason(disabled, Idle, 6, DateTime.UtcNow));
        }
    }
}
