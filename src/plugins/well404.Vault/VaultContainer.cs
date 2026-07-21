using System;
using System.Collections.Generic;

namespace well404.Vault
{
    public static class VaultCapacityLimits
    {
        public const int Maximum = 1_000_000;

        public static int RequireValid(int value, string parameterName)
        {
            if (value < 1 || value > Maximum)
                throw new ArgumentOutOfRangeException(parameterName, value,
                    $"Vault capacity must be between 1 and {Maximum} grid cells.");
            return value;
        }
    }

    public enum VaultOwnerKind
    {
        Player,
        Team
    }

    public readonly struct VaultContainerRef : IEquatable<VaultContainerRef>
    {
        public VaultContainerRef(VaultOwnerKind kind, string key)
        {
            Kind = kind;
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public VaultOwnerKind Kind { get; }
        public string Key { get; }
        public string KindKey => Kind == VaultOwnerKind.Team ? "team" : "player";

        public static VaultContainerRef Player(string steamId)
            => new VaultContainerRef(VaultOwnerKind.Player, steamId);

        public static VaultContainerRef Team(string teamKey)
            => new VaultContainerRef(VaultOwnerKind.Team, teamKey);

        public bool Equals(VaultContainerRef other)
            => Kind == other.Kind && string.Equals(Key, other.Key, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is VaultContainerRef other && Equals(other);

        public override int GetHashCode()
            => ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Key);

        public override string ToString() => KindKey + ":" + Key;
    }

    public readonly struct VaultMoveResult
    {
        public VaultMoveResult(int moved, bool capacityReached)
        {
            Moved = moved;
            CapacityReached = capacityReached;
        }

        public int Moved { get; }
        public bool CapacityReached { get; }
    }

    public sealed class VaultContainerSnapshot
    {
        public VaultContainerSnapshot(
            long id,
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            int purchasedSlots,
            int adminAdjustment,
            int? legacyCapacityOverride,
            int usedSlots,
            long version,
            long purchaseVersion,
            int pendingPurchases,
            string status)
        {
            Id = id;
            Container = container;
            DisplayName = displayName;
            BaseCapacity = baseCapacity;
            PurchasedSlots = purchasedSlots;
            AdminAdjustment = adminAdjustment;
            LegacyCapacityOverride = legacyCapacityOverride;
            UsedSlots = usedSlots;
            Version = version;
            PurchaseVersion = purchaseVersion;
            PendingPurchases = pendingPurchases;
            Status = status;
        }

        public long Id { get; }
        public VaultContainerRef Container { get; }
        public string DisplayName { get; }
        public int BaseCapacity { get; }
        public int PurchasedSlots { get; }
        public int AdminAdjustment { get; }
        public int? LegacyCapacityOverride { get; }
        public int UsedSlots { get; }
        public long Version { get; }
        public long PurchaseVersion { get; }
        public int PendingPurchases { get; }
        public string Status { get; }
        public int Capacity
        {
            get
            {
                var calculated = LegacyCapacityOverride.HasValue
                    ? (long)LegacyCapacityOverride.Value + PurchasedSlots
                    : (long)BaseCapacity + PurchasedSlots + AdminAdjustment;
                if (calculated < 1 || calculated > VaultCapacityLimits.Maximum)
                    throw new InvalidOperationException(
                        $"Vault container {Container} has invalid effective capacity {calculated}.");
                return (int)calculated;
            }
        }
    }

    public enum TeamVaultPurchaseStatus
    {
        Purchased,
        Disabled,
        NotInTeam,
        EconomyUnavailable,
        MaximumReached,
        StaleRequest,
        InvalidConfiguration,
        Failed
    }

    public readonly struct TeamVaultPurchaseResult
    {
        public TeamVaultPurchaseResult(
            TeamVaultPurchaseStatus status,
            int slotsAdded,
            int newCapacity,
            decimal price,
            string? error = null)
        {
            Status = status;
            SlotsAdded = slotsAdded;
            NewCapacity = newCapacity;
            Price = price;
            Error = error;
        }

        public TeamVaultPurchaseStatus Status { get; }
        public int SlotsAdded { get; }
        public int NewCapacity { get; }
        public decimal Price { get; }
        public string? Error { get; }
        public bool Success => Status == TeamVaultPurchaseStatus.Purchased;
    }

    public sealed class PendingTeamVaultPurchase
    {
        public PendingTeamVaultPurchase(
            string operationId,
            string buyerSteamId,
            VaultContainerRef container,
            int slots,
            decimal price,
            string economyMode,
            string state,
            string createdUtc,
            string updatedUtc)
        {
            OperationId = operationId;
            BuyerSteamId = buyerSteamId;
            Container = container;
            Slots = slots;
            Price = price;
            EconomyMode = economyMode;
            State = state;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
        }

        public string OperationId { get; }
        public string BuyerSteamId { get; }
        public VaultContainerRef Container { get; }
        public string TeamKey => Container.Key;
        public int Slots { get; }
        public decimal Price { get; }
        public string EconomyMode { get; }
        public bool IsDurable => string.Equals(EconomyMode, "durable", StringComparison.Ordinal);
        public string State { get; }
        public string CreatedUtc { get; }
        public string UpdatedUtc { get; }
        public bool RefundOnly => string.Equals(State, "refund_pending", StringComparison.Ordinal);
    }

    public readonly struct TeamVaultPurchaseResolutionResult
    {
        public TeamVaultPurchaseResolutionResult(bool success, string message)
        {
            Success = success;
            Message = message;
        }

        public bool Success { get; }
        public string Message { get; }
    }

    public sealed class TeamVaultPurchaseResolution
    {
        public TeamVaultPurchaseResolution(long id, string operationId, string action, string actor,
            string note, string fromState, string toState, string createdUtc)
        {
            Id = id;
            OperationId = operationId;
            Action = action;
            Actor = actor;
            Note = note;
            FromState = fromState;
            ToState = toState;
            CreatedUtc = createdUtc;
        }

        public long Id { get; }
        public string OperationId { get; }
        public string Action { get; }
        public string Actor { get; }
        public string Note { get; }
        public string FromState { get; }
        public string ToState { get; }
        public string CreatedUtc { get; }
    }

    public sealed class InterruptedVaultTransfer
    {
        public InterruptedVaultTransfer(
            string operationId,
            VaultContainerRef container,
            string actorSteamId,
            string direction,
            string state,
            string createdUtc,
            string updatedUtc,
            IReadOnlyList<VaultTransferAuditItem> items)
        {
            OperationId = operationId;
            Container = container;
            ActorSteamId = actorSteamId;
            Direction = direction;
            State = state;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            Items = items;
        }

        public string OperationId { get; }
        public VaultContainerRef Container { get; }
        public string ActorSteamId { get; }
        public string Direction { get; }
        public string State { get; }
        public string CreatedUtc { get; }
        public string UpdatedUtc { get; }
        public IReadOnlyList<VaultTransferAuditItem> Items { get; }
    }

    public sealed class VaultTransferAuditItem
    {
        public VaultTransferAuditItem(StoredItem item, string stage)
        {
            Item = item;
            Stage = stage;
        }

        public StoredItem Item { get; }
        public string Stage { get; }
    }
}
