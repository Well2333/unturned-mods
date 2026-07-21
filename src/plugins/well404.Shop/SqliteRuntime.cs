using SQLitePCL;

namespace well404.Shop
{
    internal static class SqliteRuntime
    {
        private static readonly object s_Lock = new object();

        public static void Initialize()
        {
            lock (s_Lock)
            {
                try
                {
                    raw.SetProvider(new SQLite3Provider_sqlite3());
                    raw.FreezeProvider();
                }
                catch (System.InvalidOperationException)
                {
                    // The provider is process-wide and may already be frozen by Economy or Vault.
                }
            }
        }
    }
}
