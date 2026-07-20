using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OpenMod.Extensions.Economy.Abstractions;
using well404.Economy.Currency;
using Xunit;

namespace well404.Economy.Tests
{
    public class SqliteCurrencyBackendTests : IDisposable
    {
        private const string Player = "player";
        private readonly string m_File;

        public SqliteCurrencyBackendTests()
        {
            m_File = Path.Combine(Path.GetTempPath(), "econtest-" + Guid.NewGuid().ToString("N") + ".sqlite3");
        }

        private SqliteCurrencyBackend NewBackend(decimal startingBalance = 0m)
            => new SqliteCurrencyBackend(m_File, startingBalance);

        [Fact]
        public async Task NewAccount_ReturnsStartingBalance()
        {
            var backend = NewBackend(startingBalance: 100m);
            Assert.Equal(100m, await backend.GetBalanceAsync("76561190000000000", Player));
        }

        [Fact]
        public async Task Update_AddsAndPersists()
        {
            var backend = NewBackend();
            Assert.Equal(50m, await backend.UpdateBalanceAsync("a", Player, 50m, "test"));
            Assert.Equal(50m, await backend.GetBalanceAsync("a", Player));

            await backend.UpdateBalanceAsync("a", Player, -20m, "test");
            Assert.Equal(30m, await backend.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task Update_GoingNegative_RollsBackAccountAndTransaction()
        {
            var backend = NewBackend();
            await backend.UpdateBalanceAsync("a", Player, 10m, "seed");

            await Assert.ThrowsAsync<NotEnoughBalanceException>(
                () => backend.UpdateBalanceAsync("a", Player, -25m, "overdraw"));

            Assert.Equal(10m, await backend.GetBalanceAsync("a", Player));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
        }

        [Fact]
        public async Task SetBalance_PersistsAcrossReopen()
        {
            var backend = NewBackend();
            await backend.SetBalanceAsync("a", Player, 200m);

            var reopened = NewBackend();
            Assert.Equal(200m, await reopened.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task DecimalBalance_RoundTripsWithoutFloatingPointLoss()
        {
            var backend = NewBackend();
            const decimal expected = 1234567890.123456789m;
            await backend.SetBalanceAsync("a", Player, expected);

            Assert.Equal(expected, await NewBackend().GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task ConcurrentUpdates_AreSerializedAndAtomic()
        {
            var backend = NewBackend();
            var updates = Enumerable.Range(0, 50)
                .Select(_ => Task.Run(() => backend.UpdateBalanceAsync("a", Player, 1m, "parallel")))
                .ToArray();

            await Task.WhenAll(updates);

            Assert.Equal(50m, await backend.GetBalanceAsync("a", Player));
            Assert.Equal(50L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
        }

        [Fact]
        public async Task ListAccounts_SeparatesOwnerTypeAndId()
        {
            var backend = NewBackend();
            await backend.SetBalanceAsync("7656119", Player, 42m);

            var accounts = await backend.ListAccountsAsync();

            var account = Assert.Single(accounts);
            Assert.Equal(Player, account.OwnerType);
            Assert.Equal("7656119", account.OwnerId);
            Assert.Equal(42m, account.Balance);
        }

        [Fact]
        public async Task Delete_RemovesAccountAndKeepsAuditRecord()
        {
            var backend = NewBackend();
            await backend.SetBalanceAsync("a", Player, 42m);
            await backend.DeleteAccountAsync("a", Player);

            Assert.Empty(await backend.ListAccountsAsync());
            Assert.Equal(2L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
        }

        [Fact]
        public async Task ApplyOnce_DebitsExactlyOnceAcrossReopen()
        {
            var backend = NewBackend(100m);

            Assert.Equal(75m, await backend.ApplyOnceAsync("purchase-1", "a", Player, -25m, "test"));
            Assert.Equal(75m, await NewBackend(100m).ApplyOnceAsync("purchase-1", "a", Player, -25m, "test"));
            Assert.Equal(75m, await backend.GetBalanceAsync("a", Player));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM idempotent_operations;"));
        }

        [Fact]
        public async Task ApplyOnce_RejectsOperationIdReuseWithDifferentAmount()
        {
            var backend = NewBackend(100m);
            await backend.ApplyOnceAsync("purchase-1", "a", Player, -25m, "test");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => backend.ApplyOnceAsync("purchase-1", "a", Player, -30m, "test"));
            Assert.Equal(75m, await backend.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task GetAppliedBalance_DistinguishesMissingAndCompletedOperations()
        {
            var backend = NewBackend(100m);
            Assert.Null(await backend.GetAppliedBalanceAsync("missing", "a", Player, -25m));
            await backend.ApplyOnceAsync("purchase-1", "a", Player, -25m, "test");
            Assert.Equal(75m, await backend.GetAppliedBalanceAsync("purchase-1", "a", Player, -25m));
        }

        [Fact]
        public async Task GetAppliedBalance_QueryFailureIsNotReportedAsMissing()
        {
            var backend = NewBackend(100m);
            Assert.Null(await backend.GetAppliedBalanceAsync("missing", "a", Player, -25m));
            using (var connection = new SqliteConnection("Data Source=" + m_File))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE idempotent_operations;";
                command.ExecuteNonQuery();
            }

            await Assert.ThrowsAsync<SqliteException>(
                () => backend.GetAppliedBalanceAsync("missing", "a", Player, -25m));
        }

        [Fact]
        public async Task ApplyOnce_InsufficientBalanceDoesNotReserveOperationId()
        {
            var backend = NewBackend(10m);

            await Assert.ThrowsAsync<NotEnoughBalanceException>(
                () => backend.ApplyOnceAsync("purchase-failed", "a", Player, -25m, "test"));

            Assert.Null(await backend.GetAppliedBalanceAsync("purchase-failed", "a", Player, -25m));
            Assert.Equal(0L, ScalarLong("SELECT COUNT(*) FROM idempotent_operations;"));
            Assert.Equal(10m, await backend.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task ApplyOnce_ConcurrentDuplicateOperationDebitsOnlyOnce()
        {
            var backend = NewBackend(100m);
            var attempts = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => backend.ApplyOnceAsync("parallel-purchase", "a", Player, -25m, "test")))
                .ToArray();

            var balances = await Task.WhenAll(attempts);

            Assert.All(balances, balance => Assert.Equal(75m, balance));
            Assert.Equal(75m, await backend.GetBalanceAsync("a", Player));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM idempotent_operations;"));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
        }

        [Fact]
        public async Task ApplyOnce_ConcurrentBackendsShareDatabaseIdempotency()
        {
            var first = NewBackend(100m);
            var second = NewBackend(100m);

            var balances = await Task.WhenAll(
                Task.Run(() => first.ApplyOnceAsync("cross-instance", "a", Player, -25m, "test")),
                Task.Run(() => second.ApplyOnceAsync("cross-instance", "a", Player, -25m, "test")));

            Assert.All(balances, balance => Assert.Equal(75m, balance));
            Assert.Equal(75m, await first.GetBalanceAsync("a", Player));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM idempotent_operations;"));
            Assert.Equal(1L, ScalarLong("SELECT COUNT(*) FROM transactions;"));
        }

        private long ScalarLong(string sql)
        {
            using var connection = new SqliteConnection("Data Source=" + m_File);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)command.ExecuteScalar()!;
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
