using System;
using System.Collections.Generic;

namespace well404.AutoSave
{
    /// <summary>One file to place into a backup archive.</summary>
    public sealed class BackupEntry
    {
        public BackupEntry(string relativePath, string sourceFullPath, DateTime lastModifiedUtc)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            SourceFullPath = sourceFullPath ?? throw new ArgumentNullException(nameof(sourceFullPath));
            LastModifiedUtc = lastModifiedUtc;
        }

        /// <summary>Path stored inside the archive (savedata-relative, <c>/</c>-separated).</summary>
        public string RelativePath { get; }

        /// <summary>Absolute path of the file to read.</summary>
        public string SourceFullPath { get; }

        public DateTime LastModifiedUtc { get; }
    }

    /// <summary>Writes a set of files into a single compressed archive.</summary>
    public interface IBackupArchiver
    {
        /// <summary>The archive file extension this archiver produces (e.g. <c>.tar.lz</c>).</summary>
        string Extension { get; }

        /// <summary>Streams every entry into a new archive at <paramref name="outputFilePath"/>.</summary>
        void Create(string outputFilePath, IEnumerable<BackupEntry> entries);
    }
}
