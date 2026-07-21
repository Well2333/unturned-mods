using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMod.Extensions.Economy.Abstractions;
using UnturnedMods.Shared.Economy;
using well404.Shop;
using Xunit;

namespace well404.Shop.Tests
{
    public sealed class ShopTradeSagaTests
    {
        [Fact]
        public void NonDurableEconomy_IsRejected()
            => Assert.False(ShopTradeCoordinator.SupportsDurableEconomy(new NonDurableEconomy()));

        [Fact]
        public void Store_PersistsStateAndAuditAcrossReopen()
        {
            var path = TempDatabase();
            try
            {
                var store = new ShopOperationStore(path);
                Assert.True(store.TryCreate(Draft("shop:persist", ShopOperationKinds.Buy)));
                Assert.True(store.TryTransition("shop:persist", new[] { ShopOperationStates.Prepared },
                    ShopOperationStates.Debiting, "system", "before debit"));

                var reopened = new ShopOperationStore(path);
                Assert.Equal(ShopOperationStates.Debiting, reopened.Get("shop:persist")!.State);
                Assert.Equal(2, reopened.GetEvents("shop:persist").Count);
                Assert.False(reopened.TryCreate(Draft("shop:second", ShopOperationKinds.Sell)));
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Buy_Success_ReachesCompletedWithFullAuditTrail()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = new ShopTradeSaga(store, economy, new NoFaults(), NullLogger.Instance);

                var result = await saga.ExecuteBuyAsync(Draft("shop:complete", ShopOperationKinds.Buy),
                    () => Task.CompletedTask);

                Assert.Equal(ShopTradeStatus.Completed, result.Status);
                Assert.Equal(90m, economy.Balance);
                Assert.Equal(ShopOperationStates.Completed, store.Get("shop:complete")!.State);
                Assert.Equal(5, store.GetEvents("shop:complete").Count);
                Assert.Empty(store.GetPending());
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Sell_DefiniteInventoryRejection_ResolvesWithoutCredit()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = new ShopTradeSaga(store, economy, new NoFaults(), NullLogger.Instance);

                var result = await saga.ExecuteSellAsync(Draft("shop:missing", ShopOperationKinds.Sell),
                    () => Task.FromResult(false));

                Assert.Equal(ShopTradeStatus.NotEnoughItems, result.Status);
                Assert.Equal(100m, economy.Balance);
                Assert.Equal(ShopOperationStates.Resolved, store.Get("shop:missing")!.State);
                Assert.Equal("rejected_missing_items", store.Get("shop:missing")!.Resolution);
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Buy_InventoryBoundaryFailure_QuarantinesWithoutAutomaticRefund()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = Saga(store, economy, ShopTradeFaultPoint.AfterInventory);
                var grants = 0;

                var result = await saga.ExecuteBuyAsync(Draft("shop:buy", ShopOperationKinds.Buy),
                    () => { grants++; return Task.CompletedTask; });

                Assert.Equal(ShopTradeStatus.Quarantined, result.Status);
                Assert.Equal(1, grants);
                Assert.Equal(90m, economy.Balance);
                Assert.Equal(ShopOperationStates.Granting, store.Get("shop:buy")!.State);
                Assert.False(economy.Applied.ContainsKey("shop:buy:refund"));

                var resolved = await saga.ResolveAsync("shop:buy", "retry-refund", "shop:buy",
                    "inventory manually removed", "admin:test");
                Assert.True(resolved.Success);
                Assert.Equal(100m, economy.Balance);
                Assert.Equal(ShopOperationStates.Resolved, store.Get("shop:buy")!.State);
                Assert.False((await saga.ResolveAsync("shop:buy", "retry-refund", "shop:buy",
                    "duplicate resolution attempt", "admin:test")).Success);
                Assert.Equal(100m, economy.Balance);
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Buy_LedgerSucceededBeforeStateWrite_StaysQuarantinedUntilReviewed()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = Saga(store, economy, ShopTradeFaultPoint.AfterEconomy);

                var result = await saga.ExecuteBuyAsync(Draft("shop:ledger", ShopOperationKinds.Buy),
                    () => Task.CompletedTask);

                Assert.Equal(ShopTradeStatus.Quarantined, result.Status);
                Assert.Equal(ShopOperationStates.Debiting, store.Get("shop:ledger")!.State);
                Assert.Equal(90m, economy.Balance);
                Assert.True((await saga.ResolveAsync("shop:ledger", "retry-refund", "shop:ledger",
                    "ledger checked by administrator", "admin:test")).Success);
                Assert.Equal(100m, economy.Balance);
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Sell_InventoryBoundaryFailure_DoesNotCreditUntilExplicitReview()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = Saga(store, economy, ShopTradeFaultPoint.AfterInventory);
                var removals = 0;

                var result = await saga.ExecuteSellAsync(Draft("shop:sell", ShopOperationKinds.Sell),
                    () => { removals++; return Task.FromResult(true); });

                Assert.Equal(ShopTradeStatus.Quarantined, result.Status);
                Assert.Equal(1, removals);
                Assert.Equal(100m, economy.Balance);
                Assert.Equal(ShopOperationStates.Taking, store.Get("shop:sell")!.State);

                Assert.True((await saga.ResolveAsync("shop:sell", "retry-credit", "shop:sell",
                    "removal manually confirmed", "admin:test")).Success);
                Assert.Equal(110m, economy.Balance);
                Assert.Equal(ShopOperationStates.Resolved, store.Get("shop:sell")!.State);
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task CrossInstanceResolution_WaitsForInFlightInventoryBoundary()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var worker = new ShopTradeSaga(new ShopOperationStore(path), economy,
                    new NoFaults(), NullLogger.Instance);
                var administrator = new ShopTradeSaga(new ShopOperationStore(path), economy,
                    new NoFaults(), NullLogger.Instance);
                var enteredInventory = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseInventory = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                var trade = worker.ExecuteBuyAsync(Draft("shop:reload", ShopOperationKinds.Buy),
                    async () =>
                    {
                        enteredInventory.TrySetResult(true);
                        await releaseInventory.Task;
                    });
                await enteredInventory.Task;

                var resolution = administrator.ResolveAsync("shop:reload", "retry-refund",
                    "shop:reload", "review during plugin reload", "admin:test");
                Assert.NotSame(resolution, await Task.WhenAny(resolution, Task.Delay(100)));

                releaseInventory.TrySetResult(true);
                Assert.Equal(ShopTradeStatus.Completed, (await trade).Status);
                Assert.False((await resolution).Success);
                Assert.Equal(90m, economy.Balance);
                Assert.False(economy.Applied.ContainsKey("shop:reload:refund"));
            }
            finally { DeleteDatabase(path); }
        }

        [Fact]
        public async Task Resolution_RequiresExactIdAndAuditNote()
        {
            var path = TempDatabase();
            try
            {
                var economy = new FakeEconomy(100m);
                var store = new ShopOperationStore(path);
                var saga = Saga(store, economy, ShopTradeFaultPoint.BeforeEconomy);
                await saga.ExecuteBuyAsync(Draft("shop:confirm", ShopOperationKinds.Buy),
                    () => Task.CompletedTask);

                Assert.False((await saga.ResolveAsync("shop:confirm", "abort-unpaid", "wrong",
                    "valid audit note", "admin:test")).Success);
                Assert.False((await saga.ResolveAsync("shop:confirm", "abort-unpaid", "shop:confirm",
                    "short", "admin:test")).Success);
                Assert.Equal(ShopOperationStates.Debiting, store.Get("shop:confirm")!.State);
            }
            finally { DeleteDatabase(path); }
        }

        private static ShopTradeSaga Saga(ShopOperationStore store, FakeEconomy economy,
            ShopTradeFaultPoint point) => new ShopTradeSaga(store, economy,
                new OneShotFault(point), NullLogger.Instance);

        private static ShopOperationDraft Draft(string id, string kind) => new ShopOperationDraft(
            id, kind, "player", "player", "Player", "catalog",
            new[] { new ShopTradeLine(1, 1, 10m) }, 10m);

        private static string TempDatabase() => Path.Combine(Path.GetTempPath(),
            "well404-shop-" + Guid.NewGuid().ToString("N") + ".sqlite3");

        private static void DeleteDatabase(string path)
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }

        private sealed class OneShotFault : IShopTradeFaultInjector
        {
            private readonly ShopTradeFaultPoint m_Point;
            private bool m_Thrown;
            public OneShotFault(ShopTradeFaultPoint point) => m_Point = point;
            public void ThrowIfRequested(ShopTradeFaultPoint point, string operationId)
            {
                if (!m_Thrown && point == m_Point)
                {
                    m_Thrown = true;
                    throw new InvalidOperationException("Injected failure at " + point);
                }
            }
        }

        private sealed class NoFaults : IShopTradeFaultInjector
        {
            public void ThrowIfRequested(ShopTradeFaultPoint point, string operationId) { }
        }

        private sealed class FakeEconomy : IIdempotentEconomyProvider
        {
            public FakeEconomy(decimal balance) => Balance = balance;
            public bool SupportsDurableOperations => true;
            public decimal Balance { get; private set; }
            public Dictionary<string, decimal> Applied { get; } = new Dictionary<string, decimal>();

            public Task<decimal> ApplyOnceAsync(string operationId, string ownerId,
                string ownerType, decimal changeAmount, string reason)
            {
                if (!Applied.ContainsKey(operationId))
                {
                    Applied[operationId] = changeAmount;
                    Balance += changeAmount;
                }
                return Task.FromResult(Balance);
            }

            public Task<decimal?> GetAppliedBalanceAsync(string operationId, string ownerId,
                string ownerType, decimal changeAmount)
                => Task.FromResult<decimal?>(Applied.TryGetValue(operationId, out var applied)
                    && applied == changeAmount ? Balance : null);
        }

        private sealed class NonDurableEconomy : IEconomyProvider
        {
            public string CurrencyName => "test";
            public string CurrencySymbol => "$";
            public Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
                => Task.FromResult(0m);
            public Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType,
                decimal changeAmount, string? reason) => Task.FromResult(changeAmount);
            public Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
                => Task.CompletedTask;
        }
    }
}
