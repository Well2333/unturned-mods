using System;
using System.Collections.Generic;
using System.Linq;

namespace well404.AutoSave
{
    /// <summary>
    /// Decides which backups to delete to satisfy the count/size caps. Pure (no IO), so it is unit
    /// tested directly. Deletes the oldest first while EITHER cap is exceeded, but always keeps at
    /// least the single newest backup (so a just-made backup is never immediately removed, even if it
    /// alone exceeds the size cap).
    /// </summary>
    public static class RetentionPolicy
    {
        /// <param name="backups">All existing backups (any order).</param>
        /// <param name="maxCount">Maximum number to keep; 0 = unlimited.</param>
        /// <param name="maxTotalBytes">Maximum total size to keep; 0 = unlimited.</param>
        /// <returns>The backups to delete, oldest first.</returns>
        public static IReadOnlyList<BackupInfo> SelectForDeletion(
            IReadOnlyList<BackupInfo> backups, int maxCount, long maxTotalBytes)
        {
            if (backups == null)
            {
                throw new ArgumentNullException(nameof(backups));
            }

            if (backups.Count <= 1 || (maxCount <= 0 && maxTotalBytes <= 0))
            {
                return Array.Empty<BackupInfo>();
            }

            // Oldest → newest; we delete from the front and never touch the last (newest) element.
            var ordered = backups.OrderBy(b => b.TimestampUtc).ThenBy(b => b.Name, StringComparer.Ordinal).ToList();
            var remainingCount = ordered.Count;
            var remainingBytes = ordered.Sum(b => b.SizeBytes);

            var toDelete = new List<BackupInfo>();
            var i = 0;
            while (i < ordered.Count - 1
                && ((maxCount > 0 && remainingCount > maxCount)
                    || (maxTotalBytes > 0 && remainingBytes > maxTotalBytes)))
            {
                toDelete.Add(ordered[i]);
                remainingCount--;
                remainingBytes -= ordered[i].SizeBytes;
                i++;
            }

            return toDelete;
        }
    }
}
