using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteDB;
using OpenMod.Extensions.Economy.Abstractions;

namespace well404.Economy.Currency
{
    /// <summary>
    /// Serverless single-file ledger backed by LiteDB. Accounts are keyed by
    /// <c>ownerType:ownerId</c>; every change also appends a transaction record
    /// for auditing.
    /// <para>
    /// Each operation opens and closes its own connection (rather than holding one
    /// open for the plugin's lifetime) so the database file is never left locked
    /// across plugin reloads. A lock serializes operations, which also makes the
    /// read-modify-write in <see cref="UpdateBalanceAsync"/> atomic.
    /// </para>
    /// </summary>
    public sealed class LiteDbCurrencyBackend : ICurrencyBackend
    {
        public sealed class Account
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;

            public decimal Balance { get; set; }
        }

        public sealed class Transaction
        {
            public ObjectId Id { get; set; } = ObjectId.NewObjectId();
            public string Owner { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public decimal BalanceAfter { get; set; }
            public string? Reason { get; set; }
            public DateTime Timestamp { get; set; }
        }

        private readonly object m_Lock = new object();

        /// <summary>Balance assumed for accounts that do not exist yet. Mutable so the
        /// provider can keep it in sync with config.</summary>
        public decimal StartingBalance { get; set; }

        public string FilePath { get; }

        public LiteDbCurrencyBackend(string filePath, decimal startingBalance)
        {
            FilePath = filePath;
            StartingBalance = startingBalance;
        }

        private static string Key(string ownerId, string ownerType) => ownerType + ":" + ownerId;

        private T WithDatabase<T>(Func<LiteDatabase, T> action)
        {
            lock (m_Lock)
            {
                using var database = new LiteDatabase(FilePath);
                return action(database);
            }
        }

        public Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
        {
            var balance = WithDatabase(database =>
            {
                var account = database.GetCollection<Account>("accounts").FindById(Key(ownerId, ownerType));
                return account?.Balance ?? StartingBalance;
            });

            return Task.FromResult(balance);
        }

        public Task<IReadOnlyList<AccountSnapshot>> ListAccountsAsync()
        {
            var snapshots = WithDatabase(database =>
            {
                var result = new List<AccountSnapshot>();
                foreach (var account in database.GetCollection<Account>("accounts").FindAll())
                {
                    // Keys are "ownerType:ownerId" (see Key()).
                    var separator = account.Id.IndexOf(':');
                    var ownerType = separator >= 0 ? account.Id.Substring(0, separator) : string.Empty;
                    var ownerId = separator >= 0 ? account.Id.Substring(separator + 1) : account.Id;
                    result.Add(new AccountSnapshot(ownerType, ownerId, account.Balance));
                }

                return (IReadOnlyList<AccountSnapshot>)result;
            });

            return Task.FromResult(snapshots);
        }

        public Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason)
        {
            var updated = WithDatabase(database =>
            {
                var key = Key(ownerId, ownerType);
                var accounts = database.GetCollection<Account>("accounts");
                var account = accounts.FindById(key);
                var current = account?.Balance ?? StartingBalance;
                var next = current + changeAmount;

                if (next < 0m)
                {
                    throw new NotEnoughBalanceException(
                        $"Not enough balance: needs {-changeAmount}, has {current}.", current);
                }

                accounts.Upsert(new Account { Id = key, Balance = next });
                AppendTransaction(database, key, changeAmount, next, reason);
                return next;
            });

            return Task.FromResult(updated);
        }

        public Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
        {
            WithDatabase<object?>(database =>
            {
                var key = Key(ownerId, ownerType);
                database.GetCollection<Account>("accounts").Upsert(new Account { Id = key, Balance = balance });
                AppendTransaction(database, key, 0m, balance, "set_balance");
                return null;
            });

            return Task.CompletedTask;
        }

        public Task DeleteAccountAsync(string ownerId, string ownerType)
        {
            WithDatabase<object?>(database =>
            {
                var key = Key(ownerId, ownerType);
                database.GetCollection<Account>("accounts").Delete(key);
                AppendTransaction(database, key, 0m, 0m, "delete_account");
                return null;
            });

            return Task.CompletedTask;
        }

        private static void AppendTransaction(
            LiteDatabase database, string owner, decimal amount, decimal balanceAfter, string? reason)
        {
            database.GetCollection<Transaction>("transactions").Insert(new Transaction
            {
                Owner = owner,
                Amount = amount,
                BalanceAfter = balanceAfter,
                Reason = reason,
                Timestamp = DateTime.UtcNow
            });
        }
    }
}
