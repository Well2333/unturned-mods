using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Shop
{
    /// <summary>
    /// Handles the inventory side of buying and selling: granting catalog items
    /// and taking them back. All inventory access runs
    /// on the main thread. Registered as a plugin-scoped singleton in
    /// <see cref="ShopContainerConfigurator"/>.
    /// </summary>
    public class ShopService
    {
        private readonly IItemSpawner m_ItemSpawner;

        public ShopService(IItemSpawner itemSpawner)
        {
            m_ItemSpawner = itemSpawner;
        }

        /// <summary>The item-count required (or granted) per purchase unit.</summary>
        internal static Dictionary<ushort, int> ItemsPerUnit(ShopEntry entry)
            => new Dictionary<ushort, int> { [entry.ItemId] = 1 };

        public static int AvailableUnits(
            ShopEntry entry, IReadOnlyDictionary<ushort, int> inventoryCounts)
        {
            var units = int.MaxValue;
            foreach (var required in ItemsPerUnit(entry))
            {
                inventoryCounts.TryGetValue(required.Key, out var owned);
                units = System.Math.Min(units, owned / required.Value);
            }
            return units == int.MaxValue ? 0 : units;
        }

        public async Task<IReadOnlyDictionary<ushort, int>> GetInventoryCountsAsync(UnturnedUser user)
        {
            await UniTask.SwitchToMainThread();
            var counts = new Dictionary<ushort, int>();
            foreach (var page in user.Player.Inventory.Pages)
            {
                foreach (var item in page.Items)
                {
                    if (!ushort.TryParse(item.Item.Asset.ItemAssetId, NumberStyles.None,
                            CultureInfo.InvariantCulture, out var itemId))
                    {
                        continue;
                    }
                    counts.TryGetValue(itemId, out var previous);
                    // A shop unit is one inventory entry, matching GiveItemAsync. ItemAmount is
                    // asset-specific state (for example ammunition remaining in a magazine or
                    // ammunition crate), not the number of separately sellable item instances.
                    counts[itemId] = previous + 1;
                }
            }
            return counts;
        }

        public async Task GiveAsync(UnturnedUser user, ShopEntry entry, int units)
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Inventory;

            foreach (var kv in ItemsPerUnit(entry))
            {
                var assetId = kv.Key.ToString();
                var total = kv.Value * units;
                for (var i = 0; i < total; i++)
                {
                    // GiveItemAsync drops the item on the ground if the inventory is full.
                    await m_ItemSpawner.GiveItemAsync(inventory, assetId);
                }
            }
        }

        /// <summary>
        /// Verifies the player owns enough of every required item and, if so, removes
        /// them and returns true. Removes nothing and returns false if short.
        /// </summary>
        public async Task<bool> TryTakeAsync(UnturnedUser user, ShopEntry entry, int units)
            => await TryTakePlanAsync(user, new Dictionary<ushort, int>
            {
                [entry.ItemId] = units
            });

        /// <summary>
        /// Verifies an exact multi-item removal plan before destroying the first item. Returning
        /// false is proof that nothing was removed; an exception after destruction begins is
        /// intentionally ambiguous and must be quarantined by the trade coordinator.
        /// </summary>
        public async Task<bool> TryTakePlanAsync(
            UnturnedUser user, IReadOnlyDictionary<ushort, int> required)
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Inventory;
            var matches = new Dictionary<ushort, List<IInventoryItem>>();

            // Verify everything first so we never remove a partial order.
            foreach (var req in required)
            {
                var assetId = req.Key.ToString();
                var list = new List<IInventoryItem>();
                foreach (var page in inventory.Pages)
                {
                    foreach (var item in page.Items)
                    {
                        if (item.Item.Asset.ItemAssetId == assetId)
                        {
                            list.Add(item);
                        }
                    }
                }

                if (req.Value <= 0 || list.Count < req.Value)
                {
                    return false;
                }

                matches[req.Key] = list;
            }

            foreach (var req in required)
            {
                var remaining = req.Value;
                foreach (var item in matches[req.Key])
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    await item.DestroyAsync();
                    remaining--;
                }
            }

            return true;
        }

        /// <summary>Removes all inventory items matching the supplied catalog item ids.</summary>
        public async Task<IReadOnlyDictionary<ushort, int>> TakeAllAsync(
            UnturnedUser user, IEnumerable<ushort> itemIds)
        {
            await UniTask.SwitchToMainThread();
            var wanted = new HashSet<ushort>(itemIds);
            var removals = new List<PendingRemoval>();
            foreach (var page in user.Player.Inventory.Pages)
            {
                foreach (var item in page.Items)
                {
                    if (!ushort.TryParse(item.Item.Asset.ItemAssetId, NumberStyles.None,
                            CultureInfo.InvariantCulture, out var itemId) || !wanted.Contains(itemId))
                    {
                        continue;
                    }
                    removals.Add(new PendingRemoval(itemId, item));
                }
            }

            var removed = new Dictionary<ushort, int>();
            foreach (var removal in removals)
            {
                await removal.Item.DestroyAsync();
                removed.TryGetValue(removal.ItemId, out var previous);
                removed[removal.ItemId] = previous + 1;
            }
            return removed;
        }

        private sealed class PendingRemoval
        {
            public PendingRemoval(ushort itemId, IInventoryItem item)
            {
                ItemId = itemId;
                Item = item;
            }
            public ushort ItemId { get; }
            public IInventoryItem Item { get; }
        }
    }
}
