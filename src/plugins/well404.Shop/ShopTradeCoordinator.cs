using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using UnturnedMods.Shared.Economy;

namespace well404.Shop
{
    /// <summary>Single entry point used by commands, player menu and group quick-sell.</summary>
    public sealed class ShopTradeCoordinator
    {
        private readonly ShopCatalog m_Catalog;
        private readonly ShopService m_Inventory;
        private readonly IEconomyProvider m_Economy;
        private readonly ILogger<ShopTradeCoordinator> m_Logger;
        private readonly object m_SagaLock = new object();
        private readonly object m_LifecycleLock = new object();
        private ShopOperationStore? m_Store;
        private ShopTradeSaga? m_Saga;
        private bool m_ShuttingDown;
        private int m_ActiveOperations;
        private TaskCompletionSource<bool>? m_Drained;

        public ShopTradeCoordinator(ShopCatalog catalog, ShopService inventory,
            IEconomyProvider economy, ILogger<ShopTradeCoordinator> logger)
        {
            m_Catalog = catalog;
            m_Inventory = inventory;
            m_Economy = economy;
            m_Logger = logger;
        }

        private ShopOperationStore Store => m_Store
            ?? throw new InvalidOperationException("Shop transaction storage is not initialized.");
        public string CurrencySymbol => m_Economy.CurrencySymbol;
        public bool SupportsDurableTrades => SupportsDurableEconomy(m_Economy);
        public IReadOnlyList<ShopOperationRecord> PendingOperations => Store.GetPending();

        internal static bool SupportsDurableEconomy(IEconomyProvider economy)
            => economy is IIdempotentEconomyProvider durable && durable.SupportsDurableOperations;

        public Task InitializeAsync(string databasePath)
        {
            lock (m_LifecycleLock) m_ShuttingDown = false;
            m_Store = new ShopOperationStore(databasePath);
            m_Store.Initialize();
            EnsureSaga();
            return Task.CompletedTask;
        }

        public Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
            => m_Economy.GetBalanceAsync(ownerId, ownerType);

        public Task ShutdownAsync()
        {
            lock (m_LifecycleLock)
            {
                m_ShuttingDown = true;
                if (m_ActiveOperations == 0) return Task.CompletedTask;
                return (m_Drained ?? (m_Drained = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously))).Task;
            }
        }

        public Task<ShopTradeResult> BuyAsync(
            UnturnedUser user, ShopEntry entry, int? requestedAmount, decimal unitPrice)
            => RunTradeAsync(() => BuyCoreAsync(user, entry, requestedAmount, unitPrice));

        public Task<ShopTradeResult> SellAsync(
            UnturnedUser user, ShopEntry entry, int? requestedAmount)
            => RunTradeAsync(() => SellCoreAsync(user, entry, requestedAmount));

        public Task<ShopTradeResult> SellGroupAsync(UnturnedUser user, string groupId)
            => RunTradeAsync(() => SellGroupCoreAsync(user, groupId));

        private async Task<ShopTradeResult> BuyCoreAsync(
            UnturnedUser user, ShopEntry entry, int? requestedAmount, decimal unitPrice)
        {
            var saga = EnsureSaga();
            if (saga == null)
                return new ShopTradeResult(ShopTradeStatus.DurableEconomyRequired);
            if (entry.BuyPrice <= 0m || unitPrice <= 0m)
                return new ShopTradeResult(ShopTradeStatus.Invalid);
            var amount = requestedAmount ?? 0;
            if (!requestedAmount.HasValue)
            {
                var balance = await m_Economy.GetBalanceAsync(user.Id, user.Type);
                var affordable = decimal.Floor(balance / unitPrice);
                amount = affordable > int.MaxValue ? int.MaxValue : (int)affordable;
            }
            if (amount <= 0) return new ShopTradeResult(ShopTradeStatus.InsufficientBalance);
            decimal total;
            try { total = checked(unitPrice * amount); }
            catch (OverflowException) { return new ShopTradeResult(ShopTradeStatus.Invalid); }
            var lines = ExactLines(entry, amount, unitPrice);
            var draft = Draft(user, ShopOperationKinds.Buy, entry.Id, lines, total);
            return await saga.ExecuteBuyAsync(draft, () => m_Inventory.GiveAsync(user, entry, amount));
        }

        private async Task<ShopTradeResult> SellCoreAsync(
            UnturnedUser user, ShopEntry entry, int? requestedAmount)
        {
            var saga = EnsureSaga();
            if (saga == null)
                return new ShopTradeResult(ShopTradeStatus.DurableEconomyRequired);
            if (entry.SellPrice <= 0m) return new ShopTradeResult(ShopTradeStatus.Invalid);
            var counts = await m_Inventory.GetInventoryCountsAsync(user);
            var available = ShopService.AvailableUnits(entry, counts);
            var amount = requestedAmount.HasValue ? Math.Min(requestedAmount.Value, available) : available;
            if (amount <= 0) return new ShopTradeResult(ShopTradeStatus.NotEnoughItems);
            decimal total;
            try { total = checked(entry.SellPrice * amount); }
            catch (OverflowException) { return new ShopTradeResult(ShopTradeStatus.Invalid); }
            var plan = ExactPlan(entry, amount);
            var lines = plan.Select(pair =>
                new ShopTradeLine(pair.Key, pair.Value, entry.SellPrice)).ToArray();
            var draft = Draft(user, ShopOperationKinds.Sell, entry.Id, lines, total);
            return await saga.ExecuteSellAsync(draft,
                () => m_Inventory.TryTakePlanAsync(user, plan));
        }

        private async Task<ShopTradeResult> SellGroupCoreAsync(UnturnedUser user, string groupId)
        {
            var saga = EnsureSaga();
            if (saga == null)
                return new ShopTradeResult(ShopTradeStatus.DurableEconomyRequired);
            var entries = m_Catalog.Entries.Where(entry =>
                string.Equals(entry.Group, groupId, StringComparison.OrdinalIgnoreCase)
                && entry.SellPrice > 0m).ToList();
            if (entries.Count == 0) return new ShopTradeResult(ShopTradeStatus.Invalid);
            var counts = await m_Inventory.GetInventoryCountsAsync(user);
            var lines = new List<ShopTradeLine>();
            var plan = new Dictionary<ushort, int>();
            decimal total = 0m;
            try
            {
                foreach (var entry in entries)
                {
                    counts.TryGetValue(entry.ItemId, out var count);
                    if (count <= 0) continue;
                    lines.Add(new ShopTradeLine(entry.ItemId, count, entry.SellPrice));
                    plan[entry.ItemId] = count;
                    total = checked(total + entry.SellPrice * count);
                }
            }
            catch (OverflowException)
            {
                return new ShopTradeResult(ShopTradeStatus.Invalid);
            }
            if (lines.Count == 0 || total <= 0m)
                return new ShopTradeResult(ShopTradeStatus.NotEnoughItems);
            var draft = Draft(user, ShopOperationKinds.SellGroup, groupId, lines, total);
            return await saga.ExecuteSellAsync(draft, () => m_Inventory.TryTakePlanAsync(user, plan));
        }

        public Task<ShopResolutionResult> ResolveAsync(string operationId, string action,
            string confirmation, string note, string actor = "web-admin")
            => RunResolutionAsync(() => ResolveCoreAsync(
                operationId, action, confirmation, note, actor));

        private Task<ShopResolutionResult> ResolveCoreAsync(string operationId, string action,
            string confirmation, string note, string actor)
        {
            var saga = EnsureSaga();
            if (saga == null)
                return Task.FromResult(new ShopResolutionResult(false,
                    "Shop trades require the durable SQLite Economy backend."));
            return saga.ResolveAsync(operationId, action, confirmation, note, actor);
        }

        private async Task<ShopTradeResult> RunTradeAsync(Func<Task<ShopTradeResult>> action)
        {
            if (!TryEnterOperation())
                return new ShopTradeResult(ShopTradeStatus.Invalid,
                    detail: "Shop is shutting down; retry after reload completes.");
            try { return await action(); }
            finally { ExitOperation(); }
        }

        private async Task<ShopResolutionResult> RunResolutionAsync(
            Func<Task<ShopResolutionResult>> action)
        {
            if (!TryEnterOperation())
                return new ShopResolutionResult(false,
                    "Shop is shutting down; retry after reload completes.");
            try { return await action(); }
            finally { ExitOperation(); }
        }

        private bool TryEnterOperation()
        {
            lock (m_LifecycleLock)
            {
                if (m_ShuttingDown) return false;
                m_ActiveOperations++;
                return true;
            }
        }

        private void ExitOperation()
        {
            TaskCompletionSource<bool>? drained = null;
            lock (m_LifecycleLock)
            {
                m_ActiveOperations--;
                if (m_ShuttingDown && m_ActiveOperations == 0)
                {
                    drained = m_Drained;
                    m_Drained = null;
                }
            }
            drained?.TrySetResult(true);
        }

        private ShopTradeSaga? EnsureSaga()
        {
            if (!(m_Economy is IIdempotentEconomyProvider durable)
                || !durable.SupportsDurableOperations || m_Store == null)
                return null;
            lock (m_SagaLock)
            {
                return m_Saga ?? (m_Saga = new ShopTradeSaga(m_Store, durable,
                    new NullShopTradeFaultInjector(), m_Logger));
            }
        }

        private static ShopOperationDraft Draft(UnturnedUser user, string kind,
            string catalogKey, IReadOnlyList<ShopTradeLine> lines, decimal total)
            => new ShopOperationDraft("shop:" + Guid.NewGuid().ToString("N"), kind,
                user.Id, user.Type, user.DisplayName, catalogKey, lines, total);

        private static IReadOnlyDictionary<ushort, int> ExactPlan(ShopEntry entry, int units)
        {
            var result = new Dictionary<ushort, int>();
            foreach (var pair in ShopService.ItemsPerUnit(entry))
                result[pair.Key] = checked(pair.Value * units);
            return result;
        }

        private static IReadOnlyList<ShopTradeLine> ExactLines(
            ShopEntry entry, int units, decimal orderUnitPrice)
            => ExactPlan(entry, units).Select(pair =>
                new ShopTradeLine(pair.Key, pair.Value, orderUnitPrice)).ToArray();
    }
}
