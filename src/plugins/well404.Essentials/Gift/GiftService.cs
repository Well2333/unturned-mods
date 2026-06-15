using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.API.Permissions;
using OpenMod.Extensions.Games.Abstractions.Items;
using OpenMod.Unturned.Users;
using well404.Essentials.Data;

namespace well404.Essentials.Gift
{
    public enum GiftClaimStatus
    {
        Claimed,
        NotFound,
        NoPermission,
        OnCooldown
    }

    /// <summary>One claimable gift's state for the <c>/gift</c> list.</summary>
    public sealed class GiftListing
    {
        public GiftListing(string id, string name, bool ready, double refreshInSeconds)
        {
            Id = id;
            Name = name;
            Ready = ready;
            RefreshInSeconds = refreshInSeconds;
        }

        public string Id { get; }
        public string Name { get; }
        public bool Ready { get; }

        /// <summary>Seconds until the next refresh when not <see cref="Ready"/> (0 otherwise).</summary>
        public double RefreshInSeconds { get; }
    }

    public sealed class GiftClaimResult
    {
        public GiftClaimResult(GiftClaimStatus status, string giftName = "", double refreshInSeconds = 0)
        {
            Status = status;
            GiftName = giftName;
            RefreshInSeconds = refreshInSeconds;
        }

        public GiftClaimStatus Status { get; }
        public string GiftName { get; }
        public double RefreshInSeconds { get; }
    }

    /// <summary>
    /// Free gift packs with crontab refresh rules and optional per-gift (VIP) permissions.
    /// Definitions come from the shared <see cref="EssentialsConfigStore"/>; per-player claim
    /// times are persisted in <see cref="PlayerDataStore"/>. Cron windows are evaluated in
    /// <b>server local time</b>; a player may claim once per cron period. Registered as a
    /// plugin-scoped singleton.
    /// </summary>
    public sealed class GiftService
    {
        private readonly EssentialsConfigStore m_Store;
        private readonly PlayerDataStore m_PlayerData;
        private readonly IPermissionChecker m_PermissionChecker;
        private readonly IItemSpawner m_ItemSpawner;

        public GiftService(
            EssentialsConfigStore store,
            PlayerDataStore playerData,
            IPermissionChecker permissionChecker,
            IItemSpawner itemSpawner)
        {
            m_Store = store;
            m_PlayerData = playerData;
            m_PermissionChecker = permissionChecker;
            m_ItemSpawner = itemSpawner;
        }

        private static GiftEntry? Find(IReadOnlyList<GiftEntry> gifts, string id)
        {
            foreach (var gift in gifts)
            {
                if (string.Equals(gift.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return gift;
                }
            }

            return null;
        }

        private async Task<bool> HasPermissionAsync(UnturnedUser user, GiftEntry gift)
            => string.IsNullOrWhiteSpace(gift.Permission)
               || await m_PermissionChecker.CheckPermissionAsync(user, gift.Permission) == PermissionGrantResult.Grant;

        /// <summary>Lists the gifts this player is allowed to see (permission-gated), with status.</summary>
        public async Task<IReadOnlyList<GiftListing>> GetListingsAsync(UnturnedUser user)
        {
            var now = DateTime.Now;
            var result = new List<GiftListing>();
            foreach (var gift in m_Store.Gifts)
            {
                if (!await HasPermissionAsync(user, gift))
                {
                    continue;
                }

                var lastClaim = await m_PlayerData.GetGiftClaimAsync(user.Id, gift.Id);
                var ready = GiftEligibility.IsClaimable(gift.Cron, lastClaim, now, out var refreshIn);
                result.Add(new GiftListing(gift.Id, gift.Name, ready, refreshIn));
            }

            return result;
        }

        public async Task<GiftClaimResult> ClaimAsync(UnturnedUser user, string id)
        {
            var gift = Find(m_Store.Gifts, id);
            if (gift == null)
            {
                return new GiftClaimResult(GiftClaimStatus.NotFound);
            }

            if (!await HasPermissionAsync(user, gift))
            {
                return new GiftClaimResult(GiftClaimStatus.NoPermission);
            }

            var lastClaim = await m_PlayerData.GetGiftClaimAsync(user.Id, gift.Id);
            if (!GiftEligibility.IsClaimable(gift.Cron, lastClaim, DateTime.Now, out var refreshIn))
            {
                return new GiftClaimResult(GiftClaimStatus.OnCooldown, gift.Name, refreshIn);
            }

            await GiveAsync(user, gift);
            await m_PlayerData.SetGiftClaimAsync(user.Id, gift.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return new GiftClaimResult(GiftClaimStatus.Claimed, gift.Name);
        }

        private async Task GiveAsync(UnturnedUser user, GiftEntry gift)
        {
            await UniTask.SwitchToMainThread();
            var inventory = user.Player.Inventory;
            foreach (var item in gift.Items)
            {
                if (item.ItemId == 0 || item.Amount < 1)
                {
                    continue;
                }

                var assetId = item.ItemId.ToString();
                for (var i = 0; i < item.Amount; i++)
                {
                    // GiveItemAsync drops the item on the ground if the inventory is full.
                    await m_ItemSpawner.GiveItemAsync(inventory, assetId);
                }
            }
        }
    }
}
