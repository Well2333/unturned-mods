using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace well404.Shop
{
    /// <summary>Durable audit log and quarantine state for cross-system shop operations.</summary>
    public sealed class ShopOperationStore
    {
        private readonly object m_Lock = new object();
        private bool m_Initialized;

        public ShopOperationStore(string filePath)
        {
            SqliteRuntime.Initialize();
            FilePath = filePath;
        }

        public string FilePath { get; }

        public void Initialize()
        {
            lock (m_Lock) EnsureInitialized();
        }

        public bool TryCreate(ShopOperationDraft draft)
        {
            return WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var now = UtcNow();
                try
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
INSERT INTO shop_operations(
    operation_id, kind, player_id, player_type, player_name, catalog_key,
    item_plan, total, state, detail, created_utc, updated_utc,
    resolution, resolved_by, resolution_note)
VALUES($id, $kind, $player, $type, $name, $catalog, $plan, $total,
       'prepared', '', $now, $now, '', '', '');";
                    command.Parameters.AddWithValue("$id", draft.OperationId);
                    command.Parameters.AddWithValue("$kind", draft.Kind);
                    command.Parameters.AddWithValue("$player", draft.PlayerId);
                    command.Parameters.AddWithValue("$type", draft.PlayerType);
                    command.Parameters.AddWithValue("$name", draft.PlayerName ?? string.Empty);
                    command.Parameters.AddWithValue("$catalog", draft.CatalogKey ?? string.Empty);
                    command.Parameters.AddWithValue("$plan", SerializeLines(draft.Lines));
                    command.Parameters.AddWithValue("$total", Money(draft.Total));
                    command.Parameters.AddWithValue("$now", now);
                    command.ExecuteNonQuery();
                    InsertEvent(connection, transaction, draft.OperationId, string.Empty,
                        ShopOperationStates.Prepared, "system", "Operation prepared.", now);
                    transaction.Commit();
                    return true;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    transaction.Rollback();
                    return false;
                }
            });
        }

        public bool TryTransition(string operationId, IReadOnlyList<string> expectedStates,
            string nextState, string actor, string note)
            => TryTransitionCore(operationId, expectedStates, nextState, actor, note, string.Empty);

        public bool TryResolve(string operationId, IReadOnlyList<string> expectedStates,
            string resolution, string actor, string note)
            => TryTransitionCore(operationId, expectedStates, ShopOperationStates.Resolved,
                actor, note, resolution);

        private bool TryTransitionCore(string operationId, IReadOnlyList<string> expectedStates,
            string nextState, string actor, string note, string resolution)
        {
            if (expectedStates.Count == 0) return false;
            return WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var current = ReadState(connection, transaction, operationId);
                if (current == null || !expectedStates.Contains(current, StringComparer.Ordinal))
                {
                    transaction.Rollback();
                    return false;
                }

                var now = UtcNow();
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE shop_operations
SET state = $next, detail = $detail, updated_utc = $now,
    resolution = CASE WHEN $resolution = '' THEN resolution ELSE $resolution END,
    resolved_by = CASE WHEN $resolution = '' THEN resolved_by ELSE $actor END,
    resolution_note = CASE WHEN $resolution = '' THEN resolution_note ELSE $detail END
WHERE operation_id = $id AND state = $current;";
                update.Parameters.AddWithValue("$next", nextState);
                update.Parameters.AddWithValue("$detail", note ?? string.Empty);
                update.Parameters.AddWithValue("$now", now);
                update.Parameters.AddWithValue("$resolution", resolution ?? string.Empty);
                update.Parameters.AddWithValue("$actor", actor ?? string.Empty);
                update.Parameters.AddWithValue("$id", operationId);
                update.Parameters.AddWithValue("$current", current);
                if (update.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    return false;
                }
                InsertEvent(connection, transaction, operationId, current, nextState,
                    actor ?? string.Empty, note ?? string.Empty, now);
                transaction.Commit();
                return true;
            });
        }

        public ShopOperationRecord? Get(string operationId)
            => WithDatabase(connection => ReadOperation(connection, @"
SELECT operation_id, kind, player_id, player_type, player_name, catalog_key,
       item_plan, total, state, detail, created_utc, updated_utc,
       resolution, resolved_by, resolution_note
FROM shop_operations WHERE operation_id = $id;", operationId));

        public ShopOperationRecord? GetActiveForPlayer(string playerId)
            => WithDatabase(connection => ReadOperation(connection, @"
SELECT operation_id, kind, player_id, player_type, player_name, catalog_key,
       item_plan, total, state, detail, created_utc, updated_utc,
       resolution, resolved_by, resolution_note
FROM shop_operations
WHERE player_id = $id AND state NOT IN ('completed', 'resolved')
ORDER BY created_utc LIMIT 1;", playerId));

        public IReadOnlyList<ShopOperationRecord> GetPending(int maximum = 200)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT operation_id, kind, player_id, player_type, player_name, catalog_key,
       item_plan, total, state, detail, created_utc, updated_utc,
       resolution, resolved_by, resolution_note
FROM shop_operations
WHERE state NOT IN ('completed', 'resolved')
ORDER BY created_utc
LIMIT $maximum;";
                command.Parameters.AddWithValue("$maximum", Math.Max(1, Math.Min(1000, maximum)));
                using var reader = command.ExecuteReader();
                var result = new List<ShopOperationRecord>();
                while (reader.Read()) result.Add(ReadOperation(reader));
                return (IReadOnlyList<ShopOperationRecord>)result;
            });

        public IReadOnlyList<ShopOperationEvent> GetEvents(string operationId)
            => WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT id, operation_id, from_state, to_state, actor, note, timestamp_utc
FROM shop_operation_events WHERE operation_id = $id ORDER BY id;";
                command.Parameters.AddWithValue("$id", operationId);
                using var reader = command.ExecuteReader();
                var result = new List<ShopOperationEvent>();
                while (reader.Read())
                {
                    result.Add(new ShopOperationEvent(reader.GetInt64(0), reader.GetString(1),
                        reader.GetString(2), reader.GetString(3), reader.GetString(4),
                        reader.GetString(5), ParseTime(reader.GetString(6))));
                }
                return (IReadOnlyList<ShopOperationEvent>)result;
            });

        private T WithDatabase<T>(Func<SqliteConnection, T> action)
        {
            lock (m_Lock)
            {
                EnsureInitialized();
                using var connection = OpenConnection();
                using (var pragma = connection.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA busy_timeout=5000;";
                    pragma.ExecuteNonQuery();
                }
                return action(connection);
            }
        }

        private void EnsureInitialized()
        {
            if (m_Initialized) return;
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;
CREATE TABLE IF NOT EXISTS shop_operations(
    operation_id TEXT PRIMARY KEY,
    kind TEXT NOT NULL,
    player_id TEXT NOT NULL,
    player_type TEXT NOT NULL,
    player_name TEXT NOT NULL,
    catalog_key TEXT NOT NULL,
    item_plan TEXT NOT NULL,
    total TEXT NOT NULL,
    state TEXT NOT NULL,
    detail TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    resolution TEXT NOT NULL,
    resolved_by TEXT NOT NULL,
    resolution_note TEXT NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_shop_active_player
ON shop_operations(player_id)
WHERE state NOT IN ('completed', 'resolved');
CREATE TABLE IF NOT EXISTS shop_operation_events(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_id TEXT NOT NULL REFERENCES shop_operations(operation_id),
    from_state TEXT NOT NULL,
    to_state TEXT NOT NULL,
    actor TEXT NOT NULL,
    note TEXT NOT NULL,
    timestamp_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_shop_events_operation
ON shop_operation_events(operation_id, id);";
            command.ExecuteNonQuery();
            m_Initialized = true;
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = FilePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            connection.Open();
            return connection;
        }

        private static ShopOperationRecord? ReadOperation(
            SqliteConnection connection, string sql, string operationOrPlayerId)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", operationOrPlayerId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadOperation(reader) : null;
        }

        private static ShopOperationRecord ReadOperation(SqliteDataReader reader)
            => new ShopOperationRecord(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), ParseLines(reader.GetString(6)),
                ParseMoney(reader.GetString(7)), reader.GetString(8), reader.GetString(9),
                ParseTime(reader.GetString(10)), ParseTime(reader.GetString(11)), reader.GetString(12),
                reader.GetString(13), reader.GetString(14));

        private static string? ReadState(SqliteConnection connection, SqliteTransaction transaction, string operationId)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT state FROM shop_operations WHERE operation_id = $id;";
            command.Parameters.AddWithValue("$id", operationId);
            return command.ExecuteScalar() as string;
        }

        private static void InsertEvent(SqliteConnection connection, SqliteTransaction transaction,
            string operationId, string from, string to, string actor, string note, string timestamp)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO shop_operation_events(operation_id, from_state, to_state, actor, note, timestamp_utc)
VALUES($id, $from, $to, $actor, $note, $time);";
            command.Parameters.AddWithValue("$id", operationId);
            command.Parameters.AddWithValue("$from", from ?? string.Empty);
            command.Parameters.AddWithValue("$to", to ?? string.Empty);
            command.Parameters.AddWithValue("$actor", actor ?? string.Empty);
            command.Parameters.AddWithValue("$note", note ?? string.Empty);
            command.Parameters.AddWithValue("$time", timestamp);
            command.ExecuteNonQuery();
        }

        private static string SerializeLines(IEnumerable<ShopTradeLine> lines)
            => string.Join("\n", lines.Select(line =>
                line.ItemId.ToString(CultureInfo.InvariantCulture) + "|" +
                line.Count.ToString(CultureInfo.InvariantCulture) + "|" + Money(line.UnitPrice)));

        internal static IReadOnlyList<ShopTradeLine> ParseLines(string value)
        {
            var result = new List<ShopTradeLine>();
            foreach (var row in (value ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = row.Split('|');
                if (parts.Length != 3
                    || !ushort.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var itemId)
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                    || !decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                    throw new InvalidDataException("A persisted shop item plan is invalid.");
                result.Add(new ShopTradeLine(itemId, count, price));
            }
            return result;
        }

        private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);
        private static decimal ParseMoney(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
        private static string UtcNow() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        private static DateTime ParseTime(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }
}
