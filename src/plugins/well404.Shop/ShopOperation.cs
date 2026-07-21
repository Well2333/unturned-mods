using System;
using System.Collections.Generic;

namespace well404.Shop
{
    public static class ShopOperationKinds
    {
        public const string Buy = "buy";
        public const string Sell = "sell";
        public const string SellGroup = "sell_group";
    }

    public static class ShopOperationStates
    {
        public const string Prepared = "prepared";
        public const string Debiting = "debiting";
        public const string Debited = "debited";
        public const string Granting = "granting";
        public const string Taking = "taking";
        public const string Taken = "taken";
        public const string Crediting = "crediting";
        public const string Completed = "completed";
        public const string Resolved = "resolved";

        public static bool IsTerminal(string state)
            => string.Equals(state, Completed, StringComparison.Ordinal)
                || string.Equals(state, Resolved, StringComparison.Ordinal);
    }

    public sealed class ShopTradeLine
    {
        public ShopTradeLine(ushort itemId, int count, decimal unitPrice)
        {
            if (itemId == 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (unitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(unitPrice));
            ItemId = itemId;
            Count = count;
            UnitPrice = unitPrice;
        }

        public ushort ItemId { get; }
        public int Count { get; }
        public decimal UnitPrice { get; }
    }

    public sealed class ShopOperationDraft
    {
        public ShopOperationDraft(
            string operationId, string kind, string playerId, string playerType,
            string playerName, string catalogKey, IReadOnlyList<ShopTradeLine> lines,
            decimal total)
        {
            OperationId = operationId;
            Kind = kind;
            PlayerId = playerId;
            PlayerType = playerType;
            PlayerName = playerName;
            CatalogKey = catalogKey;
            Lines = lines;
            Total = total;
        }

        public string OperationId { get; }
        public string Kind { get; }
        public string PlayerId { get; }
        public string PlayerType { get; }
        public string PlayerName { get; }
        public string CatalogKey { get; }
        public IReadOnlyList<ShopTradeLine> Lines { get; }
        public decimal Total { get; }
    }

    public sealed class ShopOperationRecord
    {
        public ShopOperationRecord(
            string operationId, string kind, string playerId, string playerType,
            string playerName, string catalogKey, IReadOnlyList<ShopTradeLine> lines,
            decimal total, string state, string detail, DateTime createdUtc,
            DateTime updatedUtc, string resolution, string resolvedBy, string resolutionNote)
        {
            OperationId = operationId;
            Kind = kind;
            PlayerId = playerId;
            PlayerType = playerType;
            PlayerName = playerName;
            CatalogKey = catalogKey;
            Lines = lines;
            Total = total;
            State = state;
            Detail = detail;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            Resolution = resolution;
            ResolvedBy = resolvedBy;
            ResolutionNote = resolutionNote;
        }

        public string OperationId { get; }
        public string Kind { get; }
        public string PlayerId { get; }
        public string PlayerType { get; }
        public string PlayerName { get; }
        public string CatalogKey { get; }
        public IReadOnlyList<ShopTradeLine> Lines { get; }
        public decimal Total { get; }
        public string State { get; }
        public string Detail { get; }
        public DateTime CreatedUtc { get; }
        public DateTime UpdatedUtc { get; }
        public string Resolution { get; }
        public string ResolvedBy { get; }
        public string ResolutionNote { get; }
        public bool IsBuy => string.Equals(Kind, ShopOperationKinds.Buy, StringComparison.Ordinal);
    }

    public sealed class ShopOperationEvent
    {
        public ShopOperationEvent(long id, string operationId, string fromState, string toState,
            string actor, string note, DateTime timestampUtc)
        {
            Id = id;
            OperationId = operationId;
            FromState = fromState;
            ToState = toState;
            Actor = actor;
            Note = note;
            TimestampUtc = timestampUtc;
        }

        public long Id { get; }
        public string OperationId { get; }
        public string FromState { get; }
        public string ToState { get; }
        public string Actor { get; }
        public string Note { get; }
        public DateTime TimestampUtc { get; }
    }

    public enum ShopTradeStatus
    {
        Completed,
        InsufficientBalance,
        NotEnoughItems,
        DurableEconomyRequired,
        PendingOperation,
        Quarantined,
        Invalid
    }

    public sealed class ShopTradeResult
    {
        public ShopTradeResult(ShopTradeStatus status, string operationId = "", int itemCount = 0,
            decimal total = 0m, string detail = "")
        {
            Status = status;
            OperationId = operationId;
            ItemCount = itemCount;
            Total = total;
            Detail = detail;
        }

        public ShopTradeStatus Status { get; }
        public string OperationId { get; }
        public int ItemCount { get; }
        public decimal Total { get; }
        public string Detail { get; }
    }

    public sealed class ShopResolutionResult
    {
        public ShopResolutionResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }
        public string Message { get; }
    }
}
