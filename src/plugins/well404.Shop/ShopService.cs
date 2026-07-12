using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;

namespace well404.Shop
{
    /// <summary>
    /// Handles the inventory side of buying and selling: granting items (single
    /// items or bundle contents) and taking them back. All inventory access runs
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

        /// <summary>The total item-count required (or granted) per purchase unit, aggregated by asset id.</summary>
        private static Dictionary<ushort, int> ItemsPerUnit(ShopEntry entry)
        {
            var result = new Dictionary<ushort, int>();
            if (entry.IsBundle)
            {
                foreach (var content in entry.Contents)
                {
                    result.TryGetValue(content.ItemId, out var existing);
                    result[content.ItemId] = existing + content.Amount;
                }
            }
            else
            {
                // A plain item grants exactly one of itself per purchase unit.
                result[entry.ItemId] = 1;
            }

            return result;
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
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Inventory;

            var required = ItemsPerUnit(entry);
            var matches = new Dictionary<ushort, List<IInventoryItem>>();

            // Verify everything first so we never remove a partial order.
            foreach (var req in required)
            {
                var assetId = req.Key.ToString();
                var list = new List<IInventoryItem>();
                double owned = 0;

                foreach (var page in inventory.Pages)
                {
                    foreach (var item in page.Items)
                    {
                        if (item.Item.Asset.ItemAssetId == assetId)
                        {
                            list.Add(item);
                            owned += item.Item.State.ItemAmount;
                        }
                    }
                }

                if (owned < req.Value * units)
                {
                    return false;
                }

                matches[req.Key] = list;
            }

            foreach (var req in required)
            {
                var remaining = req.Value * units;
                foreach (var item in matches[req.Key])
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    var amount = (int)item.Item.State.ItemAmount;
                    if (amount <= remaining)
                    {
                        await item.DestroyAsync();
                        remaining -= amount;
                    }
                    else
                    {
                        await item.Item.SetItemAmountAsync(amount - remaining);
                        remaining = 0;
                    }
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
                    var amount = (int)item.Item.State.ItemAmount;
                    if (amount > 0)
                    {
                        removals.Add(new PendingRemoval(itemId, amount, item));
                    }
                }
            }

            var removed = new Dictionary<ushort, int>();
            foreach (var removal in removals)
            {
                await removal.Item.DestroyAsync();
                removed.TryGetValue(removal.ItemId, out var previous);
                removed[removal.ItemId] = previous + removal.Amount;
            }
            return removed;
        }

        private sealed class PendingRemoval
        {
            public PendingRemoval(ushort itemId, int amount, IInventoryItem item)
            {
                ItemId = itemId;
                Amount = amount;
                Item = item;
            }
            public ushort ItemId { get; }
            public int Amount { get; }
            public IInventoryItem Item { get; }
        }
    }
}
