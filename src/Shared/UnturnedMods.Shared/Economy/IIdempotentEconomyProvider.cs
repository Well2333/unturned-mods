using System.Threading.Tasks;
using OpenMod.API.Ioc;

namespace UnturnedMods.Shared.Economy
{
    [Service]
    public interface IIdempotentEconomyProvider
    {
        bool SupportsDurableOperations { get; }

        Task<decimal> ApplyOnceAsync(
            string operationId,
            string ownerId,
            string ownerType,
            decimal changeAmount,
            string reason);

        Task<decimal?> GetAppliedBalanceAsync(
            string operationId,
            string ownerId,
            string ownerType,
            decimal changeAmount);
    }
}
