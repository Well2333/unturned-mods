using SQLitePCL;

namespace well404.Economy.Currency
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
                    // Another plugin already initialized and froze the process-wide provider.
                }
            }
        }
    }

}
