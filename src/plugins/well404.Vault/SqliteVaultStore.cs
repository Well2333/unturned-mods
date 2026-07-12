using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace well404.Vault
{
    /// <summary>
    /// Transactional SQLite persistence for vault items and per-player capacity overrides.
    /// Every operation opens a short-lived connection so OpenMod reload never leaves the file locked.
    /// </summary>
    public sealed class SqliteVaultStore
    {
        private readonly object m_Lock = new object();
        private bool m_Initialized;

        public string FilePath { get; }

        public SqliteVaultStore(string filePath)
        {
            SqliteRuntime.Initialize();
            FilePath = filePath;
        }

        public void Initialize()
        {
            lock (m_Lock)
            {
                EnsureInitialized();
            }
        }

        public IReadOnlyList<StoredItem> Get(string steamId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE steam_id = $steamId
ORDER BY id;";
                command.Parameters.AddWithValue("$steamId", steamId);
                using var reader = command.ExecuteReader();
                var result = new List<StoredItem>();
                while (reader.Read())
                {
                    result.Add(ReadItem(reader));
                }

                return (IReadOnlyList<StoredItem>)result;
            });

        public int UsedSlots(string steamId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(SUM(slot_cost), 0) FROM vault_items WHERE steam_id = $steamId;";
                command.Parameters.AddWithValue("$steamId", steamId);
                return Convert.ToInt32(command.ExecuteScalar());
            });

        public int? GetOverride(string steamId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT capacity FROM capacity_overrides WHERE steam_id = $steamId;";
                command.Parameters.AddWithValue("$steamId", steamId);
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
            });

        public IReadOnlyDictionary<string, int> GetOverrides()
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT steam_id, capacity FROM capacity_overrides ORDER BY steam_id;";
                using var reader = command.ExecuteReader();
                var result = new Dictionary<string, int>(StringComparer.Ordinal);
                while (reader.Read())
                {
                    result[reader.GetString(0)] = reader.GetInt32(1);
                }

                return (IReadOnlyDictionary<string, int>)result;
            });

        public void SetOverride(string steamId, int capacity)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO capacity_overrides(steam_id, capacity) VALUES($steamId, $capacity)
ON CONFLICT(steam_id) DO UPDATE SET capacity = excluded.capacity;";
                command.Parameters.AddWithValue("$steamId", steamId);
                command.Parameters.AddWithValue("$capacity", capacity);
                command.ExecuteNonQuery();
                return 0;
            });

        public bool ClearOverride(string steamId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM capacity_overrides WHERE steam_id = $steamId;";
                command.Parameters.AddWithValue("$steamId", steamId);
                return command.ExecuteNonQuery() > 0;
            });

        public void AddItems(string steamId, IReadOnlyList<StoredItem> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                foreach (var item in items)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO vault_items(steam_id, item_id, amount, quality, state, slot_cost, max_amount)
VALUES($steamId, $itemId, $amount, $quality, $state, $slotCost, $maxAmount);";
                    command.Parameters.AddWithValue("$steamId", steamId);
                    command.Parameters.AddWithValue("$itemId", item.ItemId);
                    command.Parameters.AddWithValue("$amount", item.Amount);
                    command.Parameters.AddWithValue("$quality", item.Quality);
                    command.Parameters.AddWithValue("$state", DecodeState(item.State));
                    command.Parameters.AddWithValue("$slotCost", item.SlotCost);
                    command.Parameters.AddWithValue("$maxAmount", item.MaxAmount);
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                return 0;
            });
        }

        /// <summary>
        /// Atomically selects and deletes the first matching item. The returned item is no longer
        /// present in SQLite when this method returns.
        /// </summary>
        public StoredItem? TakeFirst(string steamId, Func<StoredItem, bool> matches)
            => WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                StoredItem? selected = null;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
SELECT id, item_id, amount, quality, state, slot_cost, max_amount
FROM vault_items
WHERE steam_id = $steamId
ORDER BY id;";
                    command.Parameters.AddWithValue("$steamId", steamId);
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var candidate = ReadItem(reader);
                        if (matches(candidate))
                        {
                            selected = candidate;
                            break;
                        }
                    }
                }

                if (selected == null)
                {
                    transaction.Commit();
                    return null;
                }

                using (var delete = connection.CreateCommand())
                {
                    delete.Transaction = transaction;
                    delete.CommandText = "DELETE FROM vault_items WHERE id = $id;";
                    delete.Parameters.AddWithValue("$id", selected.RecordId);
                    if (delete.ExecuteNonQuery() != 1)
                    {
                        transaction.Rollback();
                        return null;
                    }
                }

                transaction.Commit();
                return selected;
            });

        private T WithDatabase<T>(Func<SqliteConnection, T> action)
        {
            lock (m_Lock)
            {
                EnsureInitialized();
                using var connection = OpenConnection();
                return action(connection);
            }
        }

        private void EnsureInitialized()
        {
            if (m_Initialized)
            {
                return;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode = WAL;
PRAGMA synchronous = FULL;
CREATE TABLE IF NOT EXISTS vault_items (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    steam_id TEXT NOT NULL,
    item_id INTEGER NOT NULL,
    amount INTEGER NOT NULL,
    quality INTEGER NOT NULL,
    state BLOB NOT NULL,
    slot_cost INTEGER NOT NULL,
    max_amount INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_vault_items_player
    ON vault_items(steam_id, id);
CREATE INDEX IF NOT EXISTS ix_vault_items_player_item
    ON vault_items(steam_id, item_id);
CREATE TABLE IF NOT EXISTS capacity_overrides (
    steam_id TEXT NOT NULL PRIMARY KEY,
    capacity INTEGER NOT NULL CHECK(capacity > 0)
);";
            command.ExecuteNonQuery();
            m_Initialized = true;
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
            command.ExecuteNonQuery();
            return connection;
        }

        private static StoredItem ReadItem(SqliteDataReader reader)
        {
            var state = (byte[])reader.GetValue(4);
            return new StoredItem
            {
                RecordId = reader.GetInt64(0),
                ItemId = checked((ushort)reader.GetInt64(1)),
                Amount = checked((byte)reader.GetInt64(2)),
                Quality = checked((byte)reader.GetInt64(3)),
                State = state.Length == 0 ? string.Empty : Convert.ToBase64String(state),
                SlotCost = reader.GetInt32(5),
                MaxAmount = checked((byte)reader.GetInt64(6))
            };
        }

        private static byte[] DecodeState(string state)
            => string.IsNullOrEmpty(state) ? Array.Empty<byte>() : Convert.FromBase64String(state);
    }
}
