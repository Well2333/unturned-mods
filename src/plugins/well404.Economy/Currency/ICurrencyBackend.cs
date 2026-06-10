using System.Threading.Tasks;

namespace well404.Economy.Currency
{
    /// <summary>
    /// A pluggable storage backend for balances. The active backend is selected
    /// from <c>config.yaml</c> (<c>backend: database | experience</c>) and all
    /// reads/writes from <see cref="EconomyProvider"/> are routed to it.
    /// </summary>
    public interface ICurrencyBackend
    {
        Task<decimal> GetBalanceAsync(string ownerId, string ownerType);

        /// <summary>
        /// Atomically adds <paramref name="changeAmount"/> (may be negative) and returns the new balance.
        /// Throws <see cref="OpenMod.Extensions.Economy.Abstractions.NotEnoughBalanceException"/>
        /// if the balance would go negative.
        /// </summary>
        Task<decimal> UpdateBalanceAsync(string ownerId, string ownerType, decimal changeAmount, string? reason);

        Task SetBalanceAsync(string ownerId, string ownerType, decimal balance);
    }
}
