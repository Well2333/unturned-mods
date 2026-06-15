using System;
using System.Collections.Generic;
using System.Linq;
using well404.AutoSave;
using Xunit;

namespace well404.AutoSave.Tests
{
    public class RetentionPolicyTests
    {
        private static List<BackupInfo> Backups(params (int minute, long size)[] items)
        {
            var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return items
                .Select(i => new BackupInfo(
                    $"autosave-{i.minute:00}.tar.lz", $"/b/autosave-{i.minute:00}.tar.lz", i.size,
                    baseTime.AddMinutes(i.minute)))
                .ToList();
        }

        [Fact]
        public void NoLimits_DeletesNothing()
        {
            var backups = Backups((0, 100), (1, 100), (2, 100));
            var toDelete = RetentionPolicy.SelectForDeletion(backups, 0, 0);
            Assert.Empty(toDelete);
        }

        [Fact]
        public void CountLimit_DeletesOldestFirst()
        {
            var backups = Backups((0, 100), (1, 100), (2, 100), (3, 100), (4, 100));
            var toDelete = RetentionPolicy.SelectForDeletion(backups, maxCount: 3, maxTotalBytes: 0);

            Assert.Equal(2, toDelete.Count);
            Assert.Equal(new[] { "autosave-00.tar.lz", "autosave-01.tar.lz" }, toDelete.Select(b => b.Name));
        }

        [Fact]
        public void SizeLimit_DeletesUntilUnderCap()
        {
            // 5 × 100 = 500; cap at 250 → keep newest 2 (200), delete oldest 3.
            var backups = Backups((0, 100), (1, 100), (2, 100), (3, 100), (4, 100));
            var toDelete = RetentionPolicy.SelectForDeletion(backups, maxCount: 0, maxTotalBytes: 250);

            Assert.Equal(3, toDelete.Count);
            Assert.Equal(
                new[] { "autosave-00.tar.lz", "autosave-01.tar.lz", "autosave-02.tar.lz" },
                toDelete.Select(b => b.Name));
        }

        [Fact]
        public void EitherLimit_Triggers()
        {
            var backups = Backups((0, 100), (1, 100), (2, 100), (3, 100));
            // count ok (<=10) but size cap 150 → delete oldest until <=150 → keep newest 1.
            var toDelete = RetentionPolicy.SelectForDeletion(backups, maxCount: 10, maxTotalBytes: 150);
            Assert.Equal(3, toDelete.Count);
        }

        [Fact]
        public void AlwaysKeepsNewest_EvenIfItAloneExceedsSizeCap()
        {
            var backups = Backups((0, 100), (1, 100), (2, 100));
            var toDelete = RetentionPolicy.SelectForDeletion(backups, maxCount: 0, maxTotalBytes: 1);

            Assert.Equal(2, toDelete.Count); // never deletes the single newest
            Assert.DoesNotContain(toDelete, b => b.Name == "autosave-02.tar.lz");
        }

        [Fact]
        public void SingleBackup_IsNeverDeleted()
        {
            var backups = Backups((0, 10_000));
            Assert.Empty(RetentionPolicy.SelectForDeletion(backups, maxCount: 0, maxTotalBytes: 1));
        }
    }
}
