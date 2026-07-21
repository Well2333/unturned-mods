using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using well404.AutoSave;
using Xunit;

namespace well404.AutoSave.Tests
{
    public class TarLzmaArchiverTests
    {
        [Fact]
        public void Create_RoundTripsFilesAndRelativePaths()
        {
            var work = Path.Combine(Path.GetTempPath(), "autosave-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(work, "sub"));
            try
            {
                var aPath = Path.Combine(work, "a.txt");
                var bPath = Path.Combine(work, "sub", "b.txt");
                File.WriteAllText(aPath, "hello world");
                // Compressible content so the archive is meaningfully smaller than the input.
                File.WriteAllText(bPath, new string('x', 50_000));

                var entries = new List<BackupEntry>
                {
                    new BackupEntry("a.txt", aPath, DateTime.UtcNow),
                    new BackupEntry("sub/b.txt", bPath, DateTime.UtcNow)
                };

                var archive = Path.Combine(work, "out.tar.lz");
                new TarLzmaArchiver().Create(archive, entries);

                Assert.True(File.Exists(archive));
                Assert.True(new FileInfo(archive).Length > 0);
                // LZMA should compress the 50k of repeated bytes far below the raw size.
                Assert.True(new FileInfo(archive).Length < 50_000);

                var extracted = ReadArchive(archive);
                Assert.Equal(2, extracted.Count);
                Assert.Equal("hello world", extracted["a.txt"]);
                Assert.Equal(new string('x', 50_000), extracted["sub/b.txt"]);
            }
            finally
            {
                Directory.Delete(work, recursive: true);
            }
        }

        private static Dictionary<string, string> ReadArchive(string path)
        {
            var result = new Dictionary<string, string>();
            using var fs = File.OpenRead(path);
            using var lz = LZipStream.Create(fs, CompressionMode.Decompress);
            using var reader = new System.Formats.Tar.TarReader(lz, leaveOpen: false);
            System.Formats.Tar.TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory)
                {
                    continue;
                }

                var data = entry.DataStream
                    ?? throw new InvalidDataException("Tar entry has no data stream.");
                using var ms = new MemoryStream();
                data.CopyTo(ms);
                var key = entry.Name.Replace('\\', '/');
                if (key.StartsWith("./", StringComparison.Ordinal))
                {
                    key = key.Substring(2);
                }

                result[key] = Encoding.UTF8.GetString(ms.ToArray());
            }

            return result;
        }
    }
}
