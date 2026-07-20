using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.API.Commands;
using OpenMod.API.Users;
using OpenMod.Core.Users;
using OpenMod.Extensions.Economy.Abstractions;
using OpenMod.Unturned.Users;

namespace well404.Economy.Currency
{
    /// <summary>
    /// Uses the native Unturned experience points as the currency balance. Only
    /// works for <b>online</b> players (XP lives on the live <c>PlayerSkills</c>),
    /// so operations on offline players raise a user-friendly error.
    /// </summary>
    public sealed class ExperienceCurrencyBackend : ICurrencyBackend
    {
        private readonly IUserManager m_UserManager;

        public ExperienceCurrencyBackend(IUserManager userManager)
        {
            m_UserManager = userManager;
        }

        private async Task<UnturnedUser> GetOnlineUserAsync(string ownerId, string ownerType)
        {
            if (!string.Equals(ownerType, KnownActorTypes.Player, StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(
                    "The experience backend only supports player accounts.");
            }

            var user = await m_UserManager.FindUserAsync(
                KnownActorTypes.Player, ownerId, UserSearchMode.FindById) as UnturnedUser;

            if (user == null)
            {
                throw new UserFriendlyException(
                    "The player must be online for experience-based currency operations.");
            }

            return user;
        }

        public async Task<decimal> GetBalanceAsync(string ownerId, string ownerType)
        {
            var user = await GetOnlineUserAsync(ownerId, ownerType);
            await UniTask.SwitchToMainThread();
            return user.Player.Player.skills.experience;
        }

        public async Task<IReadOnlyList<AccountSnapshot>> ListAccountsAsync()
        {
            // XP is not persisted, so only currently online players can be listed.
            var users = await m_UserManager.GetUsersAsync(KnownActorTypes.Player);
            await UniTask.SwitchToMainThread();

            var result = new List<AccountSnapshot>();
            foreach (var user in users)
            {
                if (user is UnturnedUser unturnedUser)
                {
                    decimal experience = unturnedUser.Player.Player.skills.experience;
                    result.Add(new AccountSnapshot(KnownActorTypes.Player, unturnedUser.Id, experience));
                }
            }

            return result;
        }

        public async Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason)
        {
            RequireWholeExperience(changeAmount);
            var user = await GetOnlineUserAsync(ownerId, ownerType);
            await UniTask.SwitchToMainThread();

            var skills = user.Player.Player.skills;
            decimal current = skills.experience;
            var updated = current + changeAmount;

            if (updated < 0m)
            {
                throw new NotEnoughBalanceException(
                    $"Not enough experience: needs {-changeAmount}, has {current}.", current);
            }

            var applied = ClampToUInt(updated);
            skills.ServerSetExperience(applied);
            return applied;
        }

        public async Task SetBalanceAsync(string ownerId, string ownerType, decimal balance)
        {
            RequireWholeExperience(balance);
            var user = await GetOnlineUserAsync(ownerId, ownerType);
            await UniTask.SwitchToMainThread();
            user.Player.Player.skills.ServerSetExperience(ClampToUInt(balance));
        }

        public Task DeleteAccountAsync(string ownerId, string ownerType)
            => throw new UserFriendlyException("经验后端不支持删除账户（经验值不作为独立账本持久化）。");

        private static void RequireWholeExperience(decimal value)
        {
            if (decimal.Truncate(value) != value)
                throw new UserFriendlyException("Experience-based currency only supports whole-number amounts.");
        }

        private static uint ClampToUInt(decimal value)
        {
            if (value <= 0m)
            {
                return 0u;
            }

            return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
        }
    }
}
