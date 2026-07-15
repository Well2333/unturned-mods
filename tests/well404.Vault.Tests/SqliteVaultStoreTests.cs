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
        public void UpdateItem_IsPlayerScopedAndPreservesOpaqueState()
        {
            var store = NewStore();
            var state = Convert.ToBase64String(new byte[] { 4, 8, 15, 16, 23, 42 });
            store.AddItems("owner", new[]
            {
                new StoredItem { ItemId = 10, Amount = 1, Quality = 90, State = state, SlotCost = 2, MaxAmount = 0 }
            });
            var row = Assert.Single(store.Get("owner"));

            Assert.False(store.UpdateItem("other", row.RecordId, 11, 2, 70, 4, 8));
            Assert.True(store.UpdateItem("owner", row.RecordId, 11, 2, 70, 4, 8));

            var updated = Assert.Single(store.Get("owner"));
            Assert.Equal((ushort)11, updated.ItemId);
            Assert.Equal((byte)2, updated.Amount);
            Assert.Equal((byte)70, updated.Quality);
            Assert.Equal(4, updated.SlotCost);
            Assert.Equal((byte)8, updated.MaxAmount);
            Assert.Equal(state, updated.State);
        }

        [Fact]
        public void DeleteItem_IsPlayerScoped()
        {
            var store = NewStore();
            store.AddItems("owner", new[] { new StoredItem { ItemId = 10, State = string.Empty } });
            var row = Assert.Single(store.Get("owner"));

            Assert.False(store.DeleteItem("other", row.RecordId));
            Assert.Single(store.Get("owner"));
            Assert.True(store.DeleteItem("owner", row.RecordId));
            Assert.Empty(store.Get("owner"));
        }

        [Fact]
        public void DeleteItems_DeletesOnlyMatchingItemForOnePlayer()
        {
            var store = NewStore();
            store.AddItems("owner", new[]
            {
                new StoredItem { ItemId = 60332, State = string.Empty },
                new StoredItem { ItemId = 60332, State = string.Empty },
                new StoredItem { ItemId = 15, State = string.Empty }
            });
            store.AddItems("other", new[] { new StoredItem { ItemId = 60332, State = string.Empty } });

            Assert.Equal(2, store.DeleteItems("owner", 60332));
            Assert.Single(store.Get("owner"));
            Assert.Equal((ushort)15, store.Get("owner")[0].ItemId);
            Assert.Single(store.Get("other"));
            Assert.Equal(0, store.DeleteItems("owner", 60332));
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
