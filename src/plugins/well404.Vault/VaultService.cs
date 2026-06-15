using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Permissions;
using OpenMod.API.Persistence;
using OpenMod.Unturned.Users;
using SDG.Unturned;
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
    /// A distinct item variant (a group of copies with identical id + amount + quality + state) and
    /// how many copies of it there are. <see cref="State"/> is the base64 of the raw item state.
    /// </summary>
    public readonly struct ItemVariant
    {
        public ItemVariant(ushort itemId, byte amount, byte quality, string state, int count, int slotCost, byte maxAmount)
        {
            ItemId = itemId;
            Amount = amount;
            Quality = quality;
            State = state;
            Count = count;
            SlotCost = slotCost;
            MaxAmount = maxAmount;
        }

        public ushort ItemId { get; }
        public byte Amount { get; }
        public byte Quality { get; }
        public string State { get; }
        public int Count { get; }
        public int SlotCost { get; }

        /// <summary>Full stack/magazine capacity (e.g. 8 for an 8-round shell box); 0 if unknown.</summary>
        public byte MaxAmount { get; }
    }

    /// <summary>
    /// The per-player vault: moves items between a player's backpack and persistent storage with
    /// full item-state fidelity (a stored item keeps its quality and raw state bytes — attachments,
    /// the rounds inside a magazine/ammo box, etc.). Capacity is counted in grid cells: each item
    /// costs its asset's footprint (size_x × size_y), never its internal stack/ammo count. Capacity
    /// is per-player: a config base, raised by any permission tier the player holds, or set
    /// individually via a per-player override. Registered as a plugin-scoped singleton.
    ///
    /// Threading: SDG inventory/asset access runs on the Unity main thread (callers switch first).
    /// The in-memory model (<c>m_Data</c>) is touched from the main thread (store/take) AND from the
    /// web/command threads (render/list/overrides), so every read and write of it is guarded by
    /// <c>m_Lock</c>, and persistence is serialized through <c>m_SaveLock</c> over a cloned snapshot.
    /// </summary>
    public class VaultService
    {
        private const string DataKey = "vault";

        private readonly IConfiguration m_Configuration;
        private readonly IPermissionChecker m_PermissionChecker;
        private readonly object m_Lock = new object();
        private readonly SemaphoreSlim m_SaveLock = new SemaphoreSlim(1, 1);
        private IDataStore? m_DataStore;
        private VaultData m_Data = new VaultData();

        public VaultService(IConfiguration configuration, IPermissionChecker permissionChecker)
        {
            m_Configuration = configuration;
            m_PermissionChecker = permissionChecker;
        }

        private VaultSettings Settings => m_Configuration.Get<VaultSettings>() ?? new VaultSettings();

        /// <summary>The configured base capacity (before tiers / per-player overrides), at least 1.</summary>
        public int BaseMaxSlots => Math.Max(1, Settings.MaxSlots);

        /// <summary>Loads persisted data once. Called by the plugin on load (DataStore is plugin-owned).</summary>
        public async Task InitializeAsync(IDataStore dataStore)
        {
            m_DataStore = dataStore;
            VaultData loaded;
            if (await dataStore.ExistsAsync(DataKey))
            {
                loaded = await dataStore.LoadAsync<VaultData>(DataKey) ?? new VaultData();
            }
            else
            {
                loaded = new VaultData();
            }

            // Backfill the stack capacity for items stored before it was recorded, so the web view can
            // show a fill ratio (e.g. 6/8). Only stacked items (ammo/magazines) have one. Runs on load
            // before any concurrency, but we still publish under the lock for visibility.
            var changed = false;
            foreach (var list in loaded.Players.Values)
            {
                foreach (var item in list)
                {
                    if (item.MaxAmount == 0 && item.Amount > 1)
                    {
                        item.MaxAmount = MaxAmountOf(item.ItemId);
                        if (item.MaxAmount != 0)
                        {
                            changed = true;
                        }
                    }
                }
            }

            lock (m_Lock)
            {
                m_Data = loaded;
            }

            if (changed)
            {
                await SaveAsync();
            }
        }

        // ----- capacity -----

        /// <summary>The effective capacity for an online player: a per-player override, else the largest of
        /// the config base and every permission tier the player holds. Never below 1.</summary>
        public async Task<int> GetMaxSlotsAsync(UnturnedUser user)
        {
            int overridden;
            bool hasOverride;
            lock (m_Lock)
            {
                hasOverride = m_Data.Overrides.TryGetValue(user.Id, out overridden);
            }

            if (hasOverride)
            {
                return Math.Max(1, overridden);
            }

            var settings = Settings;
            var best = Math.Max(1, settings.MaxSlots);
            foreach (var tier in settings.Tiers)
            {
                if (await m_PermissionChecker.CheckPermissionAsync(user, tier.Key).ConfigureAwait(false) == PermissionGrantResult.Grant)
                {
                    best = Math.Max(best, tier.Value);
                }
            }

            return best;
        }

        /// <summary>Capacity known without the online user: a per-player override, else the config base. Never below 1.</summary>
        public int OverrideOrBase(string steamId)
        {
            lock (m_Lock)
            {
                if (m_Data.Overrides.TryGetValue(steamId, out var overridden))
                {
                    return Math.Max(1, overridden);
                }
            }

            return Math.Max(1, Settings.MaxSlots);
        }

        public IReadOnlyDictionary<string, int> Overrides
        {
            get { lock (m_Lock) { return new Dictionary<string, int>(m_Data.Overrides); } }
        }

        public async Task SetOverrideAsync(string steamId, int slots)
        {
            lock (m_Lock)
            {
                m_Data.Overrides[steamId] = Math.Max(1, slots);
            }

            await SaveAsync();
        }

        public async Task<bool> ClearOverrideAsync(string steamId)
        {
            bool removed;
            lock (m_Lock)
            {
                removed = m_Data.Overrides.Remove(steamId);
            }

            if (removed)
            {
                await SaveAsync();
            }

            return removed;
        }

        // ----- queries -----

        /// <summary>A snapshot of a player's stored items.</summary>
        public IReadOnlyList<StoredItem> Get(string steamId)
        {
            lock (m_Lock)
            {
                return m_Data.Players.TryGetValue(steamId, out var list) ? list.ToList() : new List<StoredItem>();
            }
        }

        /// <summary>Grid cells currently used by a player's vault.</summary>
        public int UsedSlots(string steamId)
        {
            lock (m_Lock)
            {
                return m_Data.Players.TryGetValue(steamId, out var list) ? list.Sum(x => x.SlotCost) : 0;
            }
        }

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
        {
            lock (m_Lock)
            {
                var result = new List<ItemVariant>();
                if (!m_Data.Players.TryGetValue(steamId, out var list))
                {
                    return result;
                }

                foreach (var group in list
                    .GroupBy(x => (x.ItemId, x.Amount, x.Quality, x.State))
                    .OrderBy(g => g.Key.ItemId))
                {
                    var first = group.First();
                    result.Add(new ItemVariant(first.ItemId, first.Amount, first.Quality, first.State, group.Count(), first.SlotCost, first.MaxAmount));
                }

                return result;
            }
        }

        /// <summary>The player's carried items collapsed into distinct variants.</summary>
        public async Task<IReadOnlyList<ItemVariant>> BackpackVariantsAsync(UnturnedUser user)
        {
            await UniTask.SwitchToMainThread();
            var player = user.Player?.Player;
            if (player == null)
            {
                return Array.Empty<ItemVariant>();
            }

            var inventory = player.inventory;
            var map = new Dictionary<(ushort, byte, byte, string), int>();
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
                    var key = (item.id, item.amount, item.quality, state);
                    map.TryGetValue(key, out var existing);
                    map[key] = existing + 1;
                }
            }

            return map.Select(kv => new ItemVariant(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Key.Item4, kv.Value, SlotCostOf(kv.Key.Item1), MaxAmountOf(kv.Key.Item1))).ToList();
        }

        // ----- store -----

        /// <summary>Stores up to <paramref name="amount"/> copies of <paramref name="itemId"/> (any variant).</summary>
        public Task<StoreResult> StoreAsync(UnturnedUser user, ushort itemId, int amount)
            => StoreMatchingAsync(user, itemId, _ => true, amount);

        /// <summary>Stores up to <paramref name="count"/> copies matching an exact variant.</summary>
        public Task<StoreResult> StoreVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte quality, byte[] state, int count)
            => StoreMatchingAsync(user, itemId, it => it.amount == amount && it.quality == quality && BytesEqual(it.state, state), count);

        private async Task<StoreResult> StoreMatchingAsync(UnturnedUser user, ushort itemId, Func<Item, bool> matches, int amount)
        {
            // Resolve capacity FIRST: GetMaxSlotsAsync may await a permission backend and resume off the
            // main thread, so we must not touch SDG inventory/assets after it. Switch to the main thread
            // afterwards and keep everything below await-free so it all runs on the main thread.
            var max = await GetMaxSlotsAsync(user);
            var used = UsedSlots(user.Id);

            await UniTask.SwitchToMainThread();
            var player = user.Player?.Player;
            if (player == null)
            {
                return new StoreResult(0, false);
            }

            var inventory = player.inventory;
            var cost = SlotCostOf(itemId);
            var maxAmount = MaxAmountOf(itemId);

            // Gather candidate jars (worn containers; held weapon slots are intentionally excluded).
            var targets = new List<(byte page, byte x, byte y)>();
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE && targets.Count < amount; page++)
            {
                var count = inventory.getItemCount(page);
                for (byte i = 0; i < count && targets.Count < amount; i++)
                {
                    var jar = inventory.getItem(page, i);
                    if (jar?.item != null && jar.item.id == itemId && matches(jar.item))
                    {
                        targets.Add((page, jar.x, jar.y));
                    }
                }
            }

            var toAdd = new List<StoredItem>();
            var capacityReached = false;
            foreach (var (page, x, y) in targets)
            {
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

                var item = jar.item;
                var stored = new StoredItem
                {
                    ItemId = item.id,
                    Amount = item.amount,
                    Quality = item.quality,
                    State = item.state != null && item.state.Length > 0 ? Convert.ToBase64String(item.state) : string.Empty,
                    SlotCost = cost,
                    MaxAmount = maxAmount
                };

                inventory.removeItem(page, index);
                toAdd.Add(stored);
                used += cost;
            }

            if (toAdd.Count > 0)
            {
                lock (m_Lock)
                {
                    ListForLocked(user.Id).AddRange(toAdd);
                }

                await SaveAsync();
            }

            return new StoreResult(toAdd.Count, capacityReached);
        }

        // ----- take -----

        /// <summary>Withdraws up to <paramref name="amount"/> copies of <paramref name="itemId"/> (any variant).</summary>
        public Task<int> TakeAsync(UnturnedUser user, ushort itemId, int amount)
            => TakeMatchingAsync(user, x => x.ItemId == itemId, amount);

        /// <summary>Withdraws up to <paramref name="count"/> copies matching an exact variant.</summary>
        public Task<int> TakeVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte quality, string state, int count)
            => TakeMatchingAsync(user, x => x.ItemId == itemId && x.Amount == amount && x.Quality == quality
                && string.Equals(x.State, state, StringComparison.Ordinal), count);

        private async Task<int> TakeMatchingAsync(UnturnedUser user, Func<StoredItem, bool> matches, int amount)
        {
            await UniTask.SwitchToMainThread();
            var player = user.Player?.Player;
            if (player == null)
            {
                return 0;
            }

            var taken = 0;
            var mutated = false;
            for (var n = 0; n < amount; n++)
            {
                StoredItem? entry = null;
                lock (m_Lock)
                {
                    if (m_Data.Players.TryGetValue(user.Id, out var list))
                    {
                        var idx = list.FindIndex(x => matches(x));
                        if (idx >= 0)
                        {
                            entry = list[idx];
                        }
                    }
                }

                if (entry == null)
                {
                    break;
                }

                var restored = GiveBack(player, entry);

                // Always drop the entry from the vault (even if its state was corrupt and couldn't be
                // restored) so the loop can't spin on it; only count items actually returned.
                lock (m_Lock)
                {
                    if (m_Data.Players.TryGetValue(user.Id, out var list))
                    {
                        list.Remove(entry);
                    }
                }

                mutated = true;
                if (restored)
                {
                    taken++;
                }
            }

            if (mutated)
            {
                await SaveAsync();
            }

            return taken;
        }

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

        // Caller must hold m_Lock.
        private List<StoredItem> ListForLocked(string steamId)
        {
            if (!m_Data.Players.TryGetValue(steamId, out var list))
            {
                list = new List<StoredItem>();
                m_Data.Players[steamId] = list;
            }

            return list;
        }

        private async Task SaveAsync()
        {
            if (m_DataStore == null)
            {
                return;
            }

            // Serialize a stable clone so the data store never walks a graph another thread is mutating,
            // and serialize concurrent saves so two writers can't interleave the file write.
            VaultData snapshot;
            lock (m_Lock)
            {
                snapshot = CloneData(m_Data);
            }

            await m_SaveLock.WaitAsync();
            try
            {
                await m_DataStore.SaveAsync(DataKey, snapshot);
            }
            finally
            {
                m_SaveLock.Release();
            }
        }

        private static VaultData CloneData(VaultData data)
        {
            var copy = new VaultData();
            foreach (var pair in data.Players)
            {
                copy.Players[pair.Key] = pair.Value.Select(CloneItem).ToList();
            }

            foreach (var pair in data.Overrides)
            {
                copy.Overrides[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static StoredItem CloneItem(StoredItem s) => new StoredItem
        {
            ItemId = s.ItemId,
            Amount = s.Amount,
            Quality = s.Quality,
            State = s.State,
            SlotCost = s.SlotCost,
            MaxAmount = s.MaxAmount
        };
    }
}
