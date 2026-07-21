using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenMod.Extensions.Economy.Abstractions;
using UnturnedMods.Shared.Economy;

namespace well404.Shop
{
    internal enum ShopTradeFaultPoint
    {
        BeforeEconomy,
        AfterEconomy,
        BeforeInventory,
        AfterInventory,
        BeforeCompletion
    }

    internal interface IShopTradeFaultInjector
    {
        void ThrowIfRequested(ShopTradeFaultPoint point, string operationId);
    }

    internal sealed class NullShopTradeFaultInjector : IShopTradeFaultInjector
    {
        public void ThrowIfRequested(ShopTradeFaultPoint point, string operationId) { }
    }

    /// <summary>
    /// Executes the durable state machine. It deliberately performs no automatic recovery:
    /// an interrupted cross-system step stays quarantined until an administrator reviews it.
    /// </summary>
    internal sealed class ShopTradeSaga
    {
        private static readonly string[] s_AnyBuyPending =
        {
            ShopOperationStates.Prepared, ShopOperationStates.Debiting,
            ShopOperationStates.Debited, ShopOperationStates.Granting
        };
        private static readonly string[] s_AnySellPending =
        {
            ShopOperationStates.Prepared, ShopOperationStates.Taking,
            ShopOperationStates.Taken, ShopOperationStates.Crediting
        };

        private readonly ShopOperationStore m_Store;
        private readonly IIdempotentEconomyProvider m_Economy;
        private readonly IShopTradeFaultInjector m_Faults;
        private readonly ILogger m_Logger;
        private readonly SemaphoreSlim m_Gate = new SemaphoreSlim(1, 1);
        private readonly ShopTradeMutex m_CrossInstanceMutex;

        public ShopTradeSaga(ShopOperationStore store, IIdempotentEconomyProvider economy,
            IShopTradeFaultInjector faults, ILogger logger)
        {
            m_Store = store;
            m_Economy = economy;
            m_Faults = faults;
            m_Logger = logger;
            m_CrossInstanceMutex = new ShopTradeMutex(store.FilePath);
        }

        public async Task<ShopTradeResult> ExecuteBuyAsync(ShopOperationDraft draft, Func<Task> grantInventory)
        {
            await m_Gate.WaitAsync();
            try
            {
                using var crossInstanceLock = await m_CrossInstanceMutex.AcquireAsync();
                if (!m_Store.TryCreate(draft)) return Pending(draft.PlayerId);
                try
                {
                    RequireTransition(draft.OperationId, ShopOperationStates.Prepared,
                        ShopOperationStates.Debiting, "Debit is about to start.");
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeEconomy, draft.OperationId);
                    await m_Economy.ApplyOnceAsync(draft.OperationId + ":debit", draft.PlayerId,
                        draft.PlayerType, -draft.Total, "shop_buy:" + draft.CatalogKey);
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.AfterEconomy, draft.OperationId);
                    RequireTransition(draft.OperationId, ShopOperationStates.Debiting,
                        ShopOperationStates.Debited, "Debit completed durably.");
                    RequireTransition(draft.OperationId, ShopOperationStates.Debited,
                        ShopOperationStates.Granting, "Inventory grant is about to start.");
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeInventory, draft.OperationId);
                    await grantInventory();
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.AfterInventory, draft.OperationId);
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeCompletion, draft.OperationId);
                    RequireTransition(draft.OperationId, ShopOperationStates.Granting,
                        ShopOperationStates.Completed, "Inventory grant completed.");
                    return Completed(draft);
                }
                catch (NotEnoughBalanceException)
                {
                    m_Store.TryResolve(draft.OperationId,
                        new[] { ShopOperationStates.Debiting }, "rejected_insufficient_balance",
                        "system", "Economy definitively rejected the debit before commit.");
                    return new ShopTradeResult(ShopTradeStatus.InsufficientBalance, draft.OperationId);
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex,
                        "Shop buy {OperationId} stopped in durable state {State}; no automatic refund or grant will run.",
                        draft.OperationId, m_Store.Get(draft.OperationId)?.State);
                    return new ShopTradeResult(ShopTradeStatus.Quarantined, draft.OperationId,
                        detail: "The operation stopped at a cross-system boundary.");
                }
            }
            finally
            {
                m_Gate.Release();
            }
        }

        public async Task<ShopTradeResult> ExecuteSellAsync(
            ShopOperationDraft draft, Func<Task<bool>> takeInventory)
        {
            await m_Gate.WaitAsync();
            try
            {
                using var crossInstanceLock = await m_CrossInstanceMutex.AcquireAsync();
                if (!m_Store.TryCreate(draft)) return Pending(draft.PlayerId);
                try
                {
                    RequireTransition(draft.OperationId, ShopOperationStates.Prepared,
                        ShopOperationStates.Taking, "Inventory removal is about to start.");
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeInventory, draft.OperationId);
                    if (!await takeInventory())
                    {
                        m_Store.TryResolve(draft.OperationId,
                            new[] { ShopOperationStates.Taking }, "rejected_missing_items",
                            "system", "Inventory verification failed before removing any item.");
                        return new ShopTradeResult(ShopTradeStatus.NotEnoughItems, draft.OperationId);
                    }
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.AfterInventory, draft.OperationId);
                    RequireTransition(draft.OperationId, ShopOperationStates.Taking,
                        ShopOperationStates.Taken, "Inventory removal completed.");
                    RequireTransition(draft.OperationId, ShopOperationStates.Taken,
                        ShopOperationStates.Crediting, "Economy credit is about to start.");
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeEconomy, draft.OperationId);
                    await m_Economy.ApplyOnceAsync(draft.OperationId + ":credit", draft.PlayerId,
                        draft.PlayerType, draft.Total, "shop_sell:" + draft.CatalogKey);
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.AfterEconomy, draft.OperationId);
                    m_Faults.ThrowIfRequested(ShopTradeFaultPoint.BeforeCompletion, draft.OperationId);
                    RequireTransition(draft.OperationId, ShopOperationStates.Crediting,
                        ShopOperationStates.Completed, "Economy credit completed durably.");
                    return Completed(draft);
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex,
                        "Shop sell {OperationId} stopped in durable state {State}; no automatic credit or inventory replay will run.",
                        draft.OperationId, m_Store.Get(draft.OperationId)?.State);
                    return new ShopTradeResult(ShopTradeStatus.Quarantined, draft.OperationId,
                        detail: "The operation stopped at a cross-system boundary.");
                }
            }
            finally
            {
                m_Gate.Release();
            }
        }

        public async Task<ShopResolutionResult> ResolveAsync(string operationId, string action,
            string confirmation, string note, string actor)
        {
            if (!string.Equals(operationId, confirmation?.Trim(), StringComparison.Ordinal))
                return Fail("Type the exact operation ID to confirm this resolution.");
            note = note?.Trim() ?? string.Empty;
            if (note.Length < 8 || note.Length > 500)
                return Fail("Enter an audit note between 8 and 500 characters.");

            await m_Gate.WaitAsync();
            try
            {
                using var crossInstanceLock = await m_CrossInstanceMutex.AcquireAsync();
                var operation = m_Store.Get(operationId);
                if (operation == null || ShopOperationStates.IsTerminal(operation.State))
                    return Fail("The operation is no longer pending; reload the quarantine list.");
                if (!m_Economy.SupportsDurableOperations)
                    return Fail("Durable Economy is unavailable; no ledger resolution is safe.");

                if (operation.IsBuy)
                    return await ResolveBuyAsync(operation, action, actor, note);
                return await ResolveSellAsync(operation, action, actor, note);
            }
            catch (Exception ex)
            {
                m_Logger.LogWarning(ex, "Shop quarantine resolution failed for {OperationId}.", operationId);
                return Fail("Resolution failed; the operation remains quarantined.");
            }
            finally
            {
                m_Gate.Release();
            }
        }

        private async Task<ShopResolutionResult> ResolveBuyAsync(
            ShopOperationRecord operation, string action, string actor, string note)
        {
            var debit = await m_Economy.GetAppliedBalanceAsync(operation.OperationId + ":debit",
                operation.PlayerId, operation.PlayerType, -operation.Total);
            var refund = await m_Economy.GetAppliedBalanceAsync(operation.OperationId + ":refund",
                operation.PlayerId, operation.PlayerType, operation.Total);

            if (string.Equals(action, "abort-unpaid", StringComparison.Ordinal))
            {
                if (operation.State != ShopOperationStates.Prepared
                    && operation.State != ShopOperationStates.Debiting)
                    return Fail("Only prepared/debiting buys can be confirmed unpaid.");
                if (debit.HasValue || refund.HasValue)
                    return Fail("The durable ledger contains a debit or refund; this buy is not unpaid.");
                return Resolve(operation, s_AnyBuyPending, "aborted_unpaid", actor, note,
                    "The unpaid operation was closed.");
            }

            if (string.Equals(action, "confirm-delivered", StringComparison.Ordinal))
            {
                if (!debit.HasValue || refund.HasValue)
                    return Fail("A durable debit without refund is required before confirming delivery.");
                return Resolve(operation, s_AnyBuyPending, "confirmed_delivered", actor, note,
                    "Delivery was manually confirmed; the debit remains final.");
            }

            if (string.Equals(action, "retry-refund", StringComparison.Ordinal))
            {
                if (!debit.HasValue)
                    return Fail("No durable debit exists. Confirm it unpaid instead of issuing money.");
                if (!refund.HasValue)
                {
                    await m_Economy.ApplyOnceAsync(operation.OperationId + ":refund",
                        operation.PlayerId, operation.PlayerType, operation.Total,
                        "shop_buy_admin_refund");
                }
                return Resolve(operation, s_AnyBuyPending, "refunded_after_manual_inventory_review",
                    actor, note, "The idempotent refund was reconciled.");
            }

            return Fail("Unknown or unsafe buy resolution action.");
        }

        private async Task<ShopResolutionResult> ResolveSellAsync(
            ShopOperationRecord operation, string action, string actor, string note)
        {
            var credit = await m_Economy.GetAppliedBalanceAsync(operation.OperationId + ":credit",
                operation.PlayerId, operation.PlayerType, operation.Total);

            if (string.Equals(action, "retry-credit", StringComparison.Ordinal))
            {
                if (operation.State != ShopOperationStates.Taking
                    && operation.State != ShopOperationStates.Taken
                    && operation.State != ShopOperationStates.Crediting)
                    return Fail("This sale has not reached an inventory-removal boundary.");
                if (!credit.HasValue)
                {
                    await m_Economy.ApplyOnceAsync(operation.OperationId + ":credit",
                        operation.PlayerId, operation.PlayerType, operation.Total,
                        "shop_sell_admin_credit");
                }
                return Resolve(operation, s_AnySellPending, "credited_after_manual_inventory_review",
                    actor, note, "The idempotent sale credit was reconciled.");
            }

            if (string.Equals(action, "confirm-restored", StringComparison.Ordinal))
            {
                if (credit.HasValue)
                    return Fail("A durable credit already exists; the sale cannot be closed as restored.");
                return Resolve(operation, s_AnySellPending, "inventory_manually_restored",
                    actor, note, "Inventory restoration was manually confirmed without credit.");
            }

            return Fail("Unknown or unsafe sale resolution action.");
        }

        private ShopResolutionResult Resolve(ShopOperationRecord operation,
            IReadOnlyList<string> states, string resolution, string actor, string note, string success)
            => m_Store.TryResolve(operation.OperationId, states, resolution, actor, note)
                ? new ShopResolutionResult(true, success)
                : Fail("Operation state changed concurrently; reload and review it again.");

        private void RequireTransition(string operationId, string expected, string next, string note)
        {
            if (!m_Store.TryTransition(operationId, new[] { expected }, next, "system", note))
                throw new InvalidOperationException("Shop operation state changed concurrently.");
        }

        private ShopTradeResult Pending(string playerId)
        {
            var active = m_Store.GetActiveForPlayer(playerId);
            return new ShopTradeResult(ShopTradeStatus.PendingOperation,
                active?.OperationId ?? string.Empty);
        }

        private static ShopTradeResult Completed(ShopOperationDraft draft)
            => new ShopTradeResult(ShopTradeStatus.Completed, draft.OperationId,
                draft.Lines.Sum(line => line.Count), draft.Total);

        private static ShopResolutionResult Fail(string message) => new ShopResolutionResult(false, message);
    }
}
