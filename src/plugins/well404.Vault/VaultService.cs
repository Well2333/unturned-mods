using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OpenMod.API.Permissions;
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
    /// A raw item variant (copies with identical id + amount + quality + state) and
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
    /// Threading: SDG inventory/asset access runs on the Unity main thread; SQLite access is
    /// serialized by SqliteVaultStore and each mutation is committed transactionally.
    /// </summary>
    public class VaultService
    {
        private readonly IConfiguration m_Configuration;
        private readonly IPermissionChecker m_PermissionChecker;
        private SqliteVaultStore? m_Store;

        private SqliteVaultStore Store => m_Store
            ?? throw new InvalidOperationException("The vault database is not initialized.");

        public VaultService(IConfiguration configuration, IPermissionChecker permissionChecker)
        {
            m_Configuration = configuration;
            m_PermissionChecker = permissionChecker;
        }

        private VaultSettings Settings => m_Configuration.Get<VaultSettings>() ?? new VaultSettings();

        /// <summary>The configured base capacity (before tiers / per-player overrides), at least 1.</summary>
        public int BaseMaxSlots => Math.Max(1, Settings.MaxSlots);

        /// <summary>Creates the SQLite schema. Existing YAML data is intentionally ignored.</summary>
        public Task InitializeAsync(string databasePath)
        {
            m_Store = new SqliteVaultStore(databasePath);
            m_Store.Initialize();
            return Task.CompletedTask;
        }

        // ----- capacity -----

        /// <summary>The effective capacity for an online player: a per-player override, else the largest of
        /// the config base and every permission tier the player holds. Never below 1.</summary>
        public async Task<int> GetMaxSlotsAsync(UnturnedUser user)
        {
            var overridden = Store.GetOverride(user.Id);
            if (overridden.HasValue)
            {
                return Math.Max(1, overridden.Value);
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

        /// <summary>Capacity known without the online user: a per-player override, else the config base.</summary>
        public int OverrideOrBase(string steamId)
            => Math.Max(1, Store.GetOverride(steamId) ?? Settings.MaxSlots);

        public IReadOnlyDictionary<string, int> Overrides => Store.GetOverrides();

        public Task SetOverrideAsync(string steamId, int slots)
        {
            Store.SetOverride(steamId, Math.Max(1, slots));
            return Task.CompletedTask;
        }

        public Task<bool> ClearOverrideAsync(string steamId)
            => Task.FromResult(Store.ClearOverride(steamId));

        // ----- queries -----

        /// <summary>A snapshot of a player's stored items.</summary>
        public IReadOnlyList<StoredItem> Get(string steamId) => Store.Get(steamId);

        /// <summary>Grid cells currently used by a player's vault.</summary>
        public int UsedSlots(string steamId) => Store.UsedSlots(steamId);

        /// <summary>
        /// Updates the editable fields of one stored row. Asset-derived capacity metadata is
        /// recalculated on Unity's main thread and the opaque item state is intentionally preserved.
        /// </summary>
        public async Task<bool> UpdateStoredItemAsync(
            string steamId, long recordId, ushort itemId, byte amount, byte quality)
        {
            await UniTask.SwitchToMainThread();
            return Store.UpdateItem(
                steamId,
                recordId,
                itemId,
                amount,
                quality,
                SlotCostOf(itemId),
                MaxAmountOf(itemId));
        }

        public Task<bool> DeleteStoredItemAsync(string steamId, long recordId)
            => Task.FromResult(Store.DeleteItem(steamId, recordId));

        public Task<int> DeleteStoredItemsAsync(string steamId, ushort itemId)
            => Task.FromResult(Store.DeleteItems(steamId, itemId));

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
            var result = new List<ItemVariant>();
            foreach (var group in Store.Get(steamId)
                .GroupBy(x => (x.ItemId, x.Amount, x.Quality, x.State))
                .OrderBy(g => g.Key.ItemId))
            {
                var first = group.First();
                result.Add(new ItemVariant(first.ItemId, first.Amount, first.Quality, first.State,
                    group.Count(), first.SlotCost, first.MaxAmount));
            }

            return result;
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

        /// <summary>Stores up to <paramref name="count"/> copies matching amount and state; quality is optional for assets that do not expose durability.</summary>
        public Task<StoreResult> StoreVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte? quality, byte[] state, int count)
            => StoreMatchingAsync(user, itemId, it => it.amount == amount
                && (!quality.HasValue || it.quality == quality.Value)
                && BytesEqual(it.state, state), count);

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
                Store.AddItems(user.Id, toAdd);
            }

            return new StoreResult(toAdd.Count, capacityReached);
        }

        // ----- take -----

        /// <summary>Withdraws up to <paramref name="amount"/> copies of <paramref name="itemId"/> (any variant).</summary>
        public Task<int> TakeAsync(UnturnedUser user, ushort itemId, int amount)
            => TakeMatchingAsync(user, x => x.ItemId == itemId, amount);

        /// <summary>Withdraws up to <paramref name="count"/> copies matching amount and state; quality is optional for assets that do not expose durability.</summary>
        public Task<int> TakeVariantAsync(UnturnedUser user, ushort itemId, byte amount, byte? quality, string state, int count)
            => TakeMatchingAsync(user, x => x.ItemId == itemId && x.Amount == amount
                && (!quality.HasValue || x.Quality == quality.Value)
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
            for (var n = 0; n < amount; n++)
            {
                var entry = Store.TakeFirst(user.Id, matches);
                if (entry == null)
                {
                    break;
                }

                if (GiveBack(player, entry))
                {
                    taken++;
                }
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

    }
}
