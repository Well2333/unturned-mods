using System;
using System.IO;
using System.Threading.Tasks;
using OpenMod.Extensions.Economy.Abstractions;
using well404.Economy.Currency;
using Xunit;

namespace well404.Economy.Tests
{
    public class LiteDbCurrencyBackendTests : IDisposable
    {
        private const string Player = "player";
        private readonly string m_File;

        public LiteDbCurrencyBackendTests()
        {
            m_File = Path.Combine(Path.GetTempPath(), "econtest-" + Guid.NewGuid().ToString("N") + ".db");
        }

        private LiteDbCurrencyBackend NewBackend(decimal startingBalance = 0m)
            => new LiteDbCurrencyBackend(m_File, startingBalance);

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
            var newBalance = await backend.UpdateBalanceAsync("a", Player, 50m, "test");
            Assert.Equal(50m, newBalance);
            Assert.Equal(50m, await backend.GetBalanceAsync("a", Player));

            await backend.UpdateBalanceAsync("a", Player, -20m, "test");
            Assert.Equal(30m, await backend.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task Update_GoingNegative_Throws_AndDoesNotChangeBalance()
        {
            var backend = NewBackend();
            await backend.UpdateBalanceAsync("a", Player, 10m, "seed");

            await Assert.ThrowsAsync<NotEnoughBalanceException>(
                () => backend.UpdateBalanceAsync("a", Player, -25m, "overdraw"));

            Assert.Equal(10m, await backend.GetBalanceAsync("a", Player));
        }

        [Fact]
        public async Task Transfer_MovesFundsBetweenAccounts()
        {
            var backend = NewBackend();
            await backend.UpdateBalanceAsync("a", Player, 100m, "seed");

            await backend.UpdateBalanceAsync("a", Player, -30m, "pay_out");
            await backend.UpdateBalanceAsync("b", Player, 30m, "pay_in");

            Assert.Equal(70m, await backend.GetBalanceAsync("a", Player));
            Assert.Equal(30m, await backend.GetBalanceAsync("b", Player));
        }

        [Fact]
        public async Task SetBalance_OverwritesAndPersistsAcrossReopen()
        {
            var backend = NewBackend();
            await backend.SetBalanceAsync("a", Player, 200m);
            Assert.Equal(200m, await backend.GetBalanceAsync("a", Player));

            // A fresh handle on the same file must see persisted data.
            var reopened = NewBackend();
            Assert.Equal(200m, await reopened.GetBalanceAsync("a", Player));
        }

        public void Dispose()
        {
            if (File.Exists(m_File))
            {
                File.Delete(m_File);
            }
        }
    }
}
