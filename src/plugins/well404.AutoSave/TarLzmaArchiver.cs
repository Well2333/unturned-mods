using System.Collections.Generic;
using System.IO;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Writers.Tar;

namespace well404.AutoSave
{
    /// <summary>
    /// Produces a solid <c>.tar.lz</c> backup: the files are written into one uncompressed tar
    /// stream which is wrapped in an <see cref="LZipStream"/> (LZMA). Solid compression across all
    /// files (rather than per-entry) gives a much smaller archive for the many small savedata files,
    /// and <c>.lz</c> is restorable with 7-Zip, lzip or SharpCompress.
    /// </summary>
    public sealed class TarLzmaArchiver : IBackupArchiver
    {
        public string Extension => ".tar.lz";

        public void Create(string outputFilePath, IEnumerable<BackupEntry> entries)
        {
            using var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var lzip = new LZipStream(fileStream, CompressionMode.Compress);
            using var tar = new TarWriter(lzip, new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true));
            foreach (var entry in entries)
            {
                using var source = new FileStream(
                    entry.SourceFullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                tar.Write(entry.RelativePath, source, entry.LastModifiedUtc);
            }
        }
    }
}
