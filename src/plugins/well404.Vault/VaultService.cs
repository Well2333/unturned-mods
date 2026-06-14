using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    /// <summary>One distinct item id present in a player's backpack, with how many copies.</summary>
    public readonly struct BackpackEntry
    {
        public BackpackEntry(ushort itemId, int count) { ItemId = itemId; Count = count; }
        public ushort ItemId { get; }
        public int Count { get; }
    }

    /// <summary>Outcome of a store request: how many were stored, and whether capacity ran out.</summary>
    public readonly struct StoreResult
    {
        public StoreResult(int stored, bool capacityReached) { Stored = stored; CapacityReached = capacityReached; }
        public int Stored { get; }
        public bool CapacityReached { get; }
    }

    /// <summary>
    /// The per-player vault: moves items between a player's backpack and persistent storage with
    /// full item-state fidelity (a stored item keeps its quality and raw state bytes — attachments,
    /// the rounds inside a magazine/ammo box, etc.). Capacity is counted in grid cells: each item
    /// costs its asset's footprint (size_x × size_y), never its internal stack/ammo count. Capacity
    /// is per-player: a config base, raised by any permission tier the player holds, or set
    /// individually via a per-player override. Registered as a plugin-scoped singleton; all inventory
    /// access runs on the main thread.
    /// </summary>
    public class VaultService
    {
        private const string DataKey = "vault";

        private readonly IConfiguration m_Configuration;
        private readonly IPermissionChecker m_PermissionChecker;
        private IDataStore? m_DataStore;
        private VaultData m_Data = new VaultData();

        public VaultService(IConfiguration configuration, IPermissionChecker permissionChecker)
        {
            m_Configuration = configuration;
            m_PermissionChecker = permissionChecker;
        }

        private VaultSettings Settings => m_Configuration.Get<VaultSettings>() ?? new VaultSettings();

        /// <summary>The configured base capacity (before tiers / per-player overrides).</summary>
        public int BaseMaxSlots => Settings.MaxSlots;

        /// <summary>Loads persisted data once. Called by the plugin on load (DataStore is plugin-owned).</summary>
        public async Task InitializeAsync(IDataStore dataStore)
        {
            m_DataStore = dataStore;
            if (await dataStore.ExistsAsync(DataKey))
            {
                m_Data = await dataStore.LoadAsync<VaultData>(DataKey) ?? new VaultData();
            }

            // Backfill unique ids for items stored before per-copy ids existed (so they can be
            // withdrawn individually in the detail view).
            var changed = false;
            foreach (var list in m_Data.Players.Values)
            {
                foreach (var item in list)
                {
                    if (string.IsNullOrEmpty(item.Uid))
                    {
                        item.Uid = NewUid();
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                await SaveAsync();
            }
        }

        // ----- capacity -----

        /// <summary>The effective capacity for an online player: a per-player override, else the largest of
        /// the config base and every permission tier the player holds.</summary>
        public async Task<int> GetMaxSlotsAsync(UnturnedUser user)
        {
            if (m_Data.Overrides.TryGetValue(user.Id, out var overridden))
            {
                return overridden;
            }

            var settings = Settings;
            var best = settings.MaxSlots;
            foreach (var tier in settings.Tiers)
            {
                if (await m_PermissionChecker.CheckPermissionAsync(user, tier.Key).ConfigureAwait(false) == PermissionGrantResult.Grant)
                {
                    best = Math.Max(best, tier.Value);
                }
            }

            return best;
        }

        /// <summary>Capacity known without the online user: a per-player override, else the config base.</summary>
        public int OverrideOrBase(string steamId)
            => m_Data.Overrides.TryGetValue(steamId, out var overridden) ? overridden : Settings.MaxSlots;

        public IReadOnlyDictionary<string, int> Overrides => new Dictionary<string, int>(m_Data.Overrides);

        public async Task SetOverrideAsync(string steamId, int slots)
        {
            m_Data.Overrides[steamId] = Math.Max(1, slots);
            await SaveAsync();
        }

        public async Task<bool> ClearOverrideAsync(string steamId)
        {
            if (m_Data.Overrides.Remove(steamId))
            {
                await SaveAsync();
                return true;
            }

            return false;
        }

        // ----- queries -----

        /// <summary>A snapshot of a player's stored items.</summary>
        public IReadOnlyList<StoredItem> Get(string steamId)
            => m_Data.Players.TryGetValue(steamId, out var list) ? list.ToList() : new List<StoredItem>();

        /// <summary>Grid cells currently used by a player's vault.</summary>
        public int UsedSlots(string steamId)
            => m_Data.Players.TryGetValue(steamId, out var list) ? list.Sum(x => x.SlotCost) : 0;

        /// <summary>The grid footprint of one item id (size_x × size_y), or 1 if the asset is unknown.</summary>
        public static int SlotCostOf(ushort itemId)
        {
            var asset = Assets.find(EAssetType.ITEM, itemId) as ItemAsset;
            return asset == null ? 1 : Math.Max(1, asset.size_x * asset.size_y);
        }

        /// <summary>Groups a player's carried items by id (skips equipment slots and any open storage).</summary>
        public async Task<IReadOnlyList<BackpackEntry>> ListBackpackAsync(UnturnedUser user)
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Player.inventory;
            var counts = new Dictionary<ushort, int>();
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

                    counts.TryGetValue(jar.item.id, out var existing);
                    counts[jar.item.id] = existing + 1;
                }
            }

            return counts.Select(kv => new BackpackEntry(kv.Key, kv.Value)).ToList();
        }

        // ----- store / take -----

        /// <summary>
        /// Moves up to <paramref name="amount"/> copies of <paramref name="itemId"/> from the player's
        /// backpack into the vault, preserving each item's full state. Stops early when capacity is
        /// reached. Returns how many were stored and whether the cap was hit.
        /// </summary>
        public async Task<StoreResult> StoreAsync(UnturnedUser user, ushort itemId, int amount)
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Player.inventory;
            var steamId = user.Id;

            var targets = new List<(byte page, ItemJar jar)>();
            for (byte page = PlayerInventory.SLOTS; page < PlayerInventory.STORAGE && targets.Count < amount; page++)
            {
                var count = inventory.getItemCount(page);
                for (byte i = 0; i < count && targets.Count < amount; i++)
                {
                    var jar = inventory.getItem(page, i);
                    if (jar?.item != null && jar.item.id == itemId)
                    {
                        targets.Add((page, jar));
                    }
                }
            }

            var list = ListFor(steamId);
            var used = UsedSlots(steamId);
            var max = await GetMaxSlotsAsync(user).ConfigureAwait(false);
            var cost = SlotCostOf(itemId);
            var stored = 0;
            var capacityReached = false;

            foreach (var (page, jar) in targets)
            {
                if (used + cost > max)
                {
                    capacityReached = true;
                    break;
                }

                var item = jar.item;
                list.Add(new StoredItem
                {
                    Uid = NewUid(),
                    ItemId = item.id,
                    Amount = item.amount,
                    Quality = item.quality,
                    State = item.state != null && item.state.Length > 0 ? Convert.ToBase64String(item.state) : string.Empty,
                    SlotCost = cost
                });
                used += cost;
                stored++;

                var index = inventory.getIndex(page, jar.x, jar.y);
                inventory.removeItem(page, index);
            }

            if (stored > 0)
            {
                await SaveAsync();
            }

            return new StoreResult(stored, capacityReached);
        }

        /// <summary>
        /// Moves up to <paramref name="amount"/> copies of <paramref name="itemId"/> from the vault
        /// back into the backpack (dropping at the player's feet if full), restoring full state.
        /// Returns how many were taken. Takes the earliest-stored copies first.
        /// </summary>
        public async Task<int> TakeAsync(UnturnedUser user, ushort itemId, int amount)
        {
            await UniTask.SwitchToMainThread();
            var player = user.Player.Player;
            var list = ListFor(user.Id);
            var taken = 0;

            for (var n = 0; n < amount; n++)
            {
                var idx = list.FindIndex(x => x.ItemId == itemId);
                if (idx < 0)
                {
                    break;
                }

                GiveBack(player, list[idx]);
                list.RemoveAt(idx);
                taken++;
            }

            if (taken > 0)
            {
                await SaveAsync();
            }

            return taken;
        }

        /// <summary>Withdraws exactly one stored copy by its <see cref="StoredItem.Uid"/>. Returns false if absent.</summary>
        public async Task<bool> TakeByUidAsync(UnturnedUser user, string uid)
        {
            await UniTask.SwitchToMainThread();
            var list = ListFor(user.Id);
            var idx = list.FindIndex(x => x.Uid == uid);
            if (idx < 0)
            {
                return false;
            }

            GiveBack(user.Player.Player, list[idx]);
            list.RemoveAt(idx);
            await SaveAsync();
            return true;
        }

        private static void GiveBack(Player player, StoredItem stored)
        {
            var state = string.IsNullOrEmpty(stored.State) ? Array.Empty<byte>() : Convert.FromBase64String(stored.State);
            var item = new Item(stored.ItemId, stored.Amount, stored.Quality, state);
            if (!player.inventory.tryAddItem(item, true))
            {
                ItemManager.dropItem(item, player.transform.position, true, true, false);
            }
        }

        private List<StoredItem> ListFor(string steamId)
        {
            if (!m_Data.Players.TryGetValue(steamId, out var list))
            {
                list = new List<StoredItem>();
                m_Data.Players[steamId] = list;
            }

            return list;
        }

        private static string NewUid() => Guid.NewGuid().ToString("N").Substring(0, 8);

        private async Task SaveAsync()
        {
            if (m_DataStore != null)
            {
                await m_DataStore.SaveAsync(DataKey, m_Data);
            }
        }
    }
}
