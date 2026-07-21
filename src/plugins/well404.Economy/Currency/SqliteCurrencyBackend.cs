using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OpenMod.Extensions.Economy.Abstractions;

namespace well404.Economy.Currency
{
    /// <summary>
    /// SQLite-backed single-file ledger. Accounts and transaction history are stored in normalized
    /// tables; every balance mutation is committed atomically in one SQLite transaction.
    /// </summary>
    public sealed class SqliteCurrencyBackend : ICurrencyBackend
    {
        private readonly object m_Lock = new object();
        private bool m_Initialized;

        public decimal StartingBalance { get; set; }

        public string FilePath { get; }

        public SqliteCurrencyBackend(string filePath, decimal startingBalance)
        {
            SqliteRuntime.Initialize();
            FilePath = filePath;
            StartingBalance = startingBalance;
        }

        public Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
        {
            var balance = WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT balance FROM accounts WHERE owner = $owner;";
                command.Parameters.AddWithValue("$owner", Key(ownerId, ownerType));
                var value = command.ExecuteScalar();
                return value == null || value == DBNull.Value
                    ? StartingBalance
                    : ParseDecimal(Convert.ToString(value, CultureInfo.InvariantCulture)!);
            });

            return Task.FromResult(balance);
        }

        public Task<IReadOnlyList<AccountSnapshot>> ListAccountsAsync()
        {
            var accounts = WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT owner, balance FROM accounts ORDER BY owner;";
                using var reader = command.ExecuteReader();
                var result = new List<AccountSnapshot>();
                while (reader.Read())
                {
                    var owner = reader.GetString(0);
                    var separator = owner.IndexOf(':');
                    var ownerType = separator >= 0 ? owner.Substring(0, separator) : string.Empty;
                    var ownerId = separator >= 0 ? owner.Substring(separator + 1) : owner;
                    result.Add(new AccountSnapshot(ownerType, ownerId, ParseDecimal(reader.GetString(1))));
                }

                return (IReadOnlyList<AccountSnapshot>)result;
            });

            return Task.FromResult(accounts);
        }

        public Task<decimal> UpdateBalanceAsync(
            string ownerId, string ownerType, decimal changeAmount, string? reason)
        {
            var updated = WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var owner = Key(ownerId, ownerType);
                var current = ReadBalance(connection, transaction, owner) ?? StartingBalance;
                var next = current + changeAmount;
                if (next < 0m)
                {
                    throw new NotEnoughBalanceException(
                        $"Not enough balance: needs {-changeAmount}, has {current}.", current);
                }

                UpsertBalance(connection, transaction, owner, next);
                AppendTransaction(connection, transaction, owner, changeAmount, next, reason);
                transaction.Commit();
                return next;
            });

            return Task.FromResult(updated);
        }

        public Task<decimal> ApplyOnceAsync(
            string operationId,
            string ownerId,
            string ownerType,
            decimal changeAmount,
            string? reason)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException("An operation ID is required.", nameof(operationId));
            }

            var updated = WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var owner = Key(ownerId, ownerType);
                using (var existing = connection.CreateCommand())
                {
                    existing.Transaction = transaction;
                    existing.CommandText = @"
SELECT owner, amount, balance_after
FROM idempotent_operations
WHERE operation_id = $operation;";
                    existing.Parameters.AddWithValue("$operation", operationId);
                    using var reader = existing.ExecuteReader();
                    if (reader.Read())
                    {
                        var existingOwner = reader.GetString(0);
                        var existingAmount = ParseDecimal(reader.GetString(1));
                        var existingBalance = ParseDecimal(reader.GetString(2));
                        if (!string.Equals(existingOwner, owner, StringComparison.Ordinal)
                            || existingAmount != changeAmount)
                        {
                            throw new InvalidOperationException(
                                "The idempotent economy operation ID was reused with different parameters.");
                        }

                        transaction.Commit();
                        return existingBalance;
                    }
                }

                var current = ReadBalance(connection, transaction, owner) ?? StartingBalance;
                var next = current + changeAmount;
                if (next < 0m)
                {
                    throw new NotEnoughBalanceException(
                        $"Not enough balance: needs {-changeAmount}, has {current}.", current);
                }

                UpsertBalance(connection, transaction, owner, next);
                AppendTransaction(connection, transaction, owner, changeAmount, next, reason);
                using (var insert = connection.CreateCommand())
                {
                    insert.Transaction = transaction;
                    insert.CommandText = @"
INSERT INTO idempotent_operations(
    operation_id, owner, amount, balance_after, reason, timestamp_utc)
VALUES($operation, $owner, $amount, $balance, $reason, $timestamp);";
                    insert.Parameters.AddWithValue("$operation", operationId);
                    insert.Parameters.AddWithValue("$owner", owner);
                    insert.Parameters.AddWithValue("$amount", FormatDecimal(changeAmount));
                    insert.Parameters.AddWithValue("$balance", FormatDecimal(next));
                    insert.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
                    insert.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    insert.ExecuteNonQuery();
                }

                transaction.Commit();
                return next;
            });

            return Task.FromResult(updated);
        }

        public Task<decimal?> GetAppliedBalanceAsync(
            string operationId,
            string ownerId,
            string ownerType,
            decimal changeAmount)
        {
            var result = WithDatabase(connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT owner, amount, balance_after
FROM idempotent_operations
WHERE operation_id = $operation;";
                command.Parameters.AddWithValue("$operation", operationId);
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return (decimal?)null;
                var owner = Key(ownerId, ownerType);
                if (!string.Equals(reader.GetString(0), owner, StringComparison.Ordinal)
                    || ParseDecimal(reader.GetString(1)) != changeAmount)
                {
                    throw new InvalidOperationException(
                        "The idempotent economy operation ID was reused with different parameters.");
                }
                return ParseDecimal(reader.GetString(2));
            });
            return Task.FromResult(result);
        }

        /// <summary>Moves currency between two accounts in one all-or-nothing SQLite transaction.</summary>
        public Task TransferAsync(
            string senderId, string senderType,
            string receiverId, string receiverType,
            decimal amount, decimal received, string? reason)
        {
            if (amount <= 0m || received < 0m || received > amount)
                throw new ArgumentOutOfRangeException(nameof(amount), "Transfer amounts are invalid.");

            var sender = Key(senderId, senderType);
            var receiver = Key(receiverId, receiverType);
            if (string.Equals(sender, receiver, StringComparison.Ordinal))
                throw new ArgumentException("Sender and receiver must be different accounts.");

            WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var senderBalance = ReadBalance(connection, transaction, sender) ?? StartingBalance;
                var receiverBalance = ReadBalance(connection, transaction, receiver) ?? StartingBalance;
                var nextSender = senderBalance - amount;
                if (nextSender < 0m)
                    throw new NotEnoughBalanceException(
                        $"Not enough balance: needs {amount}, has {senderBalance}.", senderBalance);

                var nextReceiver = checked(receiverBalance + received);
                UpsertBalance(connection, transaction, sender, nextSender);
                UpsertBalance(connection, transaction, receiver, nextReceiver);
                AppendTransaction(connection, transaction, sender, -amount, nextSender, reason);
                AppendTransaction(connection, transaction, receiver, received, nextReceiver, reason);
                transaction.Commit();
                return 0;
            });

            return Task.CompletedTask;
        }

        public Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
        {
            if (balance < 0m)
                throw new ArgumentOutOfRangeException(nameof(balance), "Balance cannot be negative.");
            WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var owner = Key(ownerId, ownerType);
                UpsertBalance(connection, transaction, owner, balance);
                AppendTransaction(connection, transaction, owner, 0m, balance, "set_balance");
                transaction.Commit();
                return 0;
            });

            return Task.CompletedTask;
        }

        public Task DeleteAccountAsync(string ownerId, string ownerType)
        {
            WithDatabase(connection =>
            {
                using var transaction = connection.BeginTransaction();
                var owner = Key(ownerId, ownerType);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM accounts WHERE owner = $owner;";
                    command.Parameters.AddWithValue("$owner", owner);
                    command.ExecuteNonQuery();
                }

                AppendTransaction(connection, transaction, owner, 0m, 0m, "delete_account");
                transaction.Commit();
                return 0;
            });

            return Task.CompletedTask;
        }

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
CREATE TABLE IF NOT EXISTS accounts (
    owner TEXT NOT NULL PRIMARY KEY,
    balance TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS transactions (
    id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    owner TEXT NOT NULL,
    amount TEXT NOT NULL,
    balance_after TEXT NOT NULL,
    reason TEXT NULL,
    timestamp_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_transactions_owner_time
    ON transactions(owner, timestamp_utc DESC);
CREATE TABLE IF NOT EXISTS idempotent_operations (
    operation_id TEXT NOT NULL PRIMARY KEY,
    owner TEXT NOT NULL,
    amount TEXT NOT NULL,
    balance_after TEXT NOT NULL,
    reason TEXT NULL,
    timestamp_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_idempotent_operations_owner_time
    ON idempotent_operations(owner, timestamp_utc DESC);";
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

        private static decimal? ReadBalance(
            SqliteConnection connection, SqliteTransaction transaction, string owner)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT balance FROM accounts WHERE owner = $owner;";
            command.Parameters.AddWithValue("$owner", owner);
            var value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? (decimal?)null
                : ParseDecimal(Convert.ToString(value, CultureInfo.InvariantCulture)!);
        }

        private static void UpsertBalance(
            SqliteConnection connection, SqliteTransaction transaction, string owner, decimal balance)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO accounts(owner, balance) VALUES($owner, $balance)
ON CONFLICT(owner) DO UPDATE SET balance = excluded.balance;";
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$balance", FormatDecimal(balance));
            command.ExecuteNonQuery();
        }

        private static void AppendTransaction(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string owner,
            decimal amount,
            decimal balanceAfter,
            string? reason)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO transactions(owner, amount, balance_after, reason, timestamp_utc)
VALUES($owner, $amount, $balanceAfter, $reason, $timestamp);";
            command.Parameters.AddWithValue("$owner", owner);
            command.Parameters.AddWithValue("$amount", FormatDecimal(amount));
            command.Parameters.AddWithValue("$balanceAfter", FormatDecimal(balanceAfter));
            command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$timestamp", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static string Key(string ownerId, string ownerType) => ownerType + ":" + ownerId;

        private static string FormatDecimal(decimal value)
            => value.ToString(CultureInfo.InvariantCulture);

        private static decimal ParseDecimal(string value)
            => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    }
}
