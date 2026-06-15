using System;

namespace well404.AutoSave
{
    /// <summary>One backup archive on disk: its file name, full path, size and timestamp.</summary>
    public sealed class BackupInfo
    {
        public BackupInfo(string name, string fullPath, long sizeBytes, DateTime timestampUtc)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            FullPath = fullPath ?? throw new ArgumentNullException(nameof(fullPath));
            SizeBytes = sizeBytes;
            TimestampUtc = timestampUtc;
        }

        /// <summary>The archive file name (e.g. <c>autosave-20260615-090000.tar.lz</c>).</summary>
        public string Name { get; }

        public string FullPath { get; }

        public long SizeBytes { get; }

        /// <summary>When the backup was taken (used to order oldest → newest for retention).</summary>
        public DateTime TimestampUtc { get; }
    }
}
