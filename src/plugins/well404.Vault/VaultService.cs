using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenMod.API.Permissions;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;
using SDG.Unturned;
using UnturnedMods.Shared.Economy;
using UnturnedMods.Shared.Teams;
using UnityEngine;

namespace well404.Vault
{
    /// <summary>Outcome of a store request: how many were stored, and whether capacity ran out.</summary>
    public readonly struct StoreResult
    {
        public StoreResult(int stored, bool capacityReached) { Stored = stored; CapacityReached = capacityReached; }
        public int Stored { get; }
        public bool CapacityReached { get; }
    }

    /// <summary>
    /// A raw item variant (copies with identical id + amount + quality + state) and
    /// how many copies of it there are. <see cref="State"/> is the base64 of the raw item state.
    /// </summary>
    public readonly struct ItemVariant
    {
        public ItemVariant(ushort itemId, byte amount, byte quality, string state, int count, int slotCost, byte maxAmount, byte inventoryPage = byte.MaxValue)
        {
            ItemId = itemId;
            Amount = amount;
            Quality = quality;
            State = state;
            Count = count;
            SlotCost = slotCost;
            MaxAmount = maxAmount;
            InventoryPage = inventoryPage;
        }

        public ushort ItemId { get; }
        public byte Amount { get; }
        public byte Quality { get; }
        public string State { get; }
        public int Count { get; }
        public int SlotCost { get; }

        /// <summary>Full stack/magazine capacity (e.g. 8 for an 8-round shell box); 0 if unknown.</summary>
        public byte MaxAmount { get; }

        /// <summary>The Unturned inventory page this carried variant came from, or byte.MaxValue for stored items.</summary>
        public byte InventoryPage { get; }
    }

    /// <summary>
    /// The per-player vault: moves items between a player's backpack and persistent storage with
    /// full item-state fidelity (a stored item keeps its quality and raw state bytes — attachments,
    /// the rounds inside a magazine/ammo box, etc.). Capacity is counted in grid cells: each item
    /// costs its asset's footprint (size_x × size_y), never its internal stack/ammo count. Capacity
    /// is per-player: a config base, raised by any permission tier the player holds. Purchased slots and administrator adjustments are persisted per container. Registered as a plugin-scoped singleton.
    ///
    /// Threading: SDG inventory/asset access runs on the Unity main thread; SQLite access is
    /// serialized by SqliteVaultStore and each mutation is committed transactionally.
    /// </summary>
    public class VaultService
    {
        private readonly IConfiguration m_Configuration;
        private readonly IPermissionChecker m_PermissionChecker;
        private readonly ILifetimeScope m_LifetimeScope;
        private readonly ILogger<VaultService> m_Logger;
        private const int RecoveryBatchSize = 25;
        private const int BuyerRecoveryBatchSize = 5;
        private static readonly TimeSpan s_ProviderTimeout = TimeSpan.FromSeconds(3);
        private readonly SemaphoreSlim m_PurchaseRecoveryGate = new SemaphoreSlim(1, 1);
        private SqliteVaultStore? m_Store;

        private SqliteVaultStore Store => m_Store
            ?? throw new InvalidOperationException("The vault database is not initialized.");

        public VaultService(
            IConfiguration configuration,
            IPermissionChecker permissionChecker,
            ILifetimeScope lifetimeScope,
            ILogger<VaultService> logger)
        {
            m_Configuration = configuration;
            m_PermissionChecker = permissionChecker;
            m_LifetimeScope = lifetimeScope;
            m_Logger = logger;
        }

        private VaultSettings Settings => m_Configuration.Get<VaultSettings>() ?? new VaultSettings();
        private TeamVaultSettings TeamSettings => Settings.TeamVault ?? new TeamVaultSettings();

        /// <summary>The configured personal base capacity before permission tiers, purchases, and administrator adjustment; at least 1.</summary>
        public int BaseMaxSlots => NormalizeConfiguredCapacity(Settings.MaxSlots);

        public PersonalVaultPurchaseSettings CurrentPersonalPurchaseSettings
        {
            get
            {
                var source = Settings.PersonalPurchase ?? new PersonalVaultPurchaseSettings();
                return new PersonalVaultPurchaseSettings
                {
                    Enabled = source.Enabled,
                    MaxSlots = source.MaxSlots,
                    SlotsPerPurchase = source.SlotsPerPurchase,
                    Price = source.Price
                };
            }
        }

        public TeamVaultSettings CurrentTeamSettings
        {
            get
            {
                var source = TeamSettings;
                return new TeamVaultSettings
                {
                    Enabled = source.Enabled,
                    BaseSlots = source.BaseSlots,
                    MaxSlots = source.MaxSlots,
                    Purchase = new TeamVaultPurchaseSettings
                    {
                        Enabled = source.Purchase?.Enabled ?? true,
                        SlotsPerPurchase = source.Purchase?.SlotsPerPurchase ?? 10,
                        Price = source.Purchase?.Price ?? 500m
                    }
                };
            }
        }

        /// <summary>Creates the SQLite schema. Existing YAML data is intentionally ignored.</summary>
        public Task InitializeAsync(string databasePath)
        {
            m_Store = new SqliteVaultStore(databasePath);
            m_Store.Initialize();
            return Task.CompletedTask;
        }

        public string CurrencySymbol
        {
            get
            {
                try { return m_LifetimeScope.ResolveOptional<IEconomyProvider>()?.CurrencySymbol ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        // ----- capacity -----

        /// <summary>The effective capacity for an online player: the largest configured base/tier,
        /// purchased slots, and any administrator adjustment stored on the container.</summary>
        public async Task<int> GetMaxSlotsAsync(UnturnedUser user)
            => (await GetPersonalVaultAsync(user)).Capacity;

        private async Task<int> GetPersonalBaseSlotsAsync(UnturnedUser user)
        {
            var settings = Settings;
            var best = NormalizeConfiguredCapacity(settings.MaxSlots);
            foreach (var tier in settings.Tiers)
            {
                if (await m_PermissionChecker.CheckPermissionAsync(user, tier.Key).ConfigureAwait(false) == PermissionGrantResult.Grant)
                    best = Math.Max(best, NormalizeConfiguredCapacity(tier.Value));
            }
            return best;
        }

        public async Task<VaultContainerSnapshot> GetPersonalVaultAsync(UnturnedUser user)
        {
            var baseSlots = await GetPersonalBaseSlotsAsync(user);
            return Store.GetOrCreateContainer(VaultContainerRef.Player(user.Id), user.DisplayName, baseSlots);
        }

        /// <summary>Capacity known without the online user, using the persisted container when present.</summary>
        public int OverrideOrBase(string steamId)
        {
            var container = VaultContainerRef.Player(steamId);
            var existing = Store.GetContainer(container);
            return existing?.Capacity
                ?? Store.GetOrCreateContainer(container, string.Empty, BaseMaxSlots).Capacity;
        }

        // ----- queries -----

        /// <summary>A snapshot of a player's stored items.</summary>
        public IReadOnlyList<StoredItem> Get(string steamId) => Store.Get(steamId);

        public IReadOnlyList<StoredItem> Get(VaultContainerRef container) => Store.Get(container);

        /// <summary>Grid cells currently used by a player's vault.</summary>
        public int UsedSlots(string steamId) => Store.UsedSlots(steamId);

        public int UsedSlots(VaultContainerRef container) => Store.UsedSlots(container);

        public IReadOnlyList<VaultOwner> Owners => Store.GetOwners();

        public IReadOnlyList<VaultContainerSnapshot> Containers => Store.GetContainers();

        public IReadOnlyList<VaultContainerSnapshot> TeamContainers => Store.GetTeamContainers();

        public VaultContainerSnapshot GetContainer(VaultContainerRef container)
        {
            var existing = Store.GetContainer(container);
            if (existing != null) return existing;
            var baseSlots = container.Kind == VaultOwnerKind.Team
                ? NormalizeConfiguredCapacity(TeamSettings.BaseSlots)
                : BaseMaxSlots;
            return Store.GetOrCreateContainer(container, string.Empty, baseSlots);
        }

        public VaultContainerSnapshot? SetContainerCapacity(VaultContainerRef container, int capacity)
        {
            var existing = Store.GetContainer(container);
            var baseSlots = existing?.BaseCapacity ?? (container.Kind == VaultOwnerKind.Team
                ? NormalizeConfiguredCapacity(TeamSettings.BaseSlots)
                : BaseMaxSlots);
            if (capacity < 1 || capacity > VaultCapacityLimits.Maximum) return null;
            return Store.SetContainerCapacity(
                container, existing?.DisplayName ?? string.Empty, baseSlots, capacity);
        }

        public int InterruptedTransferCount => Store.InterruptedTransferCount();

        public IReadOnlyList<InterruptedVaultTransfer> InterruptedTransfers
            => Store.GetInterruptedTransfers();

        public IReadOnlyList<PendingTeamVaultPurchase> PendingTeamPurchases
            => Store.GetPendingTeamPurchases();

        public IReadOnlyList<TeamVaultPurchaseResolution> PurchaseResolutionAudit
            => Store.GetTeamPurchaseResolutions();

        public int PendingTeamPurchaseCount(VaultContainerRef container)
            => Store.PendingTeamPurchaseCount(container);

        public async Task<(TeamContext Context, VaultContainerSnapshot Container)?> GetTeamVaultAsync(
            UnturnedUser user)
        {
            var settings = TeamSettings;
            if (!settings.Enabled) return null;
            var context = await ResolveTeamAsync(user);
            if (context == null) return null;
            var container = Store.GetOrCreateContainer(
                VaultContainerRef.Team(context.Key),
                context.DisplayName,
                NormalizeConfiguredCapacity(settings.BaseSlots));
            return (context, container);
        }

        public void TouchOwner(UnturnedUser user)
        {
            var playerId = user.Player?.SteamPlayer?.playerID;
            var gameName = playerId?.characterName ?? user.DisplayName ?? string.Empty;
            var steamName = playerId?.playerName ?? string.Empty;
            Store.TouchOwner(user.Id, gameName, steamName);
        }

        /// <summary>
        /// Updates the editable fields of one stored row. Asset-derived capacity metadata is
        /// recalculated on Unity's main thread and the opaque item state is intentionally preserved.
        /// </summary>
        public async Task<bool> UpdateStoredItemAsync(
            string steamId, long recordId, ushort itemId, byte amount, byte quality)
            => await UpdateStoredItemAsync(
                VaultContainerRef.Player(steamId), recordId, itemId, amount, quality);

        public async Task<bool> UpdateStoredItemAsync(
            VaultContainerRef container, long recordId, ushort itemId, byte amount, byte quality)
        {
            await UniTask.SwitchToMainThread();
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            if (asset == null) return false;
            var current = Store.Get(container).FirstOrDefault(row => row.RecordId == recordId);
            if (current == null) return false;
            string? replacementState = null;
            if (current.ItemId != itemId)
            {
                var template = new Item(itemId, EItemOrigin.ADMIN);
                replacementState = template.state != null && template.state.Length > 0
                    ? Convert.ToBase64String(template.state)
                    : string.Empty;
            }
            return Store.UpdateItem(
                container,
                recordId,
                itemId,
                amount,
                quality,
                SlotCostOf(itemId),
                MaxAmountOf(itemId),
                replacementState);
        }

        public async Task<int> AddStoredItemsAsync(
            string steamId, ushort itemId, byte amount, byte quality, int count)
            => await AddStoredItemsAsync(
                VaultContainerRef.Player(steamId), itemId, amount, quality, count);

        public async Task<int> AddStoredItemsAsync(
            VaultContainerRef container, ushort itemId, byte amount, byte quality, int count)
        {
            await UniTask.SwitchToMainThread();
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            if (asset == null || count < 1)
            {
                return 0;
            }

            var template = new Item(itemId, EItemOrigin.ADMIN)
            {
                amount = amount,
                quality = quality
            };
            var state = template.state != null && template.state.Length > 0
                ? Convert.ToBase64String(template.state)
                : string.Empty;
            var rows = Enumerable.Range(0, count)
                .Select(_ => new StoredItem
                {
                    ItemId = itemId,
                    Amount = amount,
                    Quality = quality,
                    State = state,
                    SlotCost = SlotCostOf(itemId),
                    MaxAmount = MaxAmountOf(itemId)
                })
                .ToList();
            var snapshot = GetContainer(container);
            Store.AddItems(container, snapshot.DisplayName, snapshot.BaseCapacity, rows);
            return rows.Count;
        }

        public Task<bool> DeleteStoredItemAsync(string steamId, long recordId)
            => Task.FromResult(Store.DeleteItem(steamId, recordId));

        public Task<bool> DeleteStoredItemAsync(VaultContainerRef container, long recordId)
            => Task.FromResult(Store.DeleteItem(container, recordId));

        public Task<int> DeleteStoredItemsAsync(string steamId, ushort itemId)
            => Task.FromResult(Store.DeleteItems(steamId, itemId));

        public Task<int> DeleteStoredItemsAsync(VaultContainerRef container, ushort itemId)
            => Task.FromResult(Store.DeleteItems(container, itemId));

        /// <summary>The grid footprint of one item id (size_x × size_y), or 1 if the asset is unknown.</summary>
        public static int SlotCostOf(ushort itemId)
        {
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            return asset == null ? 1 : Math.Max(1, asset.size_x * asset.size_y);
        }

        /// <summary>Full stack/magazine capacity for an item id (e.g. an 8-round shell box → 8), or 0 if
        /// the item isn't a magazine/ammo item. Asset lookup → main thread only.</summary>
        public static byte MaxAmountOf(ushort itemId)
            => Assets.find(EAssetType.ITEM, itemId) is ItemMagazineAsset mag ? mag.amount : (byte)0;

        // ----- variants (copies with identical id+amount+quality+state are one variant) -----

        /// <summary>The vault's contents collapsed into distinct variants (merging identical copies).</summary>
        public IReadOnlyList<ItemVariant> VaultVariants(string steamId)
            => VaultVariants(VaultContainerRef.Player(steamId));

        public IReadOnlyList<ItemVariant> VaultVariants(VaultContainerRef container)
        {
            var result = new List<ItemVariant>();
            foreach (var group in Store.Get(container)
                .GroupBy(x => (x.ItemId, x.Amount, x.Quality, x.State))
                .OrderBy(g => g.Key.ItemId))
            {
                var first = group.First();
                result.Add(new ItemVariant(first.ItemId, first.Amount, first.Quality, first.State,
                    group.Count(), first.SlotCost, first.MaxAmount));
            }

            return result;
        }

        public async Task<IReadOnlyList<ItemVariant>> TeamVaultVariantsAsync(UnturnedUser user)
        {
            var team = await GetTeamVaultAsync(user);
            return team == null
                ? (IReadOnlyList<ItemVariant>)Array.Empty<ItemVariant>()
                : VaultVariants(VaultContainerRef.Team(team.Value.Context.Key));
        }

        /// <summary>The player's carried items collapsed into distinct variants.</summary>
        public async Task<IReadOnlyList<ItemVariant>> BackpackVariantsAsync(UnturnedUser user)
        {
            await UniTask.SwitchToMainThread();
            TouchOwner(user);
            var player = user.Player?.Player;
            if (player == null)
            {
                return Array.Empty<ItemVariant>();
            }

            var inventory = player.inventory;
            var map = new Dictionary<(ushort, byte, byte, string, byte), int>();
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE; page++)
            {
                var count = inventory.getItemCount(page);
                for (byte i = 0; i < count; i++)
                {
                    var jar = inventory.getItem(page, i);
                    if (jar?.item == null)
                    {
                        continue;
                    }

                    var item = jar.item;
                    var state = item.state != null && item.state.Length > 0 ? Convert.ToBase64String(item.state) : string.Empty;
                    var key = (item.id, item.amount, item.quality, state, page);
                    map.TryGetValue(key, out var existing);
                    map[key] = existing + 1;
                }
            }

            return map.Select(kv => new ItemVariant(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Key.Item4,
                kv.Value, SlotCostOf(kv.Key.Item1), MaxAmountOf(kv.Key.Item1), kv.Key.Item5)).ToList();
        }

        internal static bool IsCarriedInventoryPage(byte page)
            => page >= PlayerInventory.SLOTS && page < PlayerInventory.STORAGE;

        // ----- store -----

        /// <summary>Stores up to <paramref name="amount"/> copies of <paramref name="itemId"/> (any variant).</summary>
        public async Task<StoreResult> StoreAsync(UnturnedUser user, ushort itemId, int amount, byte? inventoryPage = null)
        {
            var personal = await GetPersonalVaultAsync(user);
            return await StoreMatchingAsync(
                user,
                VaultContainerRef.Player(user.Id),
                user.DisplayName,
                personal.BaseCapacity,
                personal.Capacity,
                itemId,
                _ => true,
                amount,
                inventoryPage);
        }

        /// <summary>Stores up to <paramref name="count"/> copies matching amount and state; quality is optional for assets that do not expose durability.</summary>
        public async Task<StoreResult> StoreVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte? quality, byte[] state, int count, byte? inventoryPage = null)
        {
            var personal = await GetPersonalVaultAsync(user);
            return await StoreMatchingAsync(
                user,
                VaultContainerRef.Player(user.Id),
                user.DisplayName,
                personal.BaseCapacity,
                personal.Capacity,
                itemId,
                it => it.amount == amount
                    && (!quality.HasValue || it.quality == quality.Value)
                    && BytesEqual(it.state, state),
                count,
                inventoryPage);
        }

        public async Task<StoreResult> StoreTeamAsync(UnturnedUser user, ushort itemId, int amount, byte? inventoryPage = null)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return new StoreResult(0, false);
            var settings = TeamSettings;
            return await StoreMatchingAsync(
                user,
                VaultContainerRef.Team(team.Value.Context.Key),
                team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(settings.BaseSlots),
                Math.Max(team.Value.Container.Capacity, Math.Max(settings.BaseSlots, settings.MaxSlots)),
                itemId,
                _ => true,
                amount,
                inventoryPage);
        }

        public async Task<StoreResult> StoreTeamVariantAsync(
            UnturnedUser user,
            ushort itemId,
            byte amount,
            byte? quality,
            byte[] state,
            int count,
            byte? inventoryPage = null)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return new StoreResult(0, false);
            var settings = TeamSettings;
            return await StoreMatchingAsync(
                user,
                VaultContainerRef.Team(team.Value.Context.Key),
                team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(settings.BaseSlots),
                Math.Max(team.Value.Container.Capacity, Math.Max(settings.BaseSlots, settings.MaxSlots)),
                itemId,
                it => it.amount == amount
                    && (!quality.HasValue || it.quality == quality.Value)
                    && BytesEqual(it.state, state),
                count,
                inventoryPage);
        }

        private async Task<StoreResult> StoreMatchingAsync(
            UnturnedUser user,
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            int maximumCapacity,
            ushort itemId,
            Func<Item, bool> matches,
            int amount,
            byte? inventoryPage = null)
        {
            var snapshot = Store.GetOrCreateContainer(container, displayName, baseCapacity);
            var max = Math.Min(snapshot.Capacity, Math.Max(1, maximumCapacity));
            var used = snapshot.UsedSlots;

            await UniTask.SwitchToMainThread();
            TouchOwner(user);
            if (!await StillOwnsContainerAsync(user, container))
            {
                return new StoreResult(0, false);
            }
            var player = user.Player?.Player;
            if (player == null)
            {
                return new StoreResult(0, false);
            }

            var inventory = player.inventory;
            var cost = SlotCostOf(itemId);
            var maxAmount = MaxAmountOf(itemId);

            // Gather candidate jars (worn containers; held weapon slots are intentionally excluded).
            var targets = new List<(byte page, byte x, byte y, StoredItem snapshot)>();
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE && targets.Count < amount; page++)
            {
                if (inventoryPage.HasValue && page != inventoryPage.Value)
                {
                    continue;
                }

                var count = inventory.getItemCount(page);
                for (byte i = 0; i < count && targets.Count < amount; i++)
                {
                    var jar = inventory.getItem(page, i);
                    if (jar?.item != null && jar.item.id == itemId && matches(jar.item))
                    {
                        targets.Add((page, jar.x, jar.y, Snapshot(jar.item, cost, maxAmount)));
                    }
                }
            }

            var toAdd = new List<StoredItem>();
            var capacityReached = false;
            var operationId = targets.Count == 0 ? null : "vault_transfer:" + Guid.NewGuid().ToString("N");
            if (operationId != null)
            {
                Store.BeginTransferAudit(
                    operationId,
                    container,
                    user.Id,
                    "store",
                    targets.Select(target => target.snapshot).ToArray());
            }
            for (var sequence = 0; sequence < targets.Count; sequence++)
            {
                var (page, x, y, _) = targets[sequence];
                if (used + cost > max)
                {
                    capacityReached = true;
                    break;
                }

                // Remove from the backpack FIRST and only record it in the vault once the remove is
                // confirmed — so a stale/moved jar can never duplicate the item into both places.
                var index = inventory.getIndex(page, x, y);
                if (index == byte.MaxValue)
                {
                    continue;
                }

                var jar = inventory.getItem(page, index);
                if (jar?.item == null || jar.item.id != itemId || !matches(jar.item))
                {
                    continue;
                }

                var stored = Snapshot(jar.item, cost, maxAmount);

                // This durable per-item stage makes a crash between the game-inventory mutation
                // and SQLite commit explicit. removal_started is intentionally ambiguous; it is
                // never auto-replayed and tells an administrator exactly which candidate needs
                // manual inspection.
                Store.SetTransferAuditItemStage(operationId!, sequence, "removal_started");
                inventory.removeItem(page, index);
                Store.SetTransferAuditItemStage(operationId!, sequence, "inventory_removed");
                toAdd.Add(stored);
                used += cost;
            }

            if (toAdd.Count > 0)
            {
                try
                {
                    if (!Store.TryAddItems(
                        container,
                        displayName,
                        baseCapacity,
                        maximumCapacity,
                        toAdd,
                        operationId,
                        user.Id))
                    {
                        foreach (var item in toAdd) GiveBack(player, item);
                        TrySetTransferAuditState(operationId, "compensated");
                        return new StoreResult(0, true);
                    }
                    // Completion bookkeeping must never turn a successfully committed store into
                    // an inventory refund (and therefore a duplicate) if only the audit update fails.
                    TrySetTransferAuditState(operationId, "completed");
                }
                catch
                {
                    // A SQLite commit may have succeeded even when the caller observed an I/O
                    // exception. Only compensate when the durable audit proves the transaction did
                    // not commit; otherwise returning the item would duplicate it.
                    var state = Store.GetTransferAuditState(operationId!);
                    if (string.Equals(state, "database_committed", StringComparison.Ordinal))
                    {
                        TrySetTransferAuditState(operationId, "completed");
                        return new StoreResult(toAdd.Count, capacityReached);
                    }

                    if (string.Equals(state, "prepared", StringComparison.Ordinal))
                    {
                        foreach (var item in toAdd) GiveBack(player, item);
                        TrySetTransferAuditState(operationId, "compensated");
                    }
                    throw;
                }
            }
            else if (operationId != null)
            {
                TrySetTransferAuditState(operationId, "compensated");
            }

            return new StoreResult(toAdd.Count, capacityReached);
        }

        // ----- take -----

        /// <summary>Withdraws up to <paramref name="amount"/> copies of <paramref name="itemId"/> (any variant).</summary>
        public async Task<int> TakeAsync(UnturnedUser user, ushort itemId, int amount)
        {
            var personal = await GetPersonalVaultAsync(user);
            return await TakeMatchingAsync(user, VaultContainerRef.Player(user.Id), user.DisplayName, personal.BaseCapacity,
                x => x.ItemId == itemId, amount);
        }

        /// <summary>Withdraws up to <paramref name="count"/> copies matching amount and state; quality is optional for assets that do not expose durability.</summary>
        public async Task<int> TakeVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte? quality, string state, int count)
        {
            var personal = await GetPersonalVaultAsync(user);
            return await TakeMatchingAsync(user, VaultContainerRef.Player(user.Id), user.DisplayName, personal.BaseCapacity,
                x => x.ItemId == itemId && x.Amount == amount
                && (!quality.HasValue || x.Quality == quality.Value)
                && string.Equals(x.State, state, StringComparison.Ordinal), count);
        }

        public async Task<int> TakeTeamAsync(UnturnedUser user, ushort itemId, int amount)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return 0;
            return await TakeMatchingAsync(
                user,
                VaultContainerRef.Team(team.Value.Context.Key),
                team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(TeamSettings.BaseSlots),
                x => x.ItemId == itemId,
                amount);
        }

        public async Task<int> TakeTeamVariantAsync(
            UnturnedUser user,
            ushort itemId,
            byte amount,
            byte? quality,
            string state,
            int count)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return 0;
            return await TakeMatchingAsync(
                user,
                VaultContainerRef.Team(team.Value.Context.Key),
                team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(TeamSettings.BaseSlots),
                x => x.ItemId == itemId && x.Amount == amount
                    && (!quality.HasValue || x.Quality == quality.Value)
                    && string.Equals(x.State, state, StringComparison.Ordinal),
                count);
        }

        private async Task<int> TakeMatchingAsync(
            UnturnedUser user,
            VaultContainerRef container,
            string displayName,
            int baseCapacity,
            Func<StoredItem, bool> matches,
            int amount)
        {
            Store.GetOrCreateContainer(container, displayName, baseCapacity);
            await UniTask.SwitchToMainThread();
            TouchOwner(user);
            if (!await StillOwnsContainerAsync(user, container))
            {
                return 0;
            }
            var player = user.Player?.Player;
            if (player == null)
            {
                return 0;
            }

            var operationId = "vault_transfer:" + Guid.NewGuid().ToString("N");
            IReadOnlyList<StoredItem> entries;
            try
            {
                entries = Store.TakeMany(
                    container,
                    matches,
                    Math.Max(0, amount),
                    operationId,
                    user.Id);
            }
            catch
            {
                // Resolve an ambiguous commit from the audit written in the same transaction as the
                // delete. If its state cannot be proven, leave it interrupted for manual recovery.
                if (!string.Equals(
                        Store.GetTransferAuditState(operationId),
                        "database_committed",
                        StringComparison.Ordinal))
                {
                    throw;
                }
                entries = Store.GetTransferAuditItems(operationId);
            }
            var taken = 0;
            var restore = new List<StoredItem>();
            for (var sequence = 0; sequence < entries.Count; sequence++)
            {
                var entry = entries[sequence];
                TrySetTransferAuditItemStage(operationId, sequence, "delivery_started");
                if (GiveBack(player, entry))
                {
                    taken++;
                    TrySetTransferAuditItemStage(operationId, sequence, "inventory_given");
                }
                else
                {
                    restore.Add(entry);
                    TrySetTransferAuditItemStage(operationId, sequence, "restore_pending");
                }
            }

            if (restore.Count > 0) Store.AddItems(container, displayName, baseCapacity, restore);
            if (entries.Count > 0)
            {
                TrySetTransferAuditState(
                    operationId,
                    restore.Count == 0 ? "completed" : taken == 0 ? "compensated" : "completed_with_restore");
            }

            return taken;
        }

        public async Task<VaultMoveResult> MovePersonalToTeamAsync(
            UnturnedUser user, ushort itemId, int amount, byte? itemAmount = null, byte? quality = null, string? state = null)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return new VaultMoveResult(0, false);
            var personalBase = await GetPersonalBaseSlotsAsync(user);
            var teamSettings = TeamSettings;
            var currentTeam = await ResolveTeamAsync(user);
            if (currentTeam == null || !string.Equals(currentTeam.Key, team.Value.Context.Key, StringComparison.Ordinal))
                return new VaultMoveResult(0, false);
            return Store.MoveMany(
                VaultContainerRef.Player(user.Id), user.DisplayName, personalBase,
                VaultContainerRef.Team(team.Value.Context.Key), team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(teamSettings.BaseSlots), Math.Max(team.Value.Container.Capacity, NormalizeConfiguredCapacity(Math.Max(teamSettings.BaseSlots, teamSettings.MaxSlots))),
                item => MatchesVariant(item, itemId, itemAmount, quality, state), Math.Max(0, amount));
        }

        public async Task<VaultMoveResult> MoveTeamToPersonalAsync(
            UnturnedUser user, ushort itemId, int amount, byte? itemAmount = null, byte? quality = null, string? state = null)
        {
            var team = await GetTeamVaultAsync(user);
            if (team == null) return new VaultMoveResult(0, false);
            var personalBase = await GetPersonalBaseSlotsAsync(user);
            var personalPurchase = CurrentPersonalPurchaseSettings;
            var personal = await GetPersonalVaultAsync(user);
            var currentTeam = await ResolveTeamAsync(user);
            if (currentTeam == null || !string.Equals(currentTeam.Key, team.Value.Context.Key, StringComparison.Ordinal))
                return new VaultMoveResult(0, false);
            return Store.MoveMany(
                VaultContainerRef.Team(team.Value.Context.Key), team.Value.Context.DisplayName,
                NormalizeConfiguredCapacity(TeamSettings.BaseSlots),
                VaultContainerRef.Player(user.Id), user.DisplayName, personalBase,
                Math.Max(personal.Capacity, NormalizeConfiguredCapacity(Math.Max(personalBase, personalPurchase.MaxSlots))),
                item => MatchesVariant(item, itemId, itemAmount, quality, state), Math.Max(0, amount));
        }

        private static int NormalizeConfiguredCapacity(int value)
            => Math.Min(VaultCapacityLimits.Maximum, Math.Max(1, value));

        private static bool MatchesVariant(
            StoredItem item, ushort itemId, byte? amount, byte? quality, string? state)
            => item.ItemId == itemId
               && (!amount.HasValue || item.Amount == amount.Value)
               && (!quality.HasValue || item.Quality == quality.Value)
               && (state == null || string.Equals(item.State, state, StringComparison.Ordinal));


        public async Task<TeamVaultPurchaseResult> PurchasePersonalCapacityAsync(
            UnturnedUser user,
            long? expectedPurchaseVersion = null,
            int? expectedSlots = null,
            decimal? expectedPrice = null,
            int? expectedMaximum = null)
        {
            await m_PurchaseRecoveryGate.WaitAsync();
            try
            {
                await RecoverPendingTeamPurchasesCoreAsync(
                    default, BuyerRecoveryBatchSize, user.Id);
                return await PurchasePersonalCapacityCoreAsync(
                    user, expectedPurchaseVersion, expectedSlots, expectedPrice, expectedMaximum);
            }
            finally
            {
                m_PurchaseRecoveryGate.Release();
            }
        }

        private async Task<TeamVaultPurchaseResult> PurchasePersonalCapacityCoreAsync(
            UnturnedUser user,
            long? expectedPurchaseVersion,
            int? expectedSlots,
            decimal? expectedPrice,
            int? expectedMaximum)
        {
            var purchase = CurrentPersonalPurchaseSettings;
            var baseSlots = await GetPersonalBaseSlotsAsync(user);
            var maximum = NormalizeConfiguredCapacity(Math.Max(baseSlots, purchase.MaxSlots));
            if (!purchase.Enabled)
                return new TeamVaultPurchaseResult(TeamVaultPurchaseStatus.Disabled, 0, 0, purchase.Price);
            if (purchase.MaxSlots < 1 || purchase.MaxSlots > VaultCapacityLimits.Maximum
                || purchase.SlotsPerPurchase < 1 || purchase.SlotsPerPurchase > VaultCapacityLimits.Maximum
                || purchase.Price < 0m)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.InvalidConfiguration, 0, 0, purchase.Price);

            var container = VaultContainerRef.Player(user.Id);
            var snapshot = Store.GetOrCreateContainer(container, user.DisplayName, baseSlots);
            var current = snapshot.Capacity;
            if (current >= maximum)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.MaximumReached, 0, current, purchase.Price);

            var quote = QuotePersonalCapacity(current, baseSlots);
            var slots = quote.Slots;
            var price = quote.Price;
            if ((expectedSlots.HasValue && expectedSlots.Value != slots)
                || (expectedPrice.HasValue && expectedPrice.Value != price)
                || (expectedMaximum.HasValue && expectedMaximum.Value != maximum))
            {
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.StaleRequest, 0, current, price);
            }

            var standardEconomy = m_LifetimeScope.ResolveOptional<IEconomyProvider>();
            if (price > 0m && standardEconomy == null)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
            if (price > 0m)
            {
                try
                {
                    await standardEconomy!.GetBalanceAsync(user.Id, user.Type);
                }
                catch
                {
                    return new TeamVaultPurchaseResult(
                        TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
                }
            }
            var durableEconomy = standardEconomy as IIdempotentEconomyProvider;
            var durableMode = durableEconomy?.SupportsDurableOperations == true;
            if (price > 0m && !durableMode && decimal.Truncate(price) != price)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.InvalidConfiguration, 0, current, price);

            var operation = Store.CreatePendingTeamPurchase(
                container,
                user.DisplayName,
                baseSlots,
                user.Id,
                slots,
                price,
                expectedPurchaseVersion,
                durableMode ? "durable" : "experience");
            if (operation == null)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.StaleRequest, 0, current, price);

            IIdempotentEconomyProvider? economy = null;
            try
            {
                if (price > 0m)
                {
                    economy = durableMode
                        ? durableEconomy
                        : standardEconomy == null ? null : new VolatileEconomyAdapter(standardEconomy);
                    if (economy == null)
                    {
                        Store.MarkTeamPurchaseRefundPending(operation);
                        Store.MarkTeamPurchaseRefunded(operation);
                        return new TeamVaultPurchaseResult(
                            TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
                    }
                    await economy.ApplyOnceAsync(
                        operation, user.Id, user.Type, -price, "personal_vault_capacity");
                }

                if (!Store.MarkTeamPurchaseDebited(operation)
                    && !string.Equals(Store.GetTeamPurchaseState(operation), "debited", StringComparison.Ordinal))
                    throw new InvalidOperationException("Personal vault purchase could not enter the debited stage.");
                if (!Store.MarkTeamPurchaseReady(operation)
                    && !string.Equals(Store.GetTeamPurchaseState(operation), "ready", StringComparison.Ordinal))
                    throw new InvalidOperationException("Personal vault purchase could not enter the ready stage.");

                var newCapacity = Store.CompleteTeamPurchase(operation, maximum);
                if (newCapacity == 0)
                {
                    if (Store.MarkTeamPurchaseRefundPending(operation)
                        || string.Equals(Store.GetTeamPurchaseState(operation), "refund_pending", StringComparison.Ordinal))
                    {
                        await RefundTeamPurchaseAsync(
                            economy, operation, user.Id, user.Type, price,
                            "personal_vault_capacity_refund");
                    }
                    return new TeamVaultPurchaseResult(
                        TeamVaultPurchaseStatus.MaximumReached, 0, current, price);
                }

                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Purchased, slots, newCapacity, price);
            }
            catch (NotEnoughBalanceException ex)
            {
                if (Store.MarkTeamPurchaseRefundPending(operation)
                    || string.Equals(Store.GetTeamPurchaseState(operation), "refund_pending", StringComparison.Ordinal))
                    Store.MarkTeamPurchaseRefunded(operation);
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Failed, 0, current, price, ex.Message);
            }
            catch (Exception ex)
            {
                try
                {
                    var state = Store.GetTeamPurchaseState(operation);
                    if (string.Equals(state, "completed", StringComparison.Ordinal))
                    {
                        return new TeamVaultPurchaseResult(
                            TeamVaultPurchaseStatus.Purchased, slots,
                            Store.GetOrCreateContainer(container, user.DisplayName, baseSlots).Capacity,
                            price);
                    }
                    if (string.Equals(state, "ready", StringComparison.Ordinal))
                    {
                        var recovered = Store.CompleteTeamPurchase(operation, maximum);
                        if (recovered > 0)
                            return new TeamVaultPurchaseResult(
                                TeamVaultPurchaseStatus.Purchased, slots, recovered, price);
                    }
                    if (string.Equals(state, "debited", StringComparison.Ordinal)
                        && Store.MarkTeamPurchaseRefundPending(operation))
                    {
                        await RefundTeamPurchaseAsync(
                            economy, operation, user.Id, user.Type, price,
                            "personal_vault_capacity_refund");
                    }
                }
                catch
                {
                    // Leave the durable stage for periodic/admin recovery.
                }
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Failed, 0, current, price, ex.Message);
            }
        }

        public async Task<TeamVaultPurchaseResult> PurchaseTeamCapacityAsync(
            UnturnedUser user,
            long? expectedPurchaseVersion = null,
            int? expectedSlots = null,
            decimal? expectedPrice = null,
            int? expectedMaximum = null)
        {
            await m_PurchaseRecoveryGate.WaitAsync();
            try
            {
                // A reconnecting buyer may have a debited purchase staged by an earlier crash.
                // Reconcile it while this live team context is available before accepting another.
                await RecoverPendingTeamPurchasesCoreAsync(
                    default, BuyerRecoveryBatchSize, user.Id);
                return await PurchaseTeamCapacityCoreAsync(
                    user, expectedPurchaseVersion, expectedSlots, expectedPrice, expectedMaximum);
            }
            finally
            {
                m_PurchaseRecoveryGate.Release();
            }
        }

        private async Task<TeamVaultPurchaseResult> PurchaseTeamCapacityCoreAsync(
            UnturnedUser user,
            long? expectedPurchaseVersion,
            int? expectedSlots,
            decimal? expectedPrice,
            int? expectedMaximum)
        {
            var settings = TeamSettings;
            var purchase = settings.Purchase ?? new TeamVaultPurchaseSettings();
            if (!settings.Enabled)
                return new TeamVaultPurchaseResult(TeamVaultPurchaseStatus.Disabled, 0, 0, 0m);
            if (!purchase.Enabled)
                return new TeamVaultPurchaseResult(TeamVaultPurchaseStatus.Disabled, 0, 0, purchase.Price);
            if (settings.BaseSlots < 1 || settings.BaseSlots > VaultCapacityLimits.Maximum
                || settings.MaxSlots < settings.BaseSlots || settings.MaxSlots > VaultCapacityLimits.Maximum
                || purchase.SlotsPerPurchase < 1 || purchase.SlotsPerPurchase > VaultCapacityLimits.Maximum
                || purchase.Price < 0m)
            {
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.InvalidConfiguration, 0, 0, purchase.Price);
            }

            var team = await GetTeamVaultAsync(user);
            if (team == null)
                return new TeamVaultPurchaseResult(TeamVaultPurchaseStatus.NotInTeam, 0, 0, purchase.Price);

            var current = team.Value.Container.Capacity;
            if (current >= settings.MaxSlots)
            {
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.MaximumReached, 0, current, purchase.Price);
            }

            var quote = QuoteTeamCapacity(current);
            var slots = quote.Slots;
            var price = quote.Price;
            if ((expectedSlots.HasValue && expectedSlots.Value != slots)
                || (expectedPrice.HasValue && expectedPrice.Value != price)
                || (expectedMaximum.HasValue && expectedMaximum.Value != settings.MaxSlots))
            {
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.StaleRequest, 0, current, price);
            }
            var container = VaultContainerRef.Team(team.Value.Context.Key);
            var standardEconomy = m_LifetimeScope.ResolveOptional<IEconomyProvider>();
            if (price > 0m && standardEconomy == null)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
            if (price > 0m)
            {
                try
                {
                    await standardEconomy!.GetBalanceAsync(user.Id, user.Type);
                }
                catch
                {
                    return new TeamVaultPurchaseResult(
                        TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
                }
            }
            var durableEconomy = standardEconomy as IIdempotentEconomyProvider;
            var durableMode = durableEconomy?.SupportsDurableOperations == true;
            if (price > 0m && !durableMode && decimal.Truncate(price) != price)
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.InvalidConfiguration, 0, current, price);
            var operation = Store.CreatePendingTeamPurchase(
                container,
                team.Value.Context.DisplayName,
                settings.BaseSlots,
                user.Id,
                slots,
                price,
                expectedPurchaseVersion,
                durableMode ? "durable" : "experience");
            if (operation == null)
            {
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.StaleRequest, 0, current, price);
            }
            IIdempotentEconomyProvider? economy = null;
            try
            {
                if (price > 0m)
                {
                    economy = durableMode
                        ? durableEconomy
                        : standardEconomy == null ? null : new VolatileEconomyAdapter(standardEconomy);
                    if (economy == null)
                    {
                        Store.MarkTeamPurchaseRefundPending(operation);
                        Store.MarkTeamPurchaseRefunded(operation);
                        return new TeamVaultPurchaseResult(
                            TeamVaultPurchaseStatus.EconomyUnavailable, 0, current, price);
                    }

                    await economy.ApplyOnceAsync(
                        operation,
                        user.Id,
                        user.Type,
                        -price,
                        "team_vault_capacity");
                }
                if (!Store.MarkTeamPurchaseDebited(operation)
                    && !string.Equals(Store.GetTeamPurchaseState(operation), "debited", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Team vault purchase could not enter the debited stage.");
                }

                // The shared capacity belongs to the team resolved when the purchase started. A
                // player leaving/switching teams while Economy is being awaited must be refunded,
                // never allowed to complete a purchase for the old team.
                var currentTeam = await ResolveTeamAsync(user);
                if (currentTeam == null
                    || !string.Equals(currentTeam.Key, team.Value.Context.Key, StringComparison.Ordinal))
                {
                    if (Store.MarkTeamPurchaseRefundPending(operation)
                        || string.Equals(Store.GetTeamPurchaseState(operation), "refund_pending", StringComparison.Ordinal))
                    {
                        await RefundTeamPurchaseAsync(
                            economy, operation, user.Id, user.Type, price,
                            "team_vault_capacity_membership_refund");
                    }
                    return new TeamVaultPurchaseResult(
                        TeamVaultPurchaseStatus.NotInTeam, 0, current, price);
                }

                if (!Store.MarkTeamPurchaseReady(operation)
                    && !string.Equals(Store.GetTeamPurchaseState(operation), "ready", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Team vault purchase could not enter the ready stage.");
                }

                var newCapacity = Store.CompleteTeamPurchase(operation, settings.MaxSlots);
                if (newCapacity == 0)
                {
                    if (Store.MarkTeamPurchaseRefundPending(operation)
                        || string.Equals(Store.GetTeamPurchaseState(operation), "refund_pending", StringComparison.Ordinal))
                    {
                        await RefundTeamPurchaseAsync(
                            economy, operation, user.Id, user.Type, price,
                            "team_vault_capacity_refund");
                    }
                    return new TeamVaultPurchaseResult(
                        TeamVaultPurchaseStatus.MaximumReached, 0, current, price);
                }

                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Purchased, slots, newCapacity, price);
            }
            catch (NotEnoughBalanceException ex)
            {
                // The Economy call completed with a definitive pre-commit rejection. Unlike an
                // I/O or reload interruption, this is proof that no debit can appear later, so the
                // durable operation may be closed immediately instead of leaking a debiting row.
                if (Store.MarkTeamPurchaseRefundPending(operation)
                    || string.Equals(Store.GetTeamPurchaseState(operation), "refund_pending", StringComparison.Ordinal))
                {
                    Store.MarkTeamPurchaseRefunded(operation);
                }
                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Failed, 0, current, price, ex.Message);
            }
            catch (Exception ex)
            {
                try
                {
                    var state = Store.GetTeamPurchaseState(operation);
                    if (string.Equals(state, "completed", StringComparison.Ordinal))
                    {
                        return new TeamVaultPurchaseResult(
                            TeamVaultPurchaseStatus.Purchased, slots,
                            Store.GetOrCreateContainer(container, team.Value.Context.DisplayName, settings.BaseSlots).Capacity,
                            price);
                    }
                    if (string.Equals(state, "ready", StringComparison.Ordinal))
                    {
                        var recoveredCapacity = Store.CompleteTeamPurchase(operation, settings.MaxSlots);
                        if (recoveredCapacity > 0)
                        {
                            return new TeamVaultPurchaseResult(
                                TeamVaultPurchaseStatus.Purchased, slots, recoveredCapacity, price);
                        }
                        if (Store.MarkTeamPurchaseRefundPending(operation))
                        {
                            await RefundTeamPurchaseAsync(
                                economy, operation, user.Id, user.Type, price,
                                "team_vault_capacity_refund");
                        }
                    }
                    else if (string.Equals(state, "refund_pending", StringComparison.Ordinal))
                    {
                        await RefundTeamPurchaseAsync(
                            economy, operation, user.Id, user.Type, price,
                            "team_vault_capacity_recovery_refund");
                    }
                }
                catch
                {
                    // Keep the durable stage unchanged. A later recovery will continue from it.
                }

                return new TeamVaultPurchaseResult(
                    TeamVaultPurchaseStatus.Failed, 0, current, price, ex.Message);
            }
        }

        public async Task<TeamVaultPurchaseResolutionResult> ResolvePendingTeamPurchaseAsync(
            string operationId,
            string action,
            string note,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(operationId) || string.IsNullOrWhiteSpace(note))
                return new TeamVaultPurchaseResolutionResult(false, "Operation ID and an audit note are required.");

            await m_PurchaseRecoveryGate.WaitAsync(cancellationToken);
            try
            {
                var pending = Store.GetPendingTeamPurchase(operationId);
                if (pending == null)
                    return new TeamVaultPurchaseResolutionResult(false, "The purchase is no longer pending; reload the quarantine list.");

                const string actor = "web-admin";
                if (string.Equals(action, "confirm-refunded", StringComparison.Ordinal))
                {
                    var canConfirm = string.Equals(pending.State, "refund_pending", StringComparison.Ordinal)
                        || (!pending.IsDurable
                            && string.Equals(pending.State, "debiting", StringComparison.Ordinal));
                    if (!canConfirm)
                        return new TeamVaultPurchaseResolutionResult(false,
                            "Only refund_pending, or an experience debiting row after manual refund, may be confirmed.");
                    return Store.TryConfirmTeamPurchaseRefunded(operationId, pending.State, actor, note)
                        ? new TeamVaultPurchaseResolutionResult(true, "Refund was manually confirmed and the quarantine lock was released.")
                        : new TeamVaultPurchaseResolutionResult(false, "Purchase state changed concurrently; reload and review it again.");
                }

                if (string.Equals(action, "abort-unpaid", StringComparison.Ordinal))
                {
                    if (!string.Equals(pending.State, "pending", StringComparison.Ordinal)
                        && !string.Equals(pending.State, "debiting", StringComparison.Ordinal))
                        return new TeamVaultPurchaseResolutionResult(false, "Only pending/debiting may be aborted as unpaid.");
                    if (pending.Price <= 0m)
                    {
                        return Store.TryAbortTeamPurchase(operationId, pending.State, actor, note)
                            ? new TeamVaultPurchaseResolutionResult(true, "Free purchase was aborted and the quarantine lock was released.")
                            : new TeamVaultPurchaseResolutionResult(false, "Purchase state changed concurrently; reload and review it again.");
                    }

                    var economy = m_LifetimeScope.ResolveOptional<IIdempotentEconomyProvider>();
                    if (!pending.IsDurable || economy?.SupportsDurableOperations != true)
                        return new TeamVaultPurchaseResolutionResult(false,
                            "This backend cannot safely synchronize a concurrent debit. Refund it manually after moving it to refund_pending.");
                    try
                    {
                        // Calling the original idempotent operation is the only reliable way to join
                        // a debit that another reload-era instance may still be executing. Once it
                        // returns, either the debit is durable or NotEnoughBalance proves rejection.
                        await AwaitProviderAsync(
                            economy.ApplyOnceAsync(operationId, pending.BuyerSteamId,
                                OpenMod.Core.Users.KnownActorTypes.Player, -pending.Price,
                                "vault_capacity_admin_cancel_sync"),
                            cancellationToken);
                    }
                    catch (NotEnoughBalanceException)
                    {
                        return Store.TryAbortTeamPurchase(operationId, pending.State, actor, note)
                            ? new TeamVaultPurchaseResolutionResult(true, "Rejected unpaid purchase was aborted.")
                            : new TeamVaultPurchaseResolutionResult(false, "Purchase state changed concurrently; reload and review it again.");
                    }

                    if (!Store.MarkTeamPurchaseRefundPending(operationId)
                        && !string.Equals(Store.GetTeamPurchaseState(operationId), "refund_pending", StringComparison.Ordinal))
                        return new TeamVaultPurchaseResolutionResult(false,
                            "Purchase completed or changed concurrently before cancellation; reload and review it.");
                    await AwaitProviderAsync(
                        economy.ApplyOnceAsync(operationId + ":refund", pending.BuyerSteamId,
                            OpenMod.Core.Users.KnownActorTypes.Player, pending.Price,
                            "vault_capacity_admin_cancel_refund"),
                        cancellationToken);
                    return Store.TryResolveCancelledTeamPurchaseRefund(operationId, actor, note)
                        ? new TeamVaultPurchaseResolutionResult(true, "Purchase was safely synchronized, refunded, and closed.")
                        : new TeamVaultPurchaseResolutionResult(false,
                            "Refund completed, but another resolver closed the quarantine row first; reload the audit.");
                }

                if (string.Equals(action, "retry-refund", StringComparison.Ordinal))
                {
                    if (!string.Equals(pending.State, "refund_pending", StringComparison.Ordinal))
                        return new TeamVaultPurchaseResolutionResult(false, "Only refund_pending may retry a durable refund.");
                    if (!pending.IsDurable)
                        return new TeamVaultPurchaseResolutionResult(false,
                            "Experience has no durable ledger; refund it manually, then use confirm-refunded.");
                    var economy = m_LifetimeScope.ResolveOptional<IIdempotentEconomyProvider>();
                    if (economy?.SupportsDurableOperations != true)
                        return new TeamVaultPurchaseResolutionResult(false, "Durable Economy is unavailable.");
                    if (pending.Price > 0m)
                    {
                        var refund = await AwaitProviderAsync(
                            economy.GetAppliedBalanceAsync(operationId + ":refund", pending.BuyerSteamId,
                                OpenMod.Core.Users.KnownActorTypes.Player, pending.Price), cancellationToken);
                        if (!refund.HasValue)
                        {
                            var debit = await AwaitProviderAsync(
                                economy.GetAppliedBalanceAsync(operationId, pending.BuyerSteamId,
                                    OpenMod.Core.Users.KnownActorTypes.Player, -pending.Price), cancellationToken);
                            if (debit.HasValue)
                            {
                                await AwaitProviderAsync(
                                    economy.ApplyOnceAsync(operationId + ":refund", pending.BuyerSteamId,
                                        OpenMod.Core.Users.KnownActorTypes.Player, pending.Price, "team_vault_capacity_admin_refund"),
                                    cancellationToken);
                            }
                        }
                    }
                    return Store.TryResolveRetriedTeamPurchaseRefund(operationId, actor, note)
                        ? new TeamVaultPurchaseResolutionResult(true, "Durable refund was reconciled and the quarantine lock was released.")
                        : new TeamVaultPurchaseResolutionResult(false, "Purchase state changed concurrently; reload and review it again.");
                }

                return new TeamVaultPurchaseResolutionResult(false, "Unknown purchase resolution action.");
            }
            catch (TimeoutException ex)
            {
                return new TeamVaultPurchaseResolutionResult(false, ex.Message);
            }
            finally
            {
                m_PurchaseRecoveryGate.Release();
            }
        }

        public async Task<int> RecoverPendingTeamPurchasesAsync(CancellationToken cancellationToken = default)
        {
            await m_PurchaseRecoveryGate.WaitAsync(cancellationToken);
            try
            {
                return await RecoverPendingTeamPurchasesCoreAsync(
                    cancellationToken, RecoveryBatchSize);
            }
            finally
            {
                m_PurchaseRecoveryGate.Release();
            }
        }

        private async Task<int> RecoverPendingTeamPurchasesCoreAsync(
            CancellationToken cancellationToken = default,
            int batchSize = RecoveryBatchSize,
            string? buyerSteamId = null)
        {
            var durableEconomy = m_LifetimeScope.ResolveOptional<IIdempotentEconomyProvider>();
            var teamProvider = m_LifetimeScope.ResolveOptional<ITeamContextProvider>();
            var recovered = 0;
            foreach (var pending in Store.GetPendingTeamPurchases(batchSize, buyerSteamId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var state = pending.State;
                    var actorType = OpenMod.Core.Users.KnownActorTypes.Player;
                    if (string.Equals(state, "refund_pending", StringComparison.Ordinal))
                    {
                        // The experience backend has no durable operation ledger. If the process
                        // ended after debit but before refund, guessing here could mint or destroy XP;
                        // keep the row visible in the recovery quarantine for manual resolution.
                        if (!pending.IsDurable) continue;
                        await RefundTeamPurchaseAsync(
                            durableEconomy, pending.OperationId, pending.BuyerSteamId, actorType,
                            pending.Price, "team_vault_capacity_recovery_refund");
                        continue;
                    }

                    if (string.Equals(state, "pending", StringComparison.Ordinal))
                    {
                        // Legacy pre-2.5 records must first acquire the same durable stage as new
                        // purchases. Looking at the ledger before this CAS reopens the reload race:
                        // another instance may be inside ApplyOnceAsync without a visible marker.
                        if (!Store.MarkTeamPurchaseDebiting(pending.OperationId)
                            && !string.Equals(Store.GetTeamPurchaseState(pending.OperationId), "debiting", StringComparison.Ordinal))
                            continue;
                        state = "debiting";
                    }

                    if (string.Equals(state, "debiting", StringComparison.Ordinal))
                    {
                        if (pending.Price > 0m)
                        {
                            if (!pending.IsDurable || durableEconomy == null
                                || !durableEconomy.SupportsDurableOperations) continue;
                            var refundExists = (await AwaitProviderAsync(
                                durableEconomy.GetAppliedBalanceAsync(
                                    pending.OperationId + ":refund", pending.BuyerSteamId, actorType, pending.Price),
                                cancellationToken)).HasValue;
                            if (refundExists)
                            {
                                if (Store.MarkTeamPurchaseRefundPending(pending.OperationId))
                                    Store.MarkTeamPurchaseRefunded(pending.OperationId);
                                continue;
                            }
                            var debitExists = (await AwaitProviderAsync(
                                durableEconomy.GetAppliedBalanceAsync(
                                    pending.OperationId, pending.BuyerSteamId, actorType, -pending.Price),
                                cancellationToken)).HasValue;
                            // Another plugin instance may currently be inside ApplyOnceAsync. An
                            // absent marker is therefore ambiguous and must remain staged, never
                            // be interpreted as a failed debit or automatically refunded.
                            if (!debitExists) continue;
                        }
                        if (!Store.MarkTeamPurchaseDebited(pending.OperationId)
                            && !string.Equals(Store.GetTeamPurchaseState(pending.OperationId), "debited", StringComparison.Ordinal))
                            continue;
                        state = "debited";
                    }

                    if (string.Equals(state, "debited", StringComparison.Ordinal))
                    {
                        if (pending.Container.Kind == VaultOwnerKind.Team)
                        {
                            if (teamProvider == null) continue;
                            var lookup = await AwaitProviderAsync(
                                teamProvider.GetCurrentTeamAsync(pending.BuyerSteamId, actorType),
                                cancellationToken);
                            if (lookup.Status == TeamLookupStatus.Offline
                                || lookup.Status == TeamLookupStatus.Unavailable) continue;
                            var currentTeam = lookup.Context;
                            if (lookup.Status == TeamLookupStatus.OnlineNoTeam || currentTeam == null
                                || !string.Equals(currentTeam.Key, pending.Container.Key, StringComparison.Ordinal))
                            {
                                if (Store.MarkTeamPurchaseRefundPending(pending.OperationId)
                                    && pending.IsDurable)
                                {
                                    await RefundTeamPurchaseAsync(
                                        durableEconomy, pending.OperationId, pending.BuyerSteamId, actorType,
                                        pending.Price, "team_vault_capacity_membership_refund");
                                }
                                continue;
                            }
                        }
                        else if (!string.Equals(pending.Container.Key, pending.BuyerSteamId, StringComparison.Ordinal))
                            continue;

                        if (!Store.MarkTeamPurchaseReady(pending.OperationId)
                            && !string.Equals(Store.GetTeamPurchaseState(pending.OperationId), "ready", StringComparison.Ordinal))
                            continue;
                        state = "ready";
                    }

                    if (string.Equals(state, "ready", StringComparison.Ordinal))
                    {
                        var snapshot = Store.GetContainer(pending.Container);
                        var maximum = pending.Container.Kind == VaultOwnerKind.Team
                            ? NormalizeConfiguredCapacity(Math.Max(TeamSettings.BaseSlots, TeamSettings.MaxSlots))
                            : NormalizeConfiguredCapacity(Math.Max(snapshot?.BaseCapacity ?? BaseMaxSlots,
                                CurrentPersonalPurchaseSettings.MaxSlots));
                        if (Store.CompleteTeamPurchase(pending.OperationId, maximum) > 0)
                        {
                            recovered++;
                        }
                        else if (Store.MarkTeamPurchaseRefundPending(pending.OperationId))
                        {
                            if (pending.IsDurable)
                            {
                                await RefundTeamPurchaseAsync(
                                    durableEconomy, pending.OperationId, pending.BuyerSteamId, actorType,
                                    pending.Price, "team_vault_capacity_recovery_refund");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_Logger.LogWarning(ex,
                        "Vault capacity-purchase recovery failed for operation {OperationId}; the durable stage remains quarantined.",
                        pending.OperationId);
                }
            }
            return recovered;
        }

        private static async Task<T> AwaitProviderAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            var delay = Task.Delay(s_ProviderTimeout, cancellationToken);
            var completed = await Task.WhenAny(operation, delay).ConfigureAwait(false);
            if (completed != operation)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = operation.ContinueWith(
                    task => { var ignored = task.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new TimeoutException("Vault recovery provider call exceeded the 3 second deadline.");
            }
            return await operation.ConfigureAwait(false);
        }

        private async Task RefundTeamPurchaseAsync(
            IIdempotentEconomyProvider? economy,
            string operationId,
            string buyerId,
            string buyerType,
            decimal price,
            string reason,
            CancellationToken cancellationToken = default)
        {
            if (price > 0m)
            {
                if (economy == null || !economy.SupportsDurableOperations)
                    throw new InvalidOperationException("Durable Economy is unavailable for a pending refund.");
                var refundExists = (await AwaitProviderAsync(
                    economy.GetAppliedBalanceAsync(operationId + ":refund", buyerId, buyerType, price),
                    cancellationToken)).HasValue;
                if (!refundExists)
                {
                    var debitExists = (await AwaitProviderAsync(
                        economy.GetAppliedBalanceAsync(operationId, buyerId, buyerType, -price),
                        cancellationToken)).HasValue;
                    if (debitExists)
                    {
                        await AwaitProviderAsync(
                            economy.ApplyOnceAsync(
                                operationId + ":refund", buyerId, buyerType, price, reason),
                            cancellationToken);
                    }
                }
            }
            Store.MarkTeamPurchaseRefunded(operationId);
        }

        public (int Slots, decimal Price) QuotePersonalCapacity(int currentCapacity, int baseSlots)
        {
            var purchase = CurrentPersonalPurchaseSettings;
            var maximum = NormalizeConfiguredCapacity(Math.Max(baseSlots, purchase.MaxSlots));
            if (!purchase.Enabled || purchase.SlotsPerPurchase < 1
                || purchase.Price < 0m || currentCapacity >= maximum)
            {
                return (0, 0m);
            }

            var slots = Math.Min(purchase.SlotsPerPurchase, maximum - currentCapacity);
            var price = slots == purchase.SlotsPerPurchase
                ? purchase.Price
                : decimal.Round(
                    purchase.Price * slots / purchase.SlotsPerPurchase,
                    2,
                    MidpointRounding.AwayFromZero);
            return (slots, price);
        }

        public (int Slots, decimal Price) QuoteTeamCapacity(int currentCapacity)
        {
            var settings = TeamSettings;
            var purchase = settings.Purchase ?? new TeamVaultPurchaseSettings();
            if (!settings.Enabled || !purchase.Enabled || purchase.SlotsPerPurchase < 1
                || purchase.Price < 0m || currentCapacity >= settings.MaxSlots)
            {
                return (0, 0m);
            }

            var slots = Math.Min(purchase.SlotsPerPurchase, settings.MaxSlots - currentCapacity);
            var price = slots == purchase.SlotsPerPurchase
                ? purchase.Price
                : decimal.Round(
                    purchase.Price * slots / purchase.SlotsPerPurchase,
                    2,
                    MidpointRounding.AwayFromZero);
            return (slots, price);
        }

        private sealed class VolatileEconomyAdapter : IIdempotentEconomyProvider
        {
            private readonly IEconomyProvider m_Economy;
            private readonly Dictionary<string, decimal> m_Applied = new Dictionary<string, decimal>(StringComparer.Ordinal);

            public VolatileEconomyAdapter(IEconomyProvider economy) => m_Economy = economy;
            public bool SupportsDurableOperations => true;

            public async Task<decimal> ApplyOnceAsync(
                string operationId, string ownerId, string ownerType, decimal changeAmount, string reason)
            {
                if (m_Applied.TryGetValue(operationId, out var balance)) return balance;
                balance = await m_Economy.UpdateBalanceAsync(ownerId, ownerType, changeAmount, reason);
                m_Applied[operationId] = balance;
                return balance;
            }

            public Task<decimal?> GetAppliedBalanceAsync(
                string operationId, string ownerId, string ownerType, decimal changeAmount)
                => Task.FromResult(m_Applied.TryGetValue(operationId, out var balance)
                    ? (decimal?)balance
                    : null);
        }

        private async Task<TeamContext?> ResolveTeamAsync(UnturnedUser user)
        {
            var provider = m_LifetimeScope.ResolveOptional<ITeamContextProvider>();
            if (provider == null) return null;
            var result = await provider.GetCurrentTeamAsync(user.Id, user.Type);
            return result.Status == TeamLookupStatus.InTeam ? result.Context : null;
        }

        private async Task<bool> StillOwnsContainerAsync(UnturnedUser user, VaultContainerRef container)
        {
            if (container.Kind != VaultOwnerKind.Team)
            {
                return string.Equals(container.Key, user.Id, StringComparison.Ordinal);
            }

            var current = await ResolveTeamAsync(user);
            return current != null && string.Equals(current.Key, container.Key, StringComparison.Ordinal);
        }

        private void TrySetTransferAuditState(string? operationId, string state)
        {
            if (string.IsNullOrEmpty(operationId)) return;
            try
            {
                Store.SetTransferAuditState(operationId!, state);
            }
            catch
            {
                // The preceding inventory/database mutation is authoritative. Leaving the audit in
                // an interrupted state is safer than compensating an already committed operation.
            }
        }

        private void TrySetTransferAuditItemStage(string operationId, int sequence, string stage)
        {
            try
            {
                Store.SetTransferAuditItemStage(operationId, sequence, stage);
            }
            catch
            {
                // The audit header remains interrupted. Never replay or compensate solely because
                // secondary per-item bookkeeping could not be updated.
            }
        }

        private static StoredItem Snapshot(Item item, int slotCost, byte maxAmount)
            => new StoredItem
            {
                ItemId = item.id,
                Amount = item.amount,
                Quality = item.quality,
                State = item.state != null && item.state.Length > 0
                    ? Convert.ToBase64String(item.state)
                    : string.Empty,
                SlotCost = Math.Max(1, slotCost),
                MaxAmount = maxAmount
            };

        private static bool BytesEqual(byte[]? a, byte[]? b)
        {
            var x = a ?? Array.Empty<byte>();
            var y = b ?? Array.Empty<byte>();
            if (x.Length != y.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Reconstructs the stored item into the player's inventory (dropping at their feet if
        /// it doesn't fit). Returns false if the persisted state was corrupt and could not be decoded.</summary>
        private static bool GiveBack(Player player, StoredItem stored)
        {
            byte[] state;
            try
            {
                state = string.IsNullOrEmpty(stored.State) ? Array.Empty<byte>() : Convert.FromBase64String(stored.State);
            }
            catch (FormatException)
            {
                return false;
            }

            var item = new Item(stored.ItemId, stored.Amount, stored.Quality, state);
            if (!player.inventory.tryAddItem(item, true))
            {
                ItemManager.dropItem(item, player.transform.position, true, true, false);
            }

            return true;
        }

    }
}
