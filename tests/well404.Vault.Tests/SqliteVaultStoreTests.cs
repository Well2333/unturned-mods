using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace well404.Vault.Tests
{
    public class SqliteVaultStoreTests : IDisposable
    {
        private readonly string m_File =
            Path.Combine(Path.GetTempPath(), "vaulttest-" + Guid.NewGuid().ToString("N") + ".sqlite3");

        private SqliteVaultStore NewStore()
        {
            var store = new SqliteVaultStore(m_File);
            store.Initialize();
            return store;
        }

        [Fact]
        public void AddAndGet_RoundTripsRawStateAndCapacity()
        {
            var store = NewStore();
            var state = new byte[] { 1, 2, 3, 255 };
            store.AddItems("7656119", new[]
            {
                new StoredItem
                {
                    ItemId = 363,
                    Amount = 7,
                    Quality = 83,
                    State = Convert.ToBase64String(state),
                    SlotCost = 4,
                    MaxAmount = 8
                }
            });

            var item = Assert.Single(store.Get("7656119"));
            Assert.True(item.RecordId > 0);
            Assert.Equal((ushort)363, item.ItemId);
            Assert.Equal((byte)7, item.Amount);
            Assert.Equal((byte)83, item.Quality);
            Assert.Equal(state, Convert.FromBase64String(item.State));
            Assert.Equal(4, store.UsedSlots("7656119"));
        }

        [Fact]
        public void TakeFirst_AtomicallyDeletesOnlyMatchingRow()
        {
            var store = NewStore();
            store.AddItems("p", new[]
            {
                new StoredItem { ItemId = 1, State = string.Empty },
                new StoredItem { ItemId = 2, State = string.Empty }
            });

            var taken = store.TakeFirst("p", item => item.ItemId == 2);

            Assert.NotNull(taken);
            Assert.Equal((ushort)2, taken!.ItemId);
            var remaining = Assert.Single(new SqliteVaultStore(m_File).Get("p"));
            Assert.Equal((ushort)1, remaining.ItemId);
        }

        [Fact]
        public void AddItems_InvalidState_RollsBackWholeBatch()
        {
            var store = NewStore();

            Assert.Throws<FormatException>(() => store.AddItems("p", new[]
            {
                new StoredItem { ItemId = 1, State = string.Empty },
                new StoredItem { ItemId = 2, State = "not-base64" }
            }));

            Assert.Empty(store.Get("p"));
        }

        [Fact]
        public void Overrides_AreUpsertedPersistedAndDeleted()
        {
            var store = NewStore();
            store.SetOverride("p", 400);
            store.SetOverride("p", 600);

            var reopened = new SqliteVaultStore(m_File);
            Assert.Equal(600, reopened.GetOverride("p"));
            Assert.Equal(600, reopened.GetOverrides()["p"]);
            Assert.True(reopened.ClearOverride("p"));
            Assert.Null(reopened.GetOverride("p"));
            Assert.False(reopened.ClearOverride("p"));
        }

        [Fact]
        public void Database_UsesWalJournalMode()
        {
            NewStore();
            using var connection = new SqliteConnection("Data Source=" + m_File);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("wal", (string)command.ExecuteScalar()!);
        }

        public void Dispose()
        {
            DeleteIfExists(m_File);
            DeleteIfExists(m_File + "-wal");
            DeleteIfExists(m_File + "-shm");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
