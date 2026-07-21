using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace well404.Shop
{
    internal sealed class ShopTradeMutex
    {
        private readonly string m_Path;

        public ShopTradeMutex(string databasePath)
        {
            m_Path = databasePath + ".trade.lock";
        }

        public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            var directory = Path.GetDirectoryName(m_Path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(m_Path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                        FileShare.None, 1, FileOptions.None);
                }
                catch (IOException)
                {
                    await Task.Delay(25, cancellationToken);
                }
            }
        }
    }
}
